using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._DragonStation.FighterProgression.Prototypes;

[DataDefinition]
[Prototype("fighterTransformationSkill")]
public sealed partial class FighterTransformationSkillPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name = string.Empty;

    [DataField(required: true)]
    public LocId Description = string.Empty;

    [DataField(required: true)]
    public SpriteSpecifier Icon = default!;

    [DataField]
    public List<LocId> EffectDescriptions = new();

    [DataField]
    public List<ProtoId<FighterTransformationSkillPrototype>> Prerequisites = new();

    [DataField]
    public float RequiredTransformedSeconds;

    [DataField]
    public int RequiredTransformedHits;

    [DataField]
    public int RequiredTransformedKills;

    [DataField]
    public float TransformationMeleeBonus;

    [DataField]
    public float TransformationSpeedBonus;

    [DataField]
    public float TransformationStaminaDrainMultiplier = 1f;

    [DataField]
    public float TransformationResistanceCoefficientMultiplier = 1f;
}
