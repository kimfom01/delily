using Delily.Api;
using Delily.Host.Options;
using Delily.Observability;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddDelilyOptions(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

if (builder.Configuration.GetValue<bool>("Delily:OpenTelemetry:Enabled"))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService(DelilyDiagnostics.ServiceName))
        .WithTracing(tracing => tracing
            .AddSource(DelilyDiagnostics.ActivitySourceName)
            .AddAspNetCoreInstrumentation()
            .AddOtlpExporter())
        .WithMetrics(metrics => metrics
            .AddMeter(DelilyDiagnostics.MeterName)
            .AddOtlpExporter());
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => Results.Ok(new
{
    service = DelilyDiagnostics.ServiceName,
    status = "ok"
}));

// Liveness: the process can answer HTTP. Readiness: all registered checks pass.
// Storage/DB checks are added in Phase 2.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");

app.MapDelilyApi();

app.Run();

public partial class Program;
