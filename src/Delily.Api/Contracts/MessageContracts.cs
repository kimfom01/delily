namespace Delily.Api.Contracts;

public sealed record EmailAddress(string Email, string? Name = null);

public sealed record Attachment(
    string Filename,
    string ContentType,
    string ContentBase64);

/// <summary>
/// Convenience JSON representation of a message. The raw-MIME endpoint is the
/// escape hatch for callers that need full control over the message bytes.
/// </summary>
public sealed record SendMessageRequest
{
    public required EmailAddress From { get; init; }

    public required IReadOnlyList<EmailAddress> To { get; init; }

    public IReadOnlyList<EmailAddress>? Cc { get; init; }

    public IReadOnlyList<EmailAddress>? Bcc { get; init; }

    public EmailAddress? ReplyTo { get; init; }

    public string? Subject { get; init; }

    public string? Text { get; init; }

    public string? Html { get; init; }

    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    public IReadOnlyList<Attachment>? Attachments { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record SendMessageResponse(
    string Id,
    string Status,
    DateTimeOffset AcceptedAt);

public sealed record MessageStatusResponse(
    string Id,
    string Status,
    int RecipientCount,
    DateTimeOffset AcceptedAt);
