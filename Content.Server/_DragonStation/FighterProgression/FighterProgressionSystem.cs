using System.Linq;
using System.Threading.Tasks;
using Content.Goobstation.Shared.MartialArts;
using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Server._DragonStation.FighterProgression;
using Content.Server.Dragon;
using Content.Server.Roles;
using Content.Shared.Roles;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Tag;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared._DragonStation.FighterProgression;
using Content.Shared._DragonStation.FighterProgression.Prototypes;
using Content.Shared._DragonStation.PowerLevel;
using Content.Shared._DragonStation.Transformations;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Content.Server.Preferences.Managers;
using Content.Shared.Rejuvenate;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Shared.Roles.Jobs;
using Content.Shared._DragonStation.FighterProgression.Prototypes;

namespace Content.Server._DragonStation.FighterProgression;

public sealed class FighterProgressionSystem : SharedFighterProgressionSystem
{
    private const float ThresholdFlatPerLevel = 12f;
    private const float ThresholdScalePerLevel = 1.06f;
    private const float CombatXpRatioFloor = 0.35f;
    private const float CombatXpRatioCeiling = 12f;
    private const float CombatXpRatioExponent = 1.35f;
    private const int CombatXpAwardFloor = 1;
    private const int CombatXpAwardCeiling = 20;
    private const float TrainingXpScalePerThreshold = 0.97f;
    private const int FallbackPowerLevel = 100;
    private const int NonBoxerXpAwardCap = 5;
    private const float SuperSaiyanAwakeningThresholdRatio = 0.9f;
    private static readonly TimeSpan TransformationProgressSaveInterval = TimeSpan.FromSeconds(30);
    private const string EliteSaiyanPathSkill = "FighterEliteSaiyanPath";
    private const string PrimalSaiyanPathSkill = "FighterPrimalSaiyanPath";
    private const string SuperSaiyanAwakenedNode = "FighterTransformationSuperSaiyanAwakened";

    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedMartialArtsSystem _martialArts = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly SharedRoleSystem _roles = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IServerPreferencesManager _preferencesManager = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobThresholdSystem _mobThresholds = default!;
    [Dependency] private readonly TransformationSystem _transformations = default!;
    private static readonly ProtoId<TagPrototype> CarpTag = "Carp";

    /// <summary>
    /// Registers fighter progression event handlers for XP, UI, persistence, and spawn restoration.
    /// </summary>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<FighterProgressionComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<FighterProgressionComponent, FighterProgressionChangedEvent>(OnFighterProgressionChanged);
        SubscribeLocalEvent<FighterProgressionComponent, TransformationStateChangedEvent>(OnTransformationStateChanged);
        SubscribeLocalEvent<FighterProgressionComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<FighterProgressionComponent, FighterChooseBranchMessage>(OnChooseBranch);
        SubscribeLocalEvent<FighterProgressionComponent, PowerLevelRefreshRequestedEvent>(OnPowerLevelRefreshRequested);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<FighterProgressionComponent, SuperSaiyan1Component>();
        while (query.MoveNext(out var uid, out var progression, out var transformation))
        {
            if (!progression.SuperSaiyanUnlocked || !transformation.Active)
                continue;

            progression.TransformedSeconds += frameTime;

            if (TryAdvanceTransformationMastery(uid, progression))
            {
                UpdateUi(uid, progression);
                continue;
            }

            if (_timing.CurTime < progression.NextTransformationProgressSaveTime)
                continue;

            progression.NextTransformationProgressSaveTime = _timing.CurTime + TransformationProgressSaveInterval;
            UpdateUi(uid, progression);
            _ = SavePersistentProgression(uid, progression);
        }
    }

    /// <summary>
    /// Recomputes threshold-derived bonuses whenever progression state changes.
    /// </summary>
    private void OnFighterProgressionChanged(EntityUid uid, FighterProgressionComponent component, ref FighterProgressionChangedEvent args)
    {
        RefreshThresholdBonuses(uid, component);
    }

    /// <summary>
    /// Tracks the hidden Super Saiyan awakening trigger at the intended near-death threshold.
    /// </summary>
    private void OnDamageChanged(EntityUid uid, FighterProgressionComponent component, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased)
            return;

        TryAwakenSuperSaiyan(uid, component);
    }

    /// <summary>
    /// Flushes transformation progress when the form state changes so long sessions are not lost on logout or restart.
    /// </summary>
    private void OnTransformationStateChanged(EntityUid uid, FighterProgressionComponent component, ref TransformationStateChangedEvent args)
    {
        if (args.Active)
        {
            component.NextTransformationProgressSaveTime = _timing.CurTime + TransformationProgressSaveInterval;
            return;
        }

        if (!component.SuperSaiyanUnlocked)
            return;

        _ = SavePersistentProgression(uid, component);
    }

    /// <summary>
    /// Ensures eligible spawned characters receive fighter progression and restores saved progress when present.
    /// </summary>
    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        var shouldEnsureComponent = args.JobId is "Boxer" or "VisitorBoxer" || args.Profile.FighterProgression != null;
        if (!shouldEnsureComponent)
            return;

        var component = EnsureComp<FighterProgressionComponent>(args.Mob);

        if (args.Profile.FighterProgression != null)
            RestorePersistentProgression(args.Mob, component, args.Profile.FighterProgression);

        UpdateUi(args.Mob, component);
    }

    /// <summary>
    /// Awards fighter XP for valid melee hits and advances active training or challenge progress.
    /// </summary>
    private void OnMeleeHit(MeleeHitEvent args)
    {
        if (!TryComp<FighterProgressionComponent>(args.User, out var component))
            return;

        var user = args.User;
        if (!args.IsHit || !IsFighterStyleWeapon(user, args.Weapon))
            return;

        var awardedCombat = false;
        var awardedTraining = false;
        var transformedHitAwarded = false;

        foreach (var hit in args.HitEntities.Distinct())
        {
            if (hit == user)
                continue;

            ProcessChallengeHit(user, component, hit);

            if (HasComp<FighterTrainingTargetComponent>(hit))
            {
                if (_timing.CurTime < component.NextTrainingXpTime)
                    continue;

                component.NextTrainingXpTime = _timing.CurTime + component.TrainingHitCooldown;
                AddXp(user, component, GetTrainingHitXp(component));
                awardedTraining = true;
                continue;
            }

            if (!HasComp<MobStateComponent>(hit))
                continue;

            if (component.SuperSaiyanUnlocked &&
                TryComp<SuperSaiyan1Component>(user, out var transformation) &&
                transformation.Active)
            {
                component.TransformedHits++;
                transformedHitAwarded = true;
            }

            AddXp(user, component, GetCombatHitXp(user, hit, component));
            awardedCombat = true;
        }

        if (transformedHitAwarded && TryAdvanceTransformationMastery(user, component))
            UpdateUi(user, component);

        if (awardedCombat || awardedTraining)
            UpdateUi(user, component);
    }

    /// <summary>
    /// Awards kill XP and completes kill-based fighter challenges when a valid fighter-origin kill occurs.
    /// </summary>
    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || args.Origin == null)
            return;

        var query = EntityQueryEnumerator<FighterProgressionComponent>();
        while (query.MoveNext(out var fighterUid, out var component))
        {
            if (!IsFighterStyleOrigin(fighterUid, args.Origin.Value))
                continue;

            RefreshMissedChallenges(component);

            var transformedKill = component.SuperSaiyanUnlocked &&
                TryComp<SuperSaiyan1Component>(fighterUid, out var transformation) &&
                transformation.Active;

            if (TryComp<PowerLevelComponent>(args.Target, out var targetPowerLevel))
                AddXp(fighterUid, component, Math.Max(1, targetPowerLevel.CurrentPowerLevel / 2));

            if (transformedKill)
            {
                component.TransformedKills++;
                if (TryAdvanceTransformationMastery(fighterUid, component))
                    UpdateUi(fighterUid, component);
            }

            TryCompleteKillChallenge(fighterUid, component, args.Target);
        }
    }

    /// <summary>
    /// Adds fighter XP, applying the non-boxer cap before threshold advancement and persistence updates.
    /// </summary>
    private void AddXp(EntityUid uid, FighterProgressionComponent component, int amount)
    {
        if (amount <= 0)
            return;

        if (!IsBoxerJob(uid))
            amount = Math.Min(amount, NonBoxerXpAwardCap);

        component.CurrentXp += amount;
        ProcessThresholdAdvancement(uid, component);
        Dirty(uid, component);
        _ = SavePersistentProgression(uid, component);
    }

    /// <summary>
    /// Adds an exact amount of fighter XP for debugging or test setup.
    /// </summary>
    public bool TryDebugAddXp(EntityUid uid, int amount, FighterProgressionComponent? component = null)
    {
        if (amount <= 0 || !Resolve(uid, ref component, false))
            return false;

        component.CurrentXp += amount;
        ProcessThresholdAdvancement(uid, component);
        Dirty(uid, component);
        _ = SavePersistentProgression(uid, component);
        UpdateUi(uid, component);
        return true;
    }

    /// <summary>
    /// Resets all fighter progression, hidden transformation state, and persisted counters for debugging.
    /// </summary>
    public bool TryDebugResetProgression(EntityUid uid, FighterProgressionComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return false;

        RestoreThresholdBaselines(uid, component);

        component.CurrentXp = 0;
        component.ThresholdsReached = 0;
        component.UnlockedSkills.Clear();
        component.PendingChoiceOptions.Clear();
        component.ClosedSkills.Clear();
        component.TransformationsPageUnlocked = false;
        component.SuperSaiyanUnlocked = false;
        component.UnlockedTransformationSkills.Clear();
        component.TransformedSeconds = 0f;
        component.TransformedHits = 0;
        component.TransformedKills = 0;
        component.ChallengeProgress.Clear();
        component.MissedChallengeSkills.Clear();
        component.BaseMobThresholds = null;
        component.BaseStaminaCritThreshold = null;
        component.NextTrainingXpTime = TimeSpan.Zero;
        component.NextTransformationProgressSaveTime = TimeSpan.Zero;

        if (TryComp<SuperSaiyan1Component>(uid, out var transformation) && transformation.Active)
            _transformations.DisableTransformation(uid, transformation);

        RaiseLocalEvent(uid, new FighterProgressionChangedEvent(), true);
        RaiseLocalEvent(uid, new PowerLevelRefreshRequestedEvent(), true);
        Dirty(uid, component);
        UpdateUi(uid, component);
        _ = SavePersistentProgression(uid, component);
        return true;
    }

    /// <summary>
    /// Returns whether the entity's current job should receive uncapped fighter XP awards.
    /// </summary>
    private bool IsBoxerJob(EntityUid uid)
    {
        if (!_mind.TryGetMind(uid, out var mindId, out _))
            return false;

        if (!_jobs.MindTryGetJobId(mindId, out var jobId) || jobId == null)
            return false;

        return jobId.Value.Id is "Boxer" or "VisitorBoxer";
    }

    /// <summary>
    /// Resolves a pending branch choice, closes sibling paths, and persists the result.
    /// </summary>
    private void OnChooseBranch(EntityUid uid, FighterProgressionComponent component, FighterChooseBranchMessage args)
    {
        if (!_prototype.TryIndex(args.SkillId, out FighterSkillPrototype? skill))
            return;

        var pendingOptions = GetPendingChoiceOptions(uid, component);
        if (!pendingOptions.Contains(skill.ID))
            return;

        var availability = GetAvailability(uid, skill, component);
        if (availability != FighterSkillAvailability.BranchChoiceAvailable)
            return;

        foreach (var option in pendingOptions)
        {
            if (option == skill.ID)
                continue;

            if (!component.ClosedSkills.Contains(option))
                component.ClosedSkills.Add(option);
        }

        foreach (var sibling in _prototype.EnumeratePrototypes<FighterSkillPrototype>())
        {
            if (sibling.ID == skill.ID)
                continue;

            if (!HaveMatchingPrerequisites(sibling, skill))
                continue;

            if (!component.ClosedSkills.Contains(sibling.ID))
                component.ClosedSkills.Add(sibling.ID);
        }

        component.PendingChoiceOptions.Clear();
        UnlockSkill(uid, component, skill);
        ProcessThresholdAdvancement(uid, component);
        Dirty(uid, component);
        UpdateUi(uid, component);
        _ = SavePersistentProgression(uid, component);
    }

    /// <summary>
    /// Refreshes the fighter progression UI when it is opened.
    /// </summary>
    private void OnUiOpened(EntityUid uid, FighterProgressionComponent component, BoundUIOpenedEvent args)
    {
        UpdateUi(uid, component);
    }

    /// <summary>
    /// Refreshes the fighter progression UI after external power-level updates.
    /// </summary>
    private void OnPowerLevelRefreshRequested(EntityUid uid, FighterProgressionComponent component, PowerLevelRefreshRequestedEvent args)
    {
        UpdateUi(uid, component);
    }

    /// <summary>
    /// Sends the current fighter skill tree state, XP, and power level to the bound UI.
    /// </summary>
    private void UpdateUi(EntityUid uid, FighterProgressionComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        RefreshMissedChallenges(component);

        var states = new List<FighterSkillState>();
        var transformationStates = new List<FighterTransformationSkillState>();

        foreach (var skill in _prototype.EnumeratePrototypes<FighterSkillPrototype>())
        {
            states.Add(new FighterSkillState(skill.ID, GetAvailability(uid, skill, component)));
        }

        if (component.TransformationsPageUnlocked)
        {
            foreach (var skill in _prototype.EnumeratePrototypes<FighterTransformationSkillPrototype>()
                         .OrderBy(skill => skill.RequiredTransformedSeconds)
                         .ThenBy(skill => skill.RequiredTransformedHits)
                         .ThenBy(skill => skill.RequiredTransformedKills))
            {
                transformationStates.Add(new FighterTransformationSkillState(
                    skill.ID,
                    GetTransformationAvailability(skill, component)));
            }
        }

        var powerLevel = CompOrNull<PowerLevelComponent>(uid)?.CurrentPowerLevel ?? 100;

        var state = new FighterSkillTreeBoundUserInterfaceState(
            component.ThresholdsReached,
            powerLevel,
            component.CurrentXp,
            GetCurrentXpThreshold(component),
            component.PendingChoiceOptions.Count > 0,
            states,
            component.TransformationsPageUnlocked,
            component.SuperSaiyanUnlocked,
            transformationStates,
            component.TransformedSeconds,
            component.TransformedHits,
            component.TransformedKills);

        _ui.SetUiState(uid, FighterSkillTreeUiKey.Key, state);
    }

    /// <summary>
    /// Consumes accumulated XP into threshold levels, auto-unlocks, or pending branch choices.
    /// </summary>
    private void ProcessThresholdAdvancement(EntityUid uid, FighterProgressionComponent component)
    {
        while (component.CurrentXp >= GetCurrentXpThreshold(component))
        {
            RefreshMissedChallenges(component);

            if (component.PendingChoiceOptions.Count > 0)
                break;

            var eligible = GetEligibleSkills(component).ToList();
            if (eligible.Count == 0)
                break;

            component.CurrentXp -= GetCurrentXpThreshold(component);
            component.ThresholdsReached++;

            if (eligible.Count == 1)
            {
                UnlockSkill(uid, component, eligible[0], "fighter-progression-threshold-earned");
                continue;
            }

            component.PendingChoiceOptions.Clear();
            foreach (var skill in eligible)
            {
                component.PendingChoiceOptions.Add(skill.ID);
            }
            _popup.PopupEntity(Loc.GetString("fighter-progression-branch-choice-ready"), uid, uid);
            break;
        }
    }

    /// <summary>
    /// Unlocks a fighter skill, applies its immediate side effects, and saves the new progression state.
    /// </summary>
    private void UnlockSkill(EntityUid uid, FighterProgressionComponent component, FighterSkillPrototype skill, string popupLoc = "fighter-progression-skill-unlocked")
    {
        if (component.UnlockedSkills.Contains(skill.ID))
            return;

        component.UnlockedSkills.Add(skill.ID);
        component.MissedChallengeSkills.Remove(skill.ID);
        component.ChallengeProgress.Remove(skill.ID);
        RefreshMissedChallenges(component);

        if (skill.ID == "FighterKiWarriorPath")
            StripKiWarriorBlockedGear(uid);

        if (skill.MartialArtUnlock != null)
            _martialArts.TryGrantMartialArtKnowledge(uid, skill.MartialArtUnlock.Value);

        RaiseLocalEvent(uid, new FighterProgressionChangedEvent(), true);
        RaiseLocalEvent(uid, new PowerLevelRefreshRequestedEvent(), true);
        _popup.PopupEntity(Loc.GetString(popupLoc, ("skill", Loc.GetString(skill.Name))), uid, uid);
        _ = SavePersistentProgression(uid, component);
    }

    /// <summary>
    /// Restores persistent fighter progression from a saved character profile onto the runtime component.
    /// </summary>
    private void RestorePersistentProgression(EntityUid uid, FighterProgressionComponent component, PersistentFighterProgression saved)
    {
        saved.EnsureValid();

        RestoreThresholdBaselines(uid, component);

        component.CurrentXp = saved.CurrentXp;
        component.ThresholdsReached = saved.ThresholdsReached;
        component.UnlockedSkills.Clear();
        component.ClosedSkills.Clear();
        component.PendingChoiceOptions.Clear();
        component.ChallengeProgress.Clear();
        component.MissedChallengeSkills.Clear();
        component.BaseMobThresholds = null;
        component.BaseStaminaCritThreshold = null;
        component.TransformationsPageUnlocked = saved.TransformationsPageUnlocked;
        component.SuperSaiyanUnlocked = saved.SuperSaiyanUnlocked;
        component.UnlockedTransformationSkills.Clear();
        component.TransformedSeconds = saved.TransformedSeconds;
        component.TransformedHits = saved.TransformedHits;
        component.TransformedKills = saved.TransformedKills;

        foreach (var skillId in saved.UnlockedSkills)
        {
            if (_prototype.HasIndex<FighterSkillPrototype>(skillId))
                component.UnlockedSkills.Add(skillId);
        }

        foreach (var skillId in saved.ClosedSkills)
        {
            if (_prototype.HasIndex<FighterSkillPrototype>(skillId))
                component.ClosedSkills.Add(skillId);
        }

        foreach (var skillId in saved.UnlockedTransformationSkills)
        {
            if (_prototype.HasIndex<FighterTransformationSkillPrototype>(skillId))
                component.UnlockedTransformationSkills.Add(skillId);
        }

        ApplyUnlockedSkillSideEffects(uid, component);
        RaiseLocalEvent(uid, new FighterProgressionChangedEvent(), true);
        RaiseLocalEvent(uid, new PowerLevelRefreshRequestedEvent(), true);
        Dirty(uid, component);
    }

    /// <summary>
    /// Reapplies unlock side effects that are not represented purely by stored numeric bonuses.
    /// </summary>
    private void ApplyUnlockedSkillSideEffects(EntityUid uid, FighterProgressionComponent component)
    {
        foreach (var skillId in component.UnlockedSkills)
        {
            if (!_prototype.TryIndex(skillId, out FighterSkillPrototype? skill))
                continue;

            if (skill.MartialArtUnlock != null)
                _martialArts.TryGrantMartialArtKnowledge(uid, skill.MartialArtUnlock.Value);

        }

        if (component.UnlockedSkills.Any(skillId => skillId.Id == "FighterKiWarriorPath"))
            StripKiWarriorBlockedGear(uid);

        if (component.SuperSaiyanUnlocked)
            EnsureComp<SuperSaiyan1Component>(uid);
    }

    /// <summary>
    /// Builds the compact persistence payload stored on the character profile.
    /// </summary>
    private PersistentFighterProgression GetPersistentProgression(FighterProgressionComponent component)
    {
        return new PersistentFighterProgression
        {
            CurrentXp = component.CurrentXp,
            ThresholdsReached = component.ThresholdsReached,
            UnlockedSkills = component.UnlockedSkills.Select(skill => skill.Id).Distinct().ToList(),
            ClosedSkills = component.ClosedSkills.Select(skill => skill.Id).Distinct().ToList(),
            TransformationsPageUnlocked = component.TransformationsPageUnlocked,
            SuperSaiyanUnlocked = component.SuperSaiyanUnlocked,
            UnlockedTransformationSkills = component.UnlockedTransformationSkills.Select(skill => skill.Id).Distinct().ToList(),
            TransformedSeconds = component.TransformedSeconds,
            TransformedHits = component.TransformedHits,
            TransformedKills = component.TransformedKills,
        };
    }

    /// <summary>
    /// Persists the fighter progression payload back into the player's selected humanoid profile.
    /// </summary>
    private async Task SavePersistentProgression(EntityUid uid, FighterProgressionComponent component)
    {
        try
        {
            if (!_playerManager.TryGetSessionByEntity(uid, out var session))
                return;

            if (!_preferencesManager.TryGetCachedPreferences(session.UserId, out var prefs))
                return;

            if (!prefs.Characters.TryGetValue(prefs.SelectedCharacterIndex, out var selectedProfile))
                return;

            if (selectedProfile is not HumanoidCharacterProfile humanoid)
                return;

            var payload = GetPersistentProgression(component);
            var updatedProfile = humanoid.WithFighterProgression(payload);
            await _preferencesManager.SetProfile(session.UserId, prefs.SelectedCharacterIndex, updatedProfile);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save fighter progression for {ToPrettyString(uid)}: {ex}");
        }
    }

    /// <summary>
    /// Returns the current availability state for a transformation mastery node on the hidden page.
    /// </summary>
    private FighterSkillAvailability GetTransformationAvailability(FighterTransformationSkillPrototype skill, FighterProgressionComponent component)
    {
        if (!component.TransformationsPageUnlocked)
            return FighterSkillAvailability.Hidden;

        if (component.UnlockedTransformationSkills.Contains(skill.ID))
            return FighterSkillAvailability.Unlocked;

        if (skill.Prerequisites.Any() && !skill.Prerequisites.All(component.UnlockedTransformationSkills.Contains))
            return FighterSkillAvailability.Locked;

        return IsTransformationSkillReady(skill, component)
            ? FighterSkillAvailability.NextAutoUnlock
            : FighterSkillAvailability.RequirementLocked;
    }

    /// <summary>
    /// Checks whether the entity's persistent transformed-use counters satisfy a mastery node.
    /// </summary>
    private bool IsTransformationSkillReady(FighterTransformationSkillPrototype skill, FighterProgressionComponent component)
    {
        return component.TransformedSeconds >= skill.RequiredTransformedSeconds
            && component.TransformedHits >= skill.RequiredTransformedHits
            && component.TransformedKills >= skill.RequiredTransformedKills;
    }

    /// <summary>
    /// Auto-unlocks any newly satisfied transformation mastery nodes in linear order.
    /// </summary>
    private bool TryAdvanceTransformationMastery(EntityUid uid, FighterProgressionComponent component)
    {
        if (!component.TransformationsPageUnlocked)
            return false;

        var changed = false;
        var orderedSkills = _prototype.EnumeratePrototypes<FighterTransformationSkillPrototype>()
            .OrderBy(skill => skill.RequiredTransformedSeconds)
            .ThenBy(skill => skill.RequiredTransformedHits)
            .ThenBy(skill => skill.RequiredTransformedKills)
            .ToList();

        foreach (var skill in orderedSkills)
        {
            if (component.UnlockedTransformationSkills.Contains(skill.ID))
                continue;

            if (skill.Prerequisites.Any() && !skill.Prerequisites.All(component.UnlockedTransformationSkills.Contains))
                break;

            if (!IsTransformationSkillReady(skill, component))
                break;

            component.UnlockedTransformationSkills.Add(skill.ID);
            changed = true;
            _popup.PopupEntity(Loc.GetString("fighter-progression-transformation-unlocked",
                ("skill", Loc.GetString(skill.Name))), uid, uid);
        }

        if (!changed)
            return false;

        RaiseLocalEvent(uid, new FighterProgressionChangedEvent(), true);
        RaiseLocalEvent(uid, new PowerLevelRefreshRequestedEvent(), true);
        Dirty(uid, component);
        _ = SavePersistentProgression(uid, component);
        return true;
    }

    /// <summary>
    /// Triggers the one-time hidden Super Saiyan awakening when an eligible Saiyan reaches the deep critical band.
    /// </summary>
    private void TryAwakenSuperSaiyan(EntityUid uid, FighterProgressionComponent component)
    {
        if (component.SuperSaiyanUnlocked)
            return;

        if (!HasSkill(uid, EliteSaiyanPathSkill, component) && !HasSkill(uid, PrimalSaiyanPathSkill, component))
            return;

        if (!TryComp<DamageableComponent>(uid, out var damageable) ||
            !TryComp<MobThresholdsComponent>(uid, out var thresholds))
            return;

        if (!_mobThresholds.TryGetThresholdForState(uid, MobState.Critical, out var critThresholdValue, thresholds) ||
            !_mobThresholds.TryGetDeadThreshold(uid, out var deadThresholdValue, thresholds) ||
            critThresholdValue == null ||
            deadThresholdValue == null)
            return;

        var critThreshold = critThresholdValue.Value.Float();
        var deadThreshold = deadThresholdValue.Value.Float();
        var currentDamage = damageable.TotalDamage.Float();
        var awakeningThreshold = critThreshold + (deadThreshold - critThreshold) * SuperSaiyanAwakeningThresholdRatio;

        if (currentDamage < awakeningThreshold || currentDamage >= deadThreshold)
            return;

        component.TransformationsPageUnlocked = true;
        component.SuperSaiyanUnlocked = true;
        if (!component.UnlockedTransformationSkills.Contains(SuperSaiyanAwakenedNode))
            component.UnlockedTransformationSkills.Add(SuperSaiyanAwakenedNode);

        var transformation = EnsureComp<SuperSaiyan1Component>(uid);
        RaiseLocalEvent(uid, new RejuvenateEvent());
        _transformations.TryActivateTransformation(uid, transformation);

        RaiseLocalEvent(uid, new FighterProgressionChangedEvent(), true);
        RaiseLocalEvent(uid, new PowerLevelRefreshRequestedEvent(), true);
        _popup.PopupEntity(Loc.GetString("fighter-progression-super-saiyan-awakened"), uid, uid);
        Dirty(uid, component);
        UpdateUi(uid, component);
        _ = SavePersistentProgression(uid, component);
    }

    /// <summary>
    /// Removes armor slots blocked by the Ki Warrior commitment path.
    /// </summary>
    private void StripKiWarriorBlockedGear(EntityUid uid)
    {
        _inventory.TryUnequip(uid, "suitstorage", silent: true, force: true);
        _inventory.TryUnequip(uid, "head", silent: true, force: true);
        _inventory.TryUnequip(uid, "outerClothing", silent: true, force: true);
    }

    private void ProcessChallengeHit(EntityUid uid, FighterProgressionComponent component, EntityUid target)
    {
        RefreshMissedChallenges(component);

        foreach (var skill in GetActiveChallengeSkills(uid, component))
        {
            if (skill.ChallengeTarget == null || !MatchesChallengeTarget(target, skill.ChallengeTarget.Value))
                continue;

            var progress = GetChallengeProgress(component, skill.ID);
            if (progress.Target != target)
            {
                progress.Target = target;
                progress.Hits = 0;
            }

            progress.Hits++;

            if (!skill.ChallengeRequiresKill && progress.Hits >= skill.ChallengeRequiredHits)
            {
                UnlockSkill(uid, component, skill);
                UpdateUi(uid, component);
            }
        }
    }

    private void TryCompleteKillChallenge(EntityUid uid, FighterProgressionComponent component, EntityUid target)
    {
        foreach (var skill in GetActiveChallengeSkills(uid, component))
        {
            if (!skill.ChallengeRequiresKill || skill.ChallengeTarget == null)
                continue;

            if (!MatchesChallengeTarget(target, skill.ChallengeTarget.Value))
                continue;

            if (!component.ChallengeProgress.TryGetValue(skill.ID, out var progress))
                continue;

            if (progress.Target != target || progress.Hits < skill.ChallengeRequiredHits)
                continue;

            UnlockSkill(uid, component, skill);
            UpdateUi(uid, component);
        }
    }

    private FighterChallengeProgress GetChallengeProgress(FighterProgressionComponent component, string skillId)
    {
        if (!component.ChallengeProgress.TryGetValue(skillId, out var progress))
        {
            progress = new FighterChallengeProgress();
            component.ChallengeProgress[skillId] = progress;
        }

        return progress;
    }

    private bool MatchesChallengeTarget(EntityUid target, FighterSkillChallengeTarget challengeTarget)
    {
        return challengeTarget switch
        {
            FighterSkillChallengeTarget.Carp => _tag.HasTag(target, CarpTag),
            FighterSkillChallengeTarget.Bear => MetaData(target).EntityPrototype?.ID is "MobBearSpace" or "MobBearSpaceSalvage",
            FighterSkillChallengeTarget.Borg => HasComp<BorgChassisComponent>(target),
            FighterSkillChallengeTarget.Dragon => HasComp<DragonComponent>(target),
            _ => false,
        };
    }

    protected override bool MeetsSpecialRequirements(EntityUid uid, FighterSkillPrototype skill, FighterProgressionComponent component)
    {
        if (!skill.RequiresTraitorRole)
            return true;

        if (!_mind.TryGetMind(uid, out var mindId, out _))
            return false;

        return _roles.MindHasRole<TraitorRoleComponent>(mindId);
    }

    private int GetCurrentXpThreshold(FighterProgressionComponent component)
    {
        var scaledThreshold = (component.XpThreshold + ThresholdFlatPerLevel * component.ThresholdsReached)
            * MathF.Pow(ThresholdScalePerLevel, component.ThresholdsReached);

        return Math.Max(1, (int) MathF.Round(scaledThreshold, MidpointRounding.AwayFromZero));
    }

    private int GetCombatHitXp(EntityUid attacker, EntityUid target, FighterProgressionComponent component)
    {
        var attackerPowerLevel = Math.Max(CompOrNull<PowerLevelComponent>(attacker)?.CurrentPowerLevel ?? FallbackPowerLevel, 1);
        var targetPowerLevel = Math.Max(CompOrNull<PowerLevelComponent>(target)?.CurrentPowerLevel ?? FallbackPowerLevel, 1);
        var ratio = Math.Clamp((float) targetPowerLevel / attackerPowerLevel, CombatXpRatioFloor, CombatXpRatioCeiling);
        var scaledXp = component.CombatHitXp * MathF.Pow(ratio, CombatXpRatioExponent);
        var awardedXp = (int) MathF.Round(scaledXp, MidpointRounding.AwayFromZero);

        return Math.Clamp(awardedXp, CombatXpAwardFloor, CombatXpAwardCeiling);
    }

    private int GetTrainingHitXp(FighterProgressionComponent component)
    {
        var scaledXp = component.TrainingHitXp * MathF.Pow(TrainingXpScalePerThreshold, component.ThresholdsReached);
        return Math.Max(1, (int) MathF.Round(scaledXp, MidpointRounding.AwayFromZero));
    }

    private bool IsFighterStyleOrigin(EntityUid fighterUid, EntityUid origin)
    {
        return IsFighterStyleWeapon(fighterUid, origin);
    }

    private void RefreshThresholdBonuses(EntityUid uid, FighterProgressionComponent component)
    {
        var bonuses = GetBonuses(uid, component);

        if (TryComp<MobThresholdsComponent>(uid, out var thresholds))
        {
            component.BaseMobThresholds ??= new SortedDictionary<FixedPoint2, MobState>(thresholds.Thresholds);

            var scaledThresholds = new SortedDictionary<FixedPoint2, MobState>();
            foreach (var (threshold, state) in component.BaseMobThresholds)
            {
                var scaled = threshold * bonuses.PassiveMobThresholdMultiplier;
                scaledThresholds[scaled] = state;
            }

            _mobThresholds.SetThresholds(uid, scaledThresholds, thresholds);
        }

        if (TryComp<StaminaComponent>(uid, out var stamina))
        {
            component.BaseStaminaCritThreshold ??= stamina.CritThreshold;
            stamina.CritThreshold = component.BaseStaminaCritThreshold.Value * bonuses.PassiveStaminaCritThresholdMultiplier;
            Dirty(uid, stamina);
        }

        RaiseLocalEvent(uid, new PowerLevelRefreshRequestedEvent(), true);
    }

    private void RestoreThresholdBaselines(EntityUid uid, FighterProgressionComponent component)
    {
        if (TryComp<MobThresholdsComponent>(uid, out var thresholds) && component.BaseMobThresholds != null)
        {
            _mobThresholds.SetThresholds(uid, component.BaseMobThresholds, thresholds);
        }

        if (TryComp<StaminaComponent>(uid, out var stamina) && component.BaseStaminaCritThreshold != null)
        {
            stamina.CritThreshold = component.BaseStaminaCritThreshold.Value;
            Dirty(uid, stamina);
        }

        component.BaseMobThresholds = null;
        component.BaseStaminaCritThreshold = null;
        RaiseLocalEvent(uid, new PowerLevelRefreshRequestedEvent(), true);
    }
}
