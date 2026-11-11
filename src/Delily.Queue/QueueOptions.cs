using System.ComponentModel.DataAnnotations;

namespace Delily.Queue;

/// <summary>
/// Outbound queue configuration. Values are deliberately conservative; tune
/// them only after measuring a real queue.
/// </summary>
public sealed class QueueOptions
{
    public const string SectionName = "Delily:Queue";

    [Range(1, 1000)]
    public int BatchSize { get; init; } = 50;

    [Range(30, 86400)]
    public int VisibilityTimeoutSeconds { get; init; } = 300;

    [Range(1, 1000)]
    public int MaxAttempts { get; init; } = 20;

    [Range(1, 86400)]
    public int InitialRetryDelaySeconds { get; init; } = 300;
}
