using Content.Shared._DragonStation.FighterProgression.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._DragonStation.FighterProgression;

[Serializable, NetSerializable]
public enum FighterSkillTreeUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class FighterSkillTreeBoundUserInterfaceState(
    int thresholdsReached,
    int currentPowerLevel,
    int currentXp,
    int xpThreshold,
    bool hasPendingBranchChoice,
    List<FighterSkillState> skills,
    bool transformationsPageUnlocked,
    bool superSaiyanUnlocked,
    List<FighterTransformationSkillState> transformationSkills,
    float transformedSeconds,
    int transformedHits,
    int transformedKills)
    : BoundUserInterfaceState
{
    public int ThresholdsReached = thresholdsReached;
    public int CurrentPowerLevel = currentPowerLevel;
    public int CurrentXp = currentXp;
    public int XpThreshold = xpThreshold;
    public bool HasPendingBranchChoice = hasPendingBranchChoice;
    public List<FighterSkillState> Skills = skills;
    public bool TransformationsPageUnlocked = transformationsPageUnlocked;
    public bool SuperSaiyanUnlocked = superSaiyanUnlocked;
    public List<FighterTransformationSkillState> TransformationSkills = transformationSkills;
    public float TransformedSeconds = transformedSeconds;
    public int TransformedHits = transformedHits;
    public int TransformedKills = transformedKills;
}

[Serializable, NetSerializable]
public sealed class FighterSkillState(string skillId, FighterSkillAvailability availability)
{
    public string SkillId = skillId;
    public FighterSkillAvailability Availability = availability;
}

[Serializable, NetSerializable]
public sealed class FighterTransformationSkillState(string skillId, FighterSkillAvailability availability)
{
    public string SkillId = skillId;
    public FighterSkillAvailability Availability = availability;
}

[Serializable, NetSerializable]
public sealed class FighterChooseBranchMessage(ProtoId<FighterSkillPrototype> skillId) : BoundUserInterfaceMessage
{
    public ProtoId<FighterSkillPrototype> SkillId = skillId;
}
