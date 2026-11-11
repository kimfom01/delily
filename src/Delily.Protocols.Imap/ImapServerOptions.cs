using System.ComponentModel.DataAnnotations;

namespace Delily.Protocols.Imap;

/// <summary>
/// Configuration for the IMAP listeners. Protocol behavior is implemented in
/// Phase 4; this exists now so configuration validation is in place from day one.
/// </summary>
public sealed class ImapServerOptions
{
    public const string SectionName = "Delily:Imap";

    [Required]
    public string Hostname { get; init; } = "localhost";

    [Range(1, 65535)]
    public int ImapPort { get; init; } = 143;

    [Range(1, 65535)]
    public int ImapsPort { get; init; } = 993;

    [Range(1024, 1073741824)]
    public int MaxLiteralBytes { get; init; } = 25 * 1024 * 1024;

    [Range(30, 86400)]
    public int IdleTimeoutSeconds { get; init; } = 1800;
}
