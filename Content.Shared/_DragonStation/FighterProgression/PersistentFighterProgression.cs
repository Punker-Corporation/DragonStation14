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

    /// <summary>
    /// Creates an empty persistent fighter progression payload.
    /// </summary>
    public PersistentFighterProgression()
    {
    }

    /// <summary>
    /// Creates a deep copy of another persistent fighter progression payload.
    /// </summary>
    public PersistentFighterProgression(PersistentFighterProgression other)
    {
        CurrentXp = other.CurrentXp;
        ThresholdsReached = other.ThresholdsReached;
        UnlockedSkills = new List<string>(other.UnlockedSkills ?? Enumerable.Empty<string>());
        ClosedSkills = new List<string>(other.ClosedSkills ?? Enumerable.Empty<string>());
    }

    /// <summary>
    /// Normalizes stored values so malformed or partial profile data can be safely used.
    /// </summary>
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
        UnlockedSkills = UnlockedSkills
            .Except(ClosedSkills)
            .ToList();
    }

    /// <summary>
    /// Compares the payload contents without relying on reference equality.
    /// </summary>
    public bool MemberwiseEquals(PersistentFighterProgression? other)
    {
        if (other == null)
            return false;

        return CurrentXp == other.CurrentXp
            && ThresholdsReached == other.ThresholdsReached
            && UnlockedSkills.SequenceEqual(other.UnlockedSkills)
            && ClosedSkills.SequenceEqual(other.ClosedSkills);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(CurrentXp);
        hashCode.Add(ThresholdsReached);

        foreach (var skill in UnlockedSkills)
        {
            hashCode.Add(skill);
        }

        foreach (var skill in ClosedSkills)
        {
            hashCode.Add(skill);
        }

        return hashCode.ToHashCode();
    }
}
