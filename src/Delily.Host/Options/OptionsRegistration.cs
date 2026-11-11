using Delily.Api;
using Delily.Auth;
using Delily.Protocols.Imap;
using Delily.Protocols.Smtp;
using Delily.Queue;
using Delily.Storage;
using Delily.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Delily.Host.Options;

/// <summary>
/// Binds and validates every configuration section at startup. A bad port or
/// empty required value fails fast instead of surfacing on the first connection.
/// </summary>
public static class OptionsRegistration
{
    public static IServiceCollection AddDelilyOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SmtpServerOptions>()
            .Bind(configuration.GetSection(SmtpServerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ImapServerOptions>()
            .Bind(configuration.GetSection(ImapServerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<QueueOptions>()
            .Bind(configuration.GetSection(QueueOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<TlsOptions>()
            .Bind(configuration.GetSection(TlsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<HttpSubmissionOptions>()
            .Bind(configuration.GetSection(HttpSubmissionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
