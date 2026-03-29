using System;
using Content.Goobstation.Maths.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs;

namespace Content.Shared._DragonStation.FighterProgression;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FighterProgressionComponent : Component
{
    [DataField(customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string? OpenTreeAction = "ActionOpenFighterSkillTree";

    [DataField, AutoNetworkedField]
    public EntityUid? OpenTreeActionEntity;

    [DataField, AutoNetworkedField]
    public int CurrentXp;

    [DataField, AutoNetworkedField]
    public int XpThreshold = 100;

    [DataField, AutoNetworkedField]
    public int ThresholdsReached;

    [DataField]
    public int CombatHitXp = 1;

    [DataField]
    public int TrainingHitXp = 1;

    [DataField]
    public TimeSpan TrainingHitCooldown = TimeSpan.FromSeconds(1.2f);

    [DataField]
    public TimeSpan NextTrainingXpTime;

    // Reserved for future cross-round persistence keyed to a stable character identity.
    [DataField, AutoNetworkedField]
    public string PersistentCharacterId = string.Empty;

    [DataField, AutoNetworkedField]
    public List<ProtoId<Prototypes.FighterSkillPrototype>> UnlockedSkills = new();

    [DataField, AutoNetworkedField]
    public List<ProtoId<Prototypes.FighterSkillPrototype>> PendingChoiceOptions = new();

    [DataField, AutoNetworkedField]
    public List<ProtoId<Prototypes.FighterSkillPrototype>> ClosedSkills = new();

    public readonly Dictionary<string, FighterChallengeProgress> ChallengeProgress = new();
    public readonly HashSet<string> MissedChallengeSkills = new();

    [NonSerialized]
    public SortedDictionary<FixedPoint2, MobState>? BaseMobThresholds;

    [NonSerialized]
    public float? BaseStaminaCritThreshold;
}

public sealed class FighterChallengeProgress
{
    public EntityUid? Target;
    public int Hits;
}
