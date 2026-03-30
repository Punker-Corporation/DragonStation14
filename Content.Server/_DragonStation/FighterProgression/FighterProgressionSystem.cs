using System.Linq;
using System.Threading.Tasks;
using Content.Goobstation.Shared.MartialArts;
using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Damage.Components;
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
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Shared.Roles.Jobs;

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
    private static readonly ProtoId<TagPrototype> CarpTag = "Carp";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<FighterProgressionComponent, FighterProgressionChangedEvent>(OnFighterProgressionChanged);
        SubscribeLocalEvent<FighterProgressionComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<FighterProgressionComponent, FighterChooseBranchMessage>(OnChooseBranch);
        SubscribeLocalEvent<FighterProgressionComponent, PowerLevelRefreshRequestedEvent>(OnPowerLevelRefreshRequested);
    }

    private void OnFighterProgressionChanged(EntityUid uid, FighterProgressionComponent component, ref FighterProgressionChangedEvent args)
    {
        RefreshThresholdBonuses(uid, component);
    }

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

    private void OnMeleeHit(MeleeHitEvent args)
    {
        if (!TryComp<FighterProgressionComponent>(args.User, out var component))
            return;

        var user = args.User;
        if (!args.IsHit || !IsFighterStyleWeapon(user, args.Weapon))
            return;

        var awardedCombat = false;
        var awardedTraining = false;

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

            AddXp(user, component, GetCombatHitXp(user, hit, component));
            awardedCombat = true;
        }

        if (awardedCombat || awardedTraining)
            UpdateUi(user, component);
    }

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

            if (TryComp<PowerLevelComponent>(args.Target, out var targetPowerLevel))
                AddXp(fighterUid, component, Math.Max(1, targetPowerLevel.CurrentPowerLevel / 2));

            TryCompleteKillChallenge(fighterUid, component, args.Target);
        }
    }

    private void AddXp(EntityUid uid, FighterProgressionComponent component, int amount)
    {
        if (amount <= 0)
            return;

        if (!IsBoxerJob(uid))
            amount = Math.Min(amount, NonBoxerXpAwardCap);

        if (amount <= 0)
            return;

        component.CurrentXp += amount;
        ProcessThresholdAdvancement(uid, component);
        Dirty(uid, component);
        _ = SavePersistentProgression(uid, component);
    }

    private bool IsBoxerJob(EntityUid uid)
    {
        if (!_mind.TryGetMind(uid, out var mindId, out _))
            return false;

        if (!_jobs.MindTryGetJobId(mindId, out var jobId) || jobId == null)
            return false;

        return jobId.Value.Id is "Boxer" or "VisitorBoxer";
    }

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

    private void OnUiOpened(EntityUid uid, FighterProgressionComponent component, BoundUIOpenedEvent args)
    {
        UpdateUi(uid, component);
    }

    private void OnPowerLevelRefreshRequested(EntityUid uid, FighterProgressionComponent component, PowerLevelRefreshRequestedEvent args)
    {
        UpdateUi(uid, component);
    }

    private void UpdateUi(EntityUid uid, FighterProgressionComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        RefreshMissedChallenges(component);

        var states = new List<FighterSkillState>();

        foreach (var skill in _prototype.EnumeratePrototypes<FighterSkillPrototype>())
        {
            states.Add(new FighterSkillState(skill.ID, GetAvailability(uid, skill, component)));
        }

        var powerLevel = CompOrNull<PowerLevelComponent>(uid)?.CurrentPowerLevel ?? 100;

        var state = new FighterSkillTreeBoundUserInterfaceState(
            component.ThresholdsReached,
            powerLevel,
            component.CurrentXp,
            GetCurrentXpThreshold(component),
            component.PendingChoiceOptions.Count > 0,
            states);

        _ui.SetUiState(uid, FighterSkillTreeUiKey.Key, state);
    }

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

    private void UnlockSkill(EntityUid uid, FighterProgressionComponent component, FighterSkillPrototype skill, string popupLoc = "fighter-progression-skill-unlocked")
    {
        if (component.UnlockedSkills.Contains(skill.ID))
            return;

        component.UnlockedSkills.Add(skill.ID);
        component.MissedChallengeSkills.Remove(skill.ID);
        component.ChallengeProgress.Remove(skill.ID);
        RefreshMissedChallenges(component);

        // SSJ unlocks the transformation component the first time the awakening node is reached.
        if (skill.ID == "FighterGoldenWarriorAwakening")
            EnsureComp<TransformationComponent>(uid);

        if (skill.ID == "FighterKiWarriorPath")
            StripKiWarriorBlockedGear(uid);

        if (skill.MartialArtUnlock != null)
            _martialArts.TryGrantMartialArtKnowledge(uid, skill.MartialArtUnlock.Value);

        RaiseLocalEvent(uid, new FighterProgressionChangedEvent(), true);
        RaiseLocalEvent(uid, new PowerLevelRefreshRequestedEvent(), true);
        _popup.PopupEntity(Loc.GetString(popupLoc, ("skill", Loc.GetString(skill.Name))), uid, uid);
        _ = SavePersistentProgression(uid, component);
    }

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

        ApplyUnlockedSkillSideEffects(uid, component);
        RaiseLocalEvent(uid, new FighterProgressionChangedEvent(), true);
        RaiseLocalEvent(uid, new PowerLevelRefreshRequestedEvent(), true);
        Dirty(uid, component);
    }

    private void ApplyUnlockedSkillSideEffects(EntityUid uid, FighterProgressionComponent component)
    {
        foreach (var skillId in component.UnlockedSkills)
        {
            if (!_prototype.TryIndex(skillId, out FighterSkillPrototype? skill))
                continue;

            if (skill.MartialArtUnlock != null)
                _martialArts.TryGrantMartialArtKnowledge(uid, skill.MartialArtUnlock.Value);

            if (skill.ID == "FighterGoldenWarriorAwakening")
                EnsureComp<TransformationComponent>(uid);
        }

        if (component.UnlockedSkills.Any(skillId => skillId.Id == "FighterKiWarriorPath"))
            StripKiWarriorBlockedGear(uid);
    }

    private PersistentFighterProgression GetPersistentProgression(FighterProgressionComponent component)
    {
        return new PersistentFighterProgression
        {
            CurrentXp = component.CurrentXp,
            ThresholdsReached = component.ThresholdsReached,
            UnlockedSkills = component.UnlockedSkills.Select(skill => skill.Id).Distinct().ToList(),
            ClosedSkills = component.ClosedSkills.Select(skill => skill.Id).Distinct().ToList(),
        };
    }

    private async Task SavePersistentProgression(EntityUid uid, FighterProgressionComponent component)
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
