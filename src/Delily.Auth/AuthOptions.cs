using System.ComponentModel.DataAnnotations;

namespace Delily.Auth;

/// <summary>
/// Authentication policy shared by submission, IMAP, JMAP, and ManageSieve.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Delily:Auth";

    public bool RequireTls { get; init; } = true;

    [Range(1, 100)]
    public int MaxFailedAttempts { get; init; } = 5;

    [Range(1, 86400)]
    public int LockoutSeconds { get; init; } = 900;

    public string[] AllowedMechanisms { get; init; } =
        ["SCRAM-SHA-256", "PLAIN"];
}
