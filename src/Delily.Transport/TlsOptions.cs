using System.ComponentModel.DataAnnotations;

namespace Delily.Transport;

/// <summary>
/// TLS configuration for SMTP/IMAP listeners. Certificate loading is added in
/// Phase 7; enabled is false by default so a fresh checkout starts in
/// plaintext development mode.
/// </summary>
public sealed class TlsOptions
{
    public const string SectionName = "Delily:Tls";

    public bool Enabled { get; init; }

    public string CertificatePath { get; init; } = string.Empty;

    public string CertificatePassword { get; init; } = string.Empty;

    [Required]
    public string MinimumProtocol { get; init; } = "Tls12";
}
