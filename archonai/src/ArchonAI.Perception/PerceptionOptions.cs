namespace ArchonAI.Perception;

public sealed class PerceptionOptions
{
    public int MaxTitleLength { get; set; } = 200;

    public int MaxDescriptionLength { get; set; } = 4000;

    public IReadOnlyList<string> RequiredConstraintKeys { get; set; } = new[] { "objectiveType" };

    public IReadOnlyList<string> NoiseTokens { get; set; } = new[] { "um", "uh", "like", "you know", "basically" };
}
