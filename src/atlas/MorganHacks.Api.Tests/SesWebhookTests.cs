using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Api.Webhooks;
using MorganHacks.Lark.Data.Data;

namespace MorganHacks.Api.Tests;

/// <summary>Serves one certificate, standing in for AWS.</summary>
internal sealed class StubCertHandler(string pem) : HttpMessageHandler
{
    public int Fetches { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        Fetches++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(pem),
        });
    }
}

/// <summary>
/// The bounce webhook, signature and all.
/// </summary>
/// <remarks>
/// Signed with a key generated here rather than mocking the verifier away.
/// The verification is the entire security value of this endpoint — an
/// unauthenticated caller who can post here can stop any applicant receiving
/// email, including their sign-in link — so a test that skips it tests
/// nothing that matters.
/// </remarks>
public class SesWebhookTests(IdentityDatabase db)
    : IClassFixture<IdentityDatabase>, IAsyncLifetime
{
    private static readonly RSA Key = RSA.Create(2048);
    private static readonly string Pem = BuildPem();
    private const string CertUrl = "https://sns.us-east-1.amazonaws.com/SimpleNotification.pem";

    private WebApplicationFactory<Program> _app = null!;
    private MessageQueue Queue => new(db.DataSource);

    private static string BuildPem()
    {
        var request = new CertificateRequest(
            "CN=sns.amazonaws.com", Key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return certificate.ExportCertificatePem();
    }

    public Task InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
            b.ConfigureServices(services => services
                .AddHttpClient<ISnsSignatureVerifier, SnsSignatureVerifier>()
                .ConfigurePrimaryHttpMessageHandler(() => new StubCertHandler(Pem)));
        });
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Builds an SNS envelope, signed exactly the way AWS signs one.</summary>
    private static string Envelope(string payload, string certUrl = CertUrl, bool sign = true)
    {
        var message = new SnsMessage
        {
            Type = "Notification",
            MessageId = Guid.NewGuid().ToString(),
            TopicArn = "arn:aws:sns:us-east-1:1:ses-events",
            Message = payload,
            Timestamp = DateTimeOffset.UtcNow.ToString("O"),
            SignatureVersion = "1",
            SigningCertUrl = certUrl,
        };

        var canonical = message.CanonicalBytes()!;
        message.Signature = sign
            ? Convert.ToBase64String(Key.SignData(
                canonical, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1))
            : Convert.ToBase64String("not a signature"u8.ToArray());

        return JsonSerializer.Serialize(message);
    }

    private static string Bounce(string providerId, string type) => JsonSerializer.Serialize(new
    {
        eventType = "Bounce",
        mail = new { messageId = providerId },
        bounce = new { bounceType = type, bounceSubType = "General" },
    });

    private async Task<HttpResponseMessage> Post(string body) =>
        await _app.CreateClient().PostAsync(
            "/webhooks/ses", new StringContent(body, Encoding.UTF8, "text/plain"));

    /// <summary>A message already handed to SES, so a webhook can refer to it.</summary>
    private async Task<(Guid Id, string Email, string ProviderId)> SentMessage()
    {
        var email = $"webhook-{Guid.NewGuid():N}@example.com";
        var providerId = $"ses-{Guid.NewGuid():N}";
        var personId = await db.AddPersonAsync(email);

        await using var cmd = db.DataSource.CreateCommand("""
            WITH t AS (
              INSERT INTO notify.templates
                (key, kind, subject, body_html, body_text, from_local, from_domain)
              VALUES (gen_random_uuid()::text, 'transactional', 's', 'h', 't',
                      'login', 'auth.example.com')
              RETURNING id
            ), c AS (
              INSERT INTO notify.campaigns (template_id, name, status)
              SELECT id, 'test', 'sending' FROM t RETURNING id
            )
            INSERT INTO notify.messages
              (campaign_id, person_id, to_email, status, provider_message_id,
               rendered_subject, rendered_body_html, rendered_body_text)
            SELECT id, @person, @email, 'sent', @providerId, 's', 'h', 't' FROM c
            RETURNING id
            """);
        cmd.Parameters.AddWithValue("person", personId);
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.AddWithValue("providerId", providerId);
        return ((Guid)(await cmd.ExecuteScalarAsync())!, email, providerId);
    }

    private async Task<string> StatusOf(Guid id)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT status FROM notify.messages WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task A_forged_signature_is_refused_and_changes_nothing()
    {
        var (id, email, providerId) = await SentMessage();

        var response = await Post(Envelope(Bounce(providerId, "Permanent"), sign: false));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("sent", await StatusOf(id));
        Assert.False(await Queue.IsSuppressedAsync(email, transactional: true));
    }

    [Fact]
    public async Task A_certificate_from_somebody_elses_host_is_refused()
    {
        // The attack this endpoint is most exposed to. SigningCertURL arrives
        // inside the unverified message, so without a host check an attacker
        // serves their own certificate, signs their own payload with the
        // matching key, and every signature verifies perfectly.
        var (id, email, providerId) = await SentMessage();

        var response = await Post(Envelope(
            Bounce(providerId, "Permanent"), certUrl: "https://sns.attacker.example/cert.pem"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("sent", await StatusOf(id));
        Assert.False(await Queue.IsSuppressedAsync(email, transactional: true));
    }

    [Theory]
    [InlineData("http://sns.us-east-1.amazonaws.com/c.pem")]     // not https
    [InlineData("https://sns.us-east-1.amazonaws.com.evil.com/c.pem")]
    [InlineData("https://evil.com/sns.us-east-1.amazonaws.com")]
    [InlineData("https://amazonaws.com/c.pem")]
    public void Only_a_real_sns_host_is_trusted(string url) =>
        Assert.False(SnsSignatureVerifier.IsTrustedCertUrl(url));

    [Fact]
    public void A_real_sns_host_is_trusted() =>
        Assert.True(SnsSignatureVerifier.IsTrustedCertUrl(CertUrl));

    [Fact]
    public async Task A_permanent_bounce_suppresses_the_address()
    {
        var (id, email, providerId) = await SentMessage();

        var response = await Post(Envelope(Bounce(providerId, "Permanent")));

        response.EnsureSuccessStatusCode();
        Assert.Equal("bounced", await StatusOf(id));
        Assert.True(await Queue.IsSuppressedAsync(email, transactional: true));
    }

    [Fact]
    public async Task A_transient_bounce_does_not_suppress()
    {
        // A full mailbox or a server having a bad afternoon. Suppressing on
        // this locks somebody out over a temporary problem.
        var (id, email, providerId) = await SentMessage();

        await Post(Envelope(Bounce(providerId, "Transient")));

        Assert.Equal("bounced", await StatusOf(id));
        Assert.False(await Queue.IsSuppressedAsync(email, transactional: true));
    }

    [Fact]
    public async Task A_complaint_suppresses_on_the_very_first_one()
    {
        // Somebody pressed "this is spam". There is no grace period to give.
        var (id, email, providerId) = await SentMessage();

        await Post(Envelope(JsonSerializer.Serialize(new
        {
            eventType = "Complaint",
            mail = new { messageId = providerId },
            complaint = new { complaintFeedbackType = "abuse" },
        })));

        Assert.Equal("complained", await StatusOf(id));
        Assert.True(await Queue.IsSuppressedAsync(email, transactional: true));
    }

    [Fact]
    public async Task A_delivery_arriving_after_a_bounce_does_not_erase_it()
    {
        // Providers do not promise ordering, and letting a late delivery
        // notification overwrite a bounce puts a dead address back in
        // circulation.
        var (id, _, providerId) = await SentMessage();
        await Post(Envelope(Bounce(providerId, "Permanent")));

        await Post(Envelope(JsonSerializer.Serialize(new
        {
            eventType = "Delivery",
            mail = new { messageId = providerId },
        })));

        Assert.Equal("bounced", await StatusOf(id));
    }

    [Fact]
    public async Task The_same_notification_twice_is_harmless()
    {
        // SNS redelivers as a matter of course.
        var (id, email, providerId) = await SentMessage();
        var body = Envelope(Bounce(providerId, "Permanent"));

        await Post(body);
        var second = await Post(body);

        second.EnsureSuccessStatusCode();
        Assert.Equal("bounced", await StatusOf(id));
        Assert.True(await Queue.IsSuppressedAsync(email, transactional: true));
    }

    [Fact]
    public async Task An_event_about_a_message_we_never_sent_is_accepted_quietly()
    {
        // Answering non-2xx would make SNS retry forever over something we can
        // do nothing about.
        var response = await Post(Envelope(Bounce("ses-never-seen", "Permanent")));

        response.EnsureSuccessStatusCode();
    }
}
