using System.Linq;
using Robust.Shared.Serialization;

namespace Content.Shared._DragonStation.FighterProgression;

[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class PersistentFighterProgression
{
    [DataField]
    public int CurrentXp;

    [DataField]
    public int ThresholdsReached;

    [DataField]
    public List<string> UnlockedSkills = new();

    [DataField]
    public List<string> ClosedSkills = new();

    public PersistentFighterProgression()
    {
    }

    public PersistentFighterProgression(PersistentFighterProgression other)
    {
        CurrentXp = other.CurrentXp;
        ThresholdsReached = other.ThresholdsReached;
        UnlockedSkills = new List<string>(other.UnlockedSkills);
        ClosedSkills = new List<string>(other.ClosedSkills);
    }

    public void EnsureValid()
    {
        CurrentXp = Math.Max(0, CurrentXp);
        ThresholdsReached = Math.Max(0, ThresholdsReached);
        UnlockedSkills ??= new List<string>();
        ClosedSkills ??= new List<string>();
        UnlockedSkills = UnlockedSkills
            .Where(skill => !string.IsNullOrWhiteSpace(skill))
            .Distinct()
            .ToList();
        ClosedSkills = ClosedSkills
            .Where(skill => !string.IsNullOrWhiteSpace(skill))
            .Distinct()
            .ToList();
    }

    public bool MemberwiseEquals(PersistentFighterProgression? other)
    {
        if (other == null)
            return false;

        return CurrentXp == other.CurrentXp
            && ThresholdsReached == other.ThresholdsReached
            && UnlockedSkills.SequenceEqual(other.UnlockedSkills)
            && ClosedSkills.SequenceEqual(other.ClosedSkills);
    }
}
