using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace MorganHacks.Observability;

public static class LoggingSetup
{
    /// <summary>
    /// Structured JSON to stdout, and Sentry when a DSN is configured.
    /// </summary>
    /// <remarks>
    /// JSON rather than readable lines because nothing reads these with eyes.
    /// A log aggregator can answer "every line for correlation id abc123" only
    /// if the id is a field rather than a substring of a sentence.
    /// <para>
    /// stdout rather than a file: the container runtime collects it, so there
    /// is no log rotation to configure and nothing to fill a disk at 2am.
    /// </para>
    /// <para>
    /// Sentry is wired here rather than separately so that everything sent to
    /// it passes the same redaction on the way. Without a DSN it is simply not
    /// added, which is what lets a developer run all of this with no accounts.
    /// </para>
    /// </remarks>
    public static void UseArcticLogging(
        this IHostApplicationBuilder builder, string serviceName)
    {
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Information()

            // Two of the noisiest sources in an ASP.NET app. Their warnings
            // still come through; it is the per-request chatter that buries
            // everything worth reading.
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)

            .Enrich.FromLogContext()
            .Enrich.WithProperty("service", serviceName)
            .Enrich.WithProperty("environment", builder.Environment.EnvironmentName)
            .Enrich.With<RedactingEnricher>()
            .WriteTo.Console(new CompactJsonFormatter());

        var dsn = builder.Configuration["Sentry:Dsn"];
        if (!string.IsNullOrWhiteSpace(dsn))
        {
            configuration.WriteTo.Sentry(options =>
            {
                options.Dsn = dsn;
                options.Environment = builder.Environment.EnvironmentName;

                // The git SHA, so a spike in errors can be tied to what
                // shipped rather than guessed at from timestamps.
                options.Release = builder.Configuration["Sentry:Release"];

                options.MinimumEventLevel = LogEventLevel.Error;
                options.MinimumBreadcrumbLevel = LogEventLevel.Information;

                // Never on. This is what stops Sentry attaching request
                // bodies, cookies and IP addresses to every report, which is
                // most of the PII surface in an error tracker.
                options.SendDefaultPii = false;

                options.SetBeforeSend(SentryRedaction.Scrub);
            });
        }

        Log.Logger = configuration.CreateLogger();
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger);
    }
}
