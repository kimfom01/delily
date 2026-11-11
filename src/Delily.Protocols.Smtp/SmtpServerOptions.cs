using System.ComponentModel.DataAnnotations;

namespace Delily.Protocols.Smtp;

/// <summary>
/// Configuration for the SMTP listeners. The same options object is used for
/// inbound (port 25) and submission (587/465) until the phases split them.
/// </summary>
public sealed class SmtpServerOptions
{
    public const string SectionName = "Delily:Smtp";

    [Required]
    public string Hostname { get; init; } = "localhost";

    [Range(1, 65535)]
    public int InboundPort { get; init; } = 25;

    [Range(1, 65535)]
    public int SubmissionPort { get; init; } = 587;

    [Range(1, 65535)]
    public int SubmissionsPort { get; init; } = 465;

    [Range(1024, 1073741824)]
    public int MaxMessageSizeBytes { get; init; } = 25 * 1024 * 1024;

    [Range(1, 10000)]
    public int MaxRecipients { get; init; } = 100;

    [Range(1, 65535)]
    public int MaxConnections { get; init; } = 200;

    [Range(1, 65535)]
    public int MaxConnectionsPerIp { get; init; } = 20;

    [Range(1, 3600)]
    public int CommandTimeoutSeconds { get; init; } = 300;

    [Range(1, 3600)]
    public int DataTimeoutSeconds { get; init; } = 600;

    public bool EnableStartTls { get; init; } = true;
}
