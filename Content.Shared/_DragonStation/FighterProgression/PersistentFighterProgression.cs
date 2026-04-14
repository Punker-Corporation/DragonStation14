using System.Linq;
using Robust.Shared.Serialization;

namespace Content.Shared._DragonStation.FighterProgression;

[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class PersistentFighterProgression
{
    [DataField]
    public int CurrentXp { get; set; }

    [DataField]
    public int ThresholdsReached { get; set; }

    [DataField]
    public List<string> UnlockedSkills { get; set; } = new();

    [DataField]
    public List<string> ClosedSkills { get; set; } = new();

    [DataField]
    public bool TransformationsPageUnlocked { get; set; }

    [DataField]
    public bool SuperSaiyanUnlocked { get; set; }

    [DataField]
    public List<string> UnlockedTransformationSkills { get; set; } = new();

    [DataField]
    public float TransformedSeconds { get; set; }

    [DataField]
    public int TransformedHits { get; set; }

    [DataField]
    public int TransformedKills { get; set; }

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
        TransformationsPageUnlocked = other.TransformationsPageUnlocked;
        SuperSaiyanUnlocked = other.SuperSaiyanUnlocked;
        UnlockedTransformationSkills = new List<string>(other.UnlockedTransformationSkills ?? Enumerable.Empty<string>());
        TransformedSeconds = other.TransformedSeconds;
        TransformedHits = other.TransformedHits;
        TransformedKills = other.TransformedKills;
    }

    /// <summary>
    /// Normalizes stored values so malformed or partial profile data can be safely used.
    /// </summary>
    public void EnsureValid()
    {
        CurrentXp = Math.Max(0, CurrentXp);
        ThresholdsReached = Math.Max(0, ThresholdsReached);
        TransformedSeconds = Math.Max(0f, TransformedSeconds);
        TransformedHits = Math.Max(0, TransformedHits);
        TransformedKills = Math.Max(0, TransformedKills);
        UnlockedSkills ??= new List<string>();
        ClosedSkills ??= new List<string>();
        UnlockedTransformationSkills ??= new List<string>();
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
        UnlockedTransformationSkills = UnlockedTransformationSkills
            .Where(skill => !string.IsNullOrWhiteSpace(skill))
            .Distinct()
            .ToList();

        if (SuperSaiyanUnlocked || UnlockedTransformationSkills.Count > 0)
            TransformationsPageUnlocked = true;
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
            && TransformationsPageUnlocked == other.TransformationsPageUnlocked
            && SuperSaiyanUnlocked == other.SuperSaiyanUnlocked
            && Math.Abs(TransformedSeconds - other.TransformedSeconds) < 0.001f
            && TransformedHits == other.TransformedHits
            && TransformedKills == other.TransformedKills
            && UnlockedSkills.SequenceEqual(other.UnlockedSkills)
            && ClosedSkills.SequenceEqual(other.ClosedSkills)
            && UnlockedTransformationSkills.SequenceEqual(other.UnlockedTransformationSkills);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(CurrentXp);
        hashCode.Add(ThresholdsReached);
        hashCode.Add(TransformationsPageUnlocked);
        hashCode.Add(SuperSaiyanUnlocked);
        hashCode.Add(TransformedSeconds);
        hashCode.Add(TransformedHits);
        hashCode.Add(TransformedKills);

        foreach (var skill in UnlockedSkills)
        {
            hashCode.Add(skill);
        }

        foreach (var skill in ClosedSkills)
        {
            hashCode.Add(skill);
        }

        foreach (var skill in UnlockedTransformationSkills)
        {
            hashCode.Add(skill);
        }

        return hashCode.ToHashCode();
    }
}
