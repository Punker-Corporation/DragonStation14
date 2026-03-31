using Content.Goobstation.Common.Stunnable;
using Content.Shared.Bed.Sleep;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Movement.Systems;
using Content.Shared.StatusEffect;
using Content.Shared._DragonStation.FighterProgression;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Shared._DragonStation.Transformations;

public sealed partial class TransformationSystem
{
    private void InitializeCombat()
    {
        SubscribeLocalEvent<SuperSaiyan1Component, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
        SubscribeLocalEvent<SuperSaiyan1Component, GetUserMeleeDamageEvent>(OnGetUserMeleeDamage);
        SubscribeLocalEvent<SuperSaiyan1Component, BeforeOldStatusEffectAddedEvent>(OnBeforeStatusEffect);
        SubscribeLocalEvent<SuperSaiyan1Component, BeforeStunEvent>(OnBeforeStun);
        SubscribeLocalEvent<SuperSaiyan1Component, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<SuperSaiyan1Component, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<SuperSaiyan1Component, SleepStateChangedEvent>(OnSleep);
        SubscribeLocalEvent<SuperSaiyan1Component, FighterProgressionChangedEvent>(OnFighterProgressionChanged);
    }

    private void OnRefreshMovementSpeed(EntityUid uid, SuperSaiyan1Component component, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!component.Active)
            return;

        var bonuses = _fighterProgression.GetBonuses(uid);
        var speed = component.SpeedModifier * bonuses.TransformationSpeedMultiplier;
        args.ModifySpeed(speed, speed);
    }

    private void OnGetUserMeleeDamage(EntityUid uid, SuperSaiyan1Component component, ref GetUserMeleeDamageEvent args)
    {
        if (!component.Active)
            return;

        var bonuses = _fighterProgression.GetBonuses(uid);
        args.Damage *= component.MeleeDamageModifier * bonuses.TransformationMeleeMultiplier;
    }

    // Active transformations can provide their own defensive profile.
    private void OnDamageModify(EntityUid uid, SuperSaiyan1Component component, ref DamageModifyEvent args)
    {
        if (!component.Active)
            return;

        var bonuses = _fighterProgression.GetBonuses(uid);
        var resistances = new DamageModifierSet
        {
            Coefficients =
            {
                ["Blunt"] = component.BluntResistanceCoefficient * bonuses.TransformationResistanceCoefficientMultiplier,
                ["Slash"] = component.SlashResistanceCoefficient * bonuses.TransformationResistanceCoefficientMultiplier,
                ["Piercing"] = component.PiercingResistanceCoefficient * bonuses.TransformationResistanceCoefficientMultiplier,
                ["Heat"] = component.HeatResistanceCoefficient * bonuses.TransformationResistanceCoefficientMultiplier,
                ["Cold"] = component.ColdResistanceCoefficient * bonuses.TransformationResistanceCoefficientMultiplier,
                ["Shock"] = component.ShockResistanceCoefficient * bonuses.TransformationResistanceCoefficientMultiplier,
                ["Caustic"] = component.CausticResistanceCoefficient * bonuses.TransformationResistanceCoefficientMultiplier,
                ["Asphyxiation"] = component.AsphyxiationResistanceCoefficient * bonuses.TransformationResistanceCoefficientMultiplier,
            }
        };

        args.Damage = DamageSpecifier.ApplyModifierSet(args.Damage,
            DamageSpecifier.PenetrateArmor(resistances, args.Damage.ArmorPenetration));
    }

    private void OnBeforeStatusEffect(EntityUid uid, SuperSaiyan1Component component, ref BeforeOldStatusEffectAddedEvent args)
    {
        if (!component.Active || !component.SuppressStunEffects)
            return;

        if (args.EffectKey is not ("KnockedDown" or "Stun"))
            return;

        args.Cancelled = true;
    }

    private void OnBeforeStun(EntityUid uid, SuperSaiyan1Component component, ref BeforeStunEvent args)
    {
        if (!component.Active || !component.SuppressStunEffects)
            return;

        args.Cancelled = true;
    }

    // Critical condition or death immediately knocks the user out of the form.
    private void OnMobStateChanged(EntityUid uid, SuperSaiyan1Component component, MobStateChangedEvent args)
    {
        if (!component.Active
            || args.NewMobState is not (MobState.Critical or MobState.Dead))
            return;

        DisableTransformation(uid, component);
    }

    // Falling asleep shuts the form off immediately.
    private void OnSleep(EntityUid uid, SuperSaiyan1Component component, ref SleepStateChangedEvent args)
    {
        if (!component.Active
            || !args.FellAsleep)
            return;

        DisableTransformation(uid, component);
    }

    private void OnFighterProgressionChanged(EntityUid uid, SuperSaiyan1Component component, FighterProgressionChangedEvent args)
    {
        RefreshActionAvailability(uid, component);
    }
}
