using Content.Shared.Armor;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Events;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Explosion;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Movement.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Roles;
using Content.Shared.Weapons.Melee;
using Content.Shared._DragonStation.FighterProgression;
using Content.Shared._DragonStation.Transformations;
using Robust.Shared.Prototypes;

namespace Content.Shared._DragonStation.PowerLevel;

public sealed class PowerLevelSystem : EntitySystem
{
    private const float BaselineUnarmedDamage = 5f;
    private const float BaselineUnarmedAttackRate = 1f;
    private const float BaselineWalkSpeed = MovementSpeedModifierComponent.DefaultBaseWalkSpeed;
    private const float BaselineSprintSpeed = MovementSpeedModifierComponent.DefaultBaseSprintSpeed;
    private const float DefenseExponent = 1.25f;
    private const float DefenseScoreCap = 30f;

    [Dependency] private readonly SharedFighterProgressionSystem _fighterProgression = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PowerLevelComponent, ComponentStartup>(OnPowerLevelStartup);
        SubscribeLocalEvent<PowerLevelComponent, MapInitEvent>(OnPowerLevelMapInit);
        SubscribeLocalEvent<PowerLevelComponent, PowerLevelRefreshRequestedEvent>(OnRefreshRequested);
        SubscribeLocalEvent<PowerLevelComponent, StartingGearEquippedEvent>(OnStartingGearEquipped);
        SubscribeLocalEvent<PowerLevelComponent, DidEquipEvent>(OnEquipmentChanged);
        SubscribeLocalEvent<PowerLevelComponent, DidUnequipEvent>(OnEquipmentChanged);
        SubscribeLocalEvent<ArmorComponent, GotEquippedEvent>(OnArmorEquippedChanged);
        SubscribeLocalEvent<ArmorComponent, GotUnequippedEvent>(OnArmorEquippedChanged);

        SubscribeLocalEvent<MeleeWeaponComponent, ComponentStartup>(OnMeleeWeaponChanged);
        SubscribeLocalEvent<MeleeWeaponComponent, ComponentShutdown>(OnMeleeWeaponChanged);
        SubscribeLocalEvent<MeleeWeaponComponent, GotEquippedEvent>(OnMeleeWeaponEquippedChanged);
        SubscribeLocalEvent<MeleeWeaponComponent, GotUnequippedEvent>(OnMeleeWeaponEquippedChanged);
        SubscribeLocalEvent<MovementSpeedModifierComponent, ComponentStartup>(OnMovementSpeedChanged);
        SubscribeLocalEvent<MovementSpeedModifierComponent, ComponentShutdown>(OnMovementSpeedChanged);
    }

    public void Recalculate(EntityUid uid, PowerLevelComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        var offenseScore = CalculateOffenseScore(uid);
        var mobilityScore = CalculateMobilityScore(uid);
        var durabilityScore = CalculateDurabilityScore(uid);
        var defenseScore = CalculateDefenseScore(uid);
        var healthBase = CalculateHealthBase(uid);

        component.LastOffenseScore = offenseScore;
        component.LastMobilityScore = mobilityScore;
        component.LastDurabilityScore = durabilityScore;
        component.LastDefenseScore = defenseScore;

        component.CurrentPowerLevel = (int) MathF.Round(100f * healthBase * (offenseScore + mobilityScore + durabilityScore) * defenseScore);
        Dirty(uid, component);
    }

    private void OnPowerLevelStartup(EntityUid uid, PowerLevelComponent component, ComponentStartup args)
    {
        Recalculate(uid, component);
    }

    private void OnPowerLevelMapInit(EntityUid uid, PowerLevelComponent component, MapInitEvent args)
    {
        Recalculate(uid, component);
    }

    private void OnRefreshRequested(EntityUid uid, PowerLevelComponent component, EntityEventArgs args)
    {
        Recalculate(uid, component);
    }

    private void OnStartingGearEquipped(EntityUid uid, PowerLevelComponent component, ref StartingGearEquippedEvent args)
    {
        Recalculate(uid, component);
    }

    private void OnEquipmentChanged(EntityUid uid, PowerLevelComponent component, DidEquipEvent args)
    {
        Recalculate(uid, component);
    }

    private void OnEquipmentChanged(EntityUid uid, PowerLevelComponent component, DidUnequipEvent args)
    {
        Recalculate(uid, component);
    }

    private void OnArmorEquippedChanged(EntityUid uid, ArmorComponent component, GotEquippedEvent args)
    {
        RefreshIfPowerLevel(args.Equipee);
    }

    private void OnArmorEquippedChanged(EntityUid uid, ArmorComponent component, GotUnequippedEvent args)
    {
        RefreshIfPowerLevel(args.Equipee);
    }

    private void RefreshIfPowerLevel(EntityUid uid)
    {
        if (!HasComp<PowerLevelComponent>(uid))
            return;

        Recalculate(uid);
    }

    private void OnMeleeWeaponChanged(EntityUid uid, MeleeWeaponComponent component, EntityEventArgs args)
    {
        RefreshIfPowerLevel(uid);
    }

    private void OnMeleeWeaponEquippedChanged(EntityUid uid, MeleeWeaponComponent component, GotEquippedEvent args)
    {
        if (args.Slot == "gloves")
            RefreshIfPowerLevel(args.Equipee);
    }

    private void OnMeleeWeaponEquippedChanged(EntityUid uid, MeleeWeaponComponent component, GotUnequippedEvent args)
    {
        if (args.Slot == "gloves")
            RefreshIfPowerLevel(args.Equipee);
    }

    private void OnMovementSpeedChanged(EntityUid uid, MovementSpeedModifierComponent component, EntityEventArgs args)
    {
        RefreshIfPowerLevel(uid);
    }

    private float CalculateOffenseScore(EntityUid uid)
    {
        var damageRatio = Math.Clamp(GetIntrinsicUnarmedDamage(uid) / BaselineUnarmedDamage, 0f, 10f);
        var attackRateRatio = Math.Clamp(GetIntrinsicUnarmedAttackRate(uid) / BaselineUnarmedAttackRate, 0f, 10f);
        return 0.40f * damageRatio + 0.15f * attackRateRatio;
    }

    private float CalculateMobilityScore(EntityUid uid)
    {
        var (walkSpeed, sprintSpeed) = GetIntrinsicMovement(uid);
        var walkRatio = Math.Clamp(walkSpeed / BaselineWalkSpeed, 0f, 10f);
        var sprintRatio = Math.Clamp(sprintSpeed / BaselineSprintSpeed, 0f, 10f);
        return 0.15f * walkRatio + 0.15f * sprintRatio;
    }

    private float CalculateDurabilityScore(EntityUid uid)
    {
        var weightedDefenseAverage = GetWeightedDefenseAverage(GetDefenseCoefficients(uid));
        var survivabilityRatio = Math.Clamp(1f / MathF.Max(weightedDefenseAverage, 0.0001f), 1f, 20f);
        var survivabilityBonus = Math.Clamp((survivabilityRatio - 1f) * 0.10f, 0f, 2f);
        var staminaBonus = GetStaminaDurabilityBonus(uid);

        if (!TryComp<MobThresholdsComponent>(uid, out var thresholds))
            return 0.15f + survivabilityBonus + staminaBonus;

        var critThreshold = GetThresholdForState(thresholds, MobState.Critical);
        var deadThreshold = GetThresholdForState(thresholds, MobState.Dead);

        if (critThreshold == null && deadThreshold == null)
            return 0.15f + survivabilityBonus + staminaBonus;

        var critRatio = Math.Clamp((critThreshold ?? 100f) / 100f, 0f, 20f);
        var deadRatio = Math.Clamp((deadThreshold ?? 200f) / 200f, 0f, 20f);

        return ((critRatio + deadRatio) / 2f) * 0.15f + survivabilityBonus + staminaBonus;
    }

    private float CalculateHealthBase(EntityUid uid)
    {
        if (!TryComp<MobThresholdsComponent>(uid, out var thresholds))
            return 1f;

        var critThreshold = GetThresholdForState(thresholds, MobState.Critical);
        var deadThreshold = GetThresholdForState(thresholds, MobState.Dead);

        if (critThreshold == null && deadThreshold == null)
            return 1f;

        var critRatio = Math.Clamp((critThreshold ?? 100f) / 100f, 0.25f, 50f);
        var deadRatio = Math.Clamp((deadThreshold ?? 200f) / 200f, 0.25f, 50f);
        return (critRatio + deadRatio) / 2f;
    }

    private float GetStaminaDurabilityBonus(EntityUid uid)
    {
        if (!TryComp<StaminaComponent>(uid, out var stamina))
            return 0f;

        // Keep normal humanoids almost unchanged while letting absurd stamina crit thresholds
        // meaningfully increase power level for boss-like entities.
        var staminaRatio = MathF.Max(stamina.CritThreshold / 100f, 1f);
        return Math.Clamp(MathF.Log10(staminaRatio) * 0.20f, 0f, 1.2f);
    }

    private float CalculateDefenseScore(EntityUid uid)
    {
        var weightedAverage = GetWeightedDefenseAverage(GetDefenseCoefficients(uid));

        // Defense now grows exponentially with survivability: extreme coefficients on common combat
        // types should make the wearer feel like a raid boss, not just "a bit tankier".
        return Math.Clamp(1f / MathF.Pow(Math.Max(weightedAverage, 0.0001f), DefenseExponent), 1f, DefenseScoreCap);
    }

    private float GetIntrinsicUnarmedDamage(EntityUid uid)
    {
        if (!TryGetPowerLevelWeapon(uid, out var weaponUid, out var melee))
            return 0f;

        var damage = _melee.GetDamage(weaponUid, uid, melee);
        float total = 0f;
        foreach (var value in damage.DamageDict.Values)
        {
            if (value > 0)
                total += value.Float();
        }

        return total;
    }

    private float GetIntrinsicUnarmedAttackRate(EntityUid uid)
    {
        if (!TryGetPowerLevelWeapon(uid, out var weaponUid, out var melee))
            return 0f;

        return _melee.GetAttackRate(weaponUid, uid, melee);
    }

    private (float WalkSpeed, float SprintSpeed) GetIntrinsicMovement(EntityUid uid)
    {
        var walkSpeed = BaselineWalkSpeed;
        var sprintSpeed = BaselineSprintSpeed;

        if (TryComp<MovementSpeedModifierComponent>(uid, out var movement))
        {
            walkSpeed = movement.CurrentWalkSpeed;
            sprintSpeed = movement.CurrentSprintSpeed;
        }

        if (TryComp<FighterProgressionComponent>(uid, out var fighter))
        {
            var bonuses = _fighterProgression.GetBonuses(uid, fighter);
            walkSpeed *= bonuses.PassiveSpeedMultiplier;
            sprintSpeed *= bonuses.PassiveSpeedMultiplier;

            if (TryComp<TransformationComponent>(uid, out var transformation) && transformation.Active)
            {
                walkSpeed *= transformation.SpeedModifier * bonuses.TransformationSpeedMultiplier;
                sprintSpeed *= transformation.SpeedModifier * bonuses.TransformationSpeedMultiplier;
            }
        }
        else if (TryComp<TransformationComponent>(uid, out var transformation) && transformation.Active)
        {
            walkSpeed *= transformation.SpeedModifier;
            sprintSpeed *= transformation.SpeedModifier;
        }

        return (walkSpeed, sprintSpeed);
    }

    private (float Blunt, float Slash, float Piercing, float Heat, float Cold, float Explosion, float Stamina) GetDefenseCoefficients(EntityUid uid)
    {
        var blunt = 1f;
        var slash = 1f;
        var piercing = 1f;
        var heat = 1f;
        var cold = 1f;
        var explosion = 1f;
        var stamina = 1f;

        FighterProgressionBonuses bonuses = FighterProgressionBonuses.Default;
        if (TryComp<FighterProgressionComponent>(uid, out var fighter))
        {
            bonuses = _fighterProgression.GetBonuses(uid, fighter);
            blunt *= bonuses.PassivePhysicalResistanceCoefficientMultiplier;
            slash *= bonuses.PassivePhysicalResistanceCoefficientMultiplier;
            piercing *= bonuses.PassivePhysicalResistanceCoefficientMultiplier;
            heat *= bonuses.PassiveTemperatureResistanceCoefficientMultiplier;
            cold *= bonuses.PassiveTemperatureResistanceCoefficientMultiplier;
        }

        if (TryComp<TransformationComponent>(uid, out var transformation) && transformation.Active)
        {
            blunt *= transformation.BluntResistanceCoefficient * bonuses.TransformationResistanceCoefficientMultiplier;
            slash *= transformation.SlashResistanceCoefficient * bonuses.TransformationResistanceCoefficientMultiplier;
            piercing *= transformation.PiercingResistanceCoefficient * bonuses.TransformationResistanceCoefficientMultiplier;
            heat *= transformation.HeatResistanceCoefficient * bonuses.TransformationResistanceCoefficientMultiplier;
            cold *= transformation.ColdResistanceCoefficient * bonuses.TransformationResistanceCoefficientMultiplier;
        }

        if (TryComp<DamageableComponent>(uid, out var damageable) &&
            damageable.DamageModifierSetId != null &&
            _prototypeManager.TryIndex<DamageModifierSetPrototype>(damageable.DamageModifierSetId, out var modifierSet))
        {
            if (modifierSet.Coefficients.TryGetValue("Blunt", out var setBlunt))
                blunt *= setBlunt;

            if (modifierSet.Coefficients.TryGetValue("Slash", out var setSlash))
                slash *= setSlash;

            if (modifierSet.Coefficients.TryGetValue("Piercing", out var setPiercing))
                piercing *= setPiercing;

            if (modifierSet.Coefficients.TryGetValue("Heat", out var setHeat))
                heat *= setHeat;

            if (modifierSet.Coefficients.TryGetValue("Cold", out var setCold))
                cold *= setCold;
        }

        var armorCoefficients = new CoefficientQueryEvent(~SlotFlags.POCKET);
        RaiseLocalEvent(uid, armorCoefficients);

        if (armorCoefficients.DamageModifiers.Coefficients.TryGetValue("Blunt", out var wornBlunt))
            blunt *= wornBlunt;

        if (armorCoefficients.DamageModifiers.Coefficients.TryGetValue("Slash", out var wornSlash))
            slash *= wornSlash;

        if (armorCoefficients.DamageModifiers.Coefficients.TryGetValue("Piercing", out var wornPiercing))
            piercing *= wornPiercing;

        if (armorCoefficients.DamageModifiers.Coefficients.TryGetValue("Heat", out var wornHeat))
            heat *= wornHeat;

        if (armorCoefficients.DamageModifiers.Coefficients.TryGetValue("Cold", out var wornCold))
            cold *= wornCold;

        var explosionEvent = new GetExplosionResistanceEvent("Default");
        RaiseLocalEvent(uid, ref explosionEvent);
        explosion *= Math.Max(explosionEvent.DamageCoefficient, 0.0001f);

        var staminaEvent = new BeforeStaminaDamageEvent(1f);
        RaiseLocalEvent(uid, ref staminaEvent);
        stamina *= Math.Max(staminaEvent.Value, 0.0001f);

        return (blunt, slash, piercing, heat, cold, explosion, stamina);
    }

    private static float GetWeightedDefenseAverage(
        (float Blunt, float Slash, float Piercing, float Heat, float Cold, float Explosion, float Stamina) coefficients)
    {
        return GetWeightedDefenseAverage(
            coefficients.Blunt,
            coefficients.Slash,
            coefficients.Piercing,
            coefficients.Heat,
            coefficients.Cold,
            coefficients.Explosion,
            coefficients.Stamina);
    }

    private static float GetWeightedDefenseAverage(
        float blunt,
        float slash,
        float piercing,
        float heat,
        float cold,
        float explosion,
        float stamina)
    {
        return blunt * 0.24f +
               slash * 0.24f +
               piercing * 0.24f +
               heat * 0.10f +
               explosion * 0.10f +
               stamina * 0.06f +
               cold * 0.02f;
    }

    private static float? GetThresholdForState(MobThresholdsComponent thresholds, MobState state)
    {
        foreach (var (threshold, thresholdState) in thresholds.Thresholds)
        {
            if (thresholdState == state)
                return threshold.Float();
        }

        return null;
    }

    private bool TryGetPowerLevelWeapon(EntityUid uid, out EntityUid weaponUid, out MeleeWeaponComponent? melee)
    {
        weaponUid = uid;
        melee = null;

        if (_inventory.TryGetSlotEntity(uid, "gloves", out var gloves) &&
            TryComp(gloves, out melee))
        {
            weaponUid = gloves.Value;
            return true;
        }

        if (TryComp(uid, out melee))
            return true;

        return false;
    }
}
