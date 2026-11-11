using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Delily.Observability;

/// <summary>
/// Names and instruments shared by every Delily component. Hosts configure
/// OpenTelemetry to listen to these sources.
/// </summary>
public static class DelilyDiagnostics
{
    public const string ServiceName = "Delily";
    public const string ActivitySourceName = "Delily";
    public const string MeterName = "Delily";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "0.1.0");
    public static readonly Meter Meter = new(MeterName, "0.1.0");

    public static readonly Counter<long> SmtpMessagesAccepted =
        Meter.CreateCounter<long>("delily.smtp.messages.accepted");

    public static readonly Counter<long> SmtpMessagesRejected =
        Meter.CreateCounter<long>("delily.smtp.messages.rejected");

    public static readonly Histogram<double> DeliveryDuration =
        Meter.CreateHistogram<double>("delily.delivery.duration", unit: "s");
}
