using Content.Server.Atmos.Components;
using Content.Shared._DragonStation.Transformations;

namespace Content.Server._DragonStation.Transformations;

/// <summary>
/// Applies transformation-based pressure protection to active forms during barotrauma calculations.
/// </summary>
public sealed class TransformationPressureSystem : EntitySystem
{
    /// <summary>
    /// Registers the pressure-protection hook for transformed entities.
    /// </summary>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SuperSaiyan1Component, GetPressureProtectionValuesEvent>(OnGetPressureProtectionValues);
    }

    /// <summary>
    /// Gives active transformations partial low-pressure resistance without making them fully space-proof.
    /// </summary>
    private void OnGetPressureProtectionValues(EntityUid uid, SuperSaiyan1Component component, ref GetPressureProtectionValuesEvent args)
    {
        if (!component.Active)
            return;

        if (component.LowPressureProtectionMultiplier <= 1f)
            return;

        args.LowPressureMultiplier *= component.LowPressureProtectionMultiplier;
    }
}
