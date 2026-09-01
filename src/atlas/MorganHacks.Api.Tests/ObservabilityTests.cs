using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using MorganHacks.Observability;
using Sentry;
using Serilog.Events;
using Serilog.Parsing;

namespace MorganHacks.Api.Tests;

/// <summary>
/// What must never leave the process.
/// </summary>
/// <remarks>
/// The rule everywhere else is to log person_id rather than an address. This
/// is the net underneath that rule, so it is worth testing the net rather than
/// trusting everyone to remember.
/// </remarks>
public class RedactionTests
{
    private static LogEvent Line(params (string Name, object Value)[] properties)
    {
        var evt = new LogEvent(
            DateTimeOffset.UtcNow, LogEventLevel.Information, null,
            new MessageTemplate("test", []), []);

        foreach (var (name, value) in properties)
        {
            evt.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(value)));
        }

        return evt;
    }

    private static string? Read(LogEvent evt, string name) =>
        evt.Properties[name] is ScalarValue s ? s.Value?.ToString() : null;

    [Theory]
    [InlineData("email")]
    [InlineData("to_email")]
    [InlineData("phone")]
    [InlineData("responses")]
    [InlineData("resume_key")]
    [InlineData("token")]
    [InlineData("link")]
    public void A_sensitive_property_never_reaches_the_line(string key)
    {
        // `link` and `token` matter as much as the PII ones: a magic link in a
        // log is a working sign-in for anyone who can read logs.
        var evt = Line((key, "something private"));

        new RedactingEnricher().Enrich(evt, null!);

        Assert.Equal(Redaction.Placeholder, Read(evt, key));
    }

    [Fact]
    public void An_address_hiding_in_free_text_is_masked_too()
    {
        // The usual way this happens is a database error quoting the row it
        // rejected, where there is no key to match on.
        var evt = Line(("error", "duplicate key: ada@morgan.edu already exists"));

        new RedactingEnricher().Enrich(evt, null!);

        var line = Read(evt, "error")!;
        Assert.DoesNotContain("ada@morgan.edu", line);
        Assert.Contains("duplicate key", line);
    }

    [Fact]
    public void An_ordinary_property_is_left_alone()
    {
        // Redaction that swallows everything is redaction nobody keeps.
        var evt = Line(("PersonId", "8f14e45f"), ("status", "accepted"));

        new RedactingEnricher().Enrich(evt, null!);

        Assert.Equal("8f14e45f", Read(evt, "PersonId"));
        Assert.Equal("accepted", Read(evt, "status"));
    }

    [Fact]
    public void A_query_string_never_reaches_sentry()
    {
        // This is where a magic-link token lives.
        var evt = new SentryEvent
        {
            Request = new SentryRequest
            {
                Url = "https://morganhacks.com/auth/consume",
                QueryString = "token=live-sign-in-token",
            },
        };

        var scrubbed = SentryRedaction.Scrub(evt, new SentryHint());

        Assert.Null(scrubbed!.Request.QueryString);
    }

    [Fact]
    public void Sentry_extras_and_tags_are_scrubbed()
    {
        var evt = new SentryEvent();
        evt.SetExtra("email", "ada@morgan.edu");
        evt.SetExtra("note", "failed for ada@morgan.edu");
        evt.SetTag("school", "Morgan State");

        var scrubbed = SentryRedaction.Scrub(evt, new SentryHint())!;

        Assert.Equal(Redaction.Placeholder, scrubbed.Extra["email"]);
        Assert.DoesNotContain("ada@morgan.edu", scrubbed.Extra["note"]!.ToString()!);
        Assert.Equal("Morgan State", scrubbed.Tags["school"]);
    }
}

/// <summary>The id that ties one person's request together across services.</summary>
public class CorrelationTests(IdentityDatabase db)
    : IClassFixture<IdentityDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;

    public Task InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(
            b => b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString));
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task An_inbound_id_is_handed_back()
    {
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Add(Telemetry.CorrelationIdHeader, "abc123");

        var response = await client.GetAsync("/health");

        Assert.Equal("abc123",
            response.Headers.GetValues(Telemetry.CorrelationIdHeader).Single());
    }

    [Fact]
    public async Task A_request_without_one_still_gets_an_id()
    {
        // A health check, or somebody hitting the service directly during an
        // incident, is still worth being able to trace.
        var response = await _app.CreateClient().GetAsync("/health");

        Assert.NotEmpty(response.Headers.GetValues(Telemetry.CorrelationIdHeader).Single());
    }

    [Fact]
    public async Task An_implausible_id_is_replaced_rather_than_echoed()
    {
        // It ends up on every log line this request writes, so its shape is
        // never the caller's to decide.
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Add(Telemetry.CorrelationIdHeader, new string('x', 500));

        var response = await client.GetAsync("/health");

        var returned = response.Headers.GetValues(Telemetry.CorrelationIdHeader).Single();
        Assert.True(returned.Length <= 64);
    }

    [Fact]
    public async Task A_queued_message_carries_the_id_of_the_request_that_caused_it()
    {
        // The send happens minutes later in another process, so a log line
        // alone would tie the request to the queueing and nothing after it.
        var email = $"corr-{Guid.NewGuid():N}@example.com";
        await db.AddPersonAsync(email);

        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Add(Telemetry.CorrelationIdHeader, "trace-me-42");
        await client.PostAsJsonAsync("/auth/magic-link", new { email });

        await using var cmd = db.DataSource.CreateCommand(
            "SELECT correlation_id FROM notify.messages WHERE to_email = @e");
        cmd.Parameters.AddWithValue("e", email);
        Assert.Equal("trace-me-42", await cmd.ExecuteScalarAsync());
    }
}
