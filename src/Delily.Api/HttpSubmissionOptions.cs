using System.ComponentModel.DataAnnotations;

namespace Delily.Api;

/// <summary>
/// Configuration for the HTTPS message-submission API. The API is an MSA
/// equivalent for applications: authenticated tenants submit messages, the
/// server queues them, and delivery is identical to SMTP submission.
/// </summary>
public sealed class HttpSubmissionOptions
{
    public const string SectionName = "Delily:Http";

    [Range(1024, 104857600)]
    public int MaxRequestBytes { get; init; } = 10 * 1024 * 1024;

    [Range(1, 1000)]
    public int MaxRecipients { get; init; } = 100;

    [Range(0, 100)]
    public int MaxAttachments { get; init; } = 20;

    [Range(1024, 104857600)]
    public int MaxAttachmentBytes { get; init; } = 10 * 1024 * 1024;

    [Range(1, 168)]
    public int IdempotencyWindowHours { get; init; } = 24;

    public bool RequireIdempotencyKey { get; init; } = true;

    [Range(1, 120)]
    public int WebhookTimeoutSeconds { get; init; } = 10;
}
