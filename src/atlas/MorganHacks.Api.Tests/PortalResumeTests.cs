using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Services;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api.Tests;

/// <summary>
/// An applicant attaching and replacing their own resume, from the portal.
/// </summary>
/// <remarks>
/// Against a real database and a real object store, because both halves of
/// what matters here are things a mock would agree to. The rule that an
/// applicant only ever writes their own row lives in a WHERE clause, and a
/// fake store returns whatever the test told it to — so the questions worth
/// asking are "what did the SQL actually reach" and "what is really in the
/// container", and neither survives being stubbed.
/// <para>
/// The object store is Azurite with OAuth on, the same fixture the delegated
/// link tests use, wrapped in a recorder. The wrapper is only there so a test
/// can say "and nothing was written", which is the assertion that separates a
/// refusal from a message about a refusal.
/// </para>
/// </remarks>
public class PortalResumeTests(IdentityDatabase db, DelegatedBlobStorage blobs)
    : IClassFixture<IdentityDatabase>, IClassFixture<DelegatedBlobStorage>, IAsyncLifetime
{
    private RecordingResumeStore _store = null!;
    private WebApplicationFactory<Program> _app = null!;

    public Task InitializeAsync()
    {
        _store = new RecordingResumeStore(blobs.Store);

        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);

            // Said rather than inherited from features.json, for the reason
            // PortalTests gives: the portal is off by default and a suite that
            // read the default would go red every time somebody moved a switch.
            b.UseSetting("enable_hacker_portal_feature", "true");

            b.ConfigureServices(s => s.AddSingleton<IResumeStore>(_store));
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------- holding ---

    [Fact]
    public async Task An_applicant_with_no_resume_is_told_so_rather_than_shown_nothing()
    {
        // A blank screen and "we do not have one" look the same to a page and
        // completely different to a person. The route answers 200 with a null
        // resume rather than 404, so the page has a state to render instead of
        // an error to translate.
        var person = await db.AddPersonAsync(Unique("empty"));
        await AddApplicationAsync(await AddEventAsync(), person);

        var body = await ReadJson("/portal/resume", await SignIn(person));

        Assert.Equal(JsonValueKind.Null, body.GetProperty("resume").ValueKind);
        Assert.True(body.GetProperty("started").GetBoolean());
        Assert.True(body.GetProperty("editable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("lockedReason").ValueKind);
    }

    [Fact]
    public async Task Somebody_who_has_not_applied_is_told_that_rather_than_that_it_is_locked()
    {
        // The two "you cannot upload" states need different sentences and the
        // page has no way to tell them apart from `editable` alone — both are
        // false. `started` is what separates "you have not applied yet" from
        // "this is no longer yours to change", and it is said outright rather
        // than inferred from a null reason.
        var person = await db.AddPersonAsync(Unique("neverapplied"));

        var body = await ReadJson("/portal/resume", await SignIn(person));

        Assert.False(body.GetProperty("started").GetBoolean());
        Assert.False(body.GetProperty("editable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("resume").ValueKind);

        // And no locked sentence, because there is nothing locked — telling
        // somebody who has not applied that their resume is with the team
        // would be nonsense.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("lockedReason").ValueKind);
    }

    [Fact]
    public async Task An_applicant_attaches_a_resume_and_is_shown_what_we_are_holding()
    {
        // The whole feature in one test: the file goes up, the bytes land in
        // the container, and the applicant is told the name, the size and the
        // date rather than being left to wonder whether it worked.
        var person = await db.AddPersonAsync(Unique("attach"));
        var application = await AddApplicationAsync(await AddEventAsync(), person);
        var cookie = await SignIn(person);

        var uploaded = await Upload(cookie, Pdf(2048), "Ada Lovelace CV.pdf");
        uploaded.EnsureSuccessStatusCode();

        var written = Assert.Single(_store.Written);
        Assert.Equal(written.Key, await ResumeKeyOf(application));

        var body = await ReadJson("/portal/resume", cookie);
        var resume = body.GetProperty("resume");

        Assert.Equal("Ada Lovelace CV.pdf", resume.GetProperty("filename").GetString());

        // Measured while we held the bytes, not taken from a header. A size the
        // caller sent would be a claim about a file we are holding anyway.
        Assert.Equal(2048, resume.GetProperty("size").GetInt32());
        Assert.NotEqual(
            JsonValueKind.Null, resume.GetProperty("uploadedAt").ValueKind);
    }

    [Fact]
    public async Task The_bytes_really_are_in_the_private_container()
    {
        // End to end rather than as far as the row. Everything above this could
        // pass with an object store that quietly dropped the content, and the
        // failure would surface months later as a sponsor opening an empty
        // file.
        var person = await db.AddPersonAsync(Unique("azurite"));
        await AddApplicationAsync(await AddEventAsync(), person);

        (await Upload(await SignIn(person), Pdf(4096))).EnsureSuccessStatusCode();

        var key = Assert.Single(_store.Written).Key;
        var link = await blobs.Store.LinkToAsync(key, "resume-test.pdf");

        using var http = Trusting();
        var signed = await http.GetAsync(link.Url);

        signed.EnsureSuccessStatusCode();
        Assert.Equal(4096, (await signed.Content.ReadAsByteArrayAsync()).Length);

        // And the applicant's upload did not widen the container on its way in.
        // The same URL without the signature has to be refused, or every resume
        // in the account is a guessable URL away from the public internet.
        var unsigned = await http.GetAsync(link.Url.GetLeftPart(UriPartial.Path));
        Assert.False(unsigned.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Nothing_the_portal_says_about_a_resume_includes_where_it_lives()
    {
        // resume_key is on Redaction.SensitiveKeys because it is the one string
        // that turns "somebody has a CV" into "here it is". The portal has no
        // use for it, so the test is that it never appears — in the upload's
        // answer or in the read.
        var person = await db.AddPersonAsync(Unique("nokey"));
        await AddApplicationAsync(await AddEventAsync(), person);
        var cookie = await SignIn(person);

        var uploaded = await Upload(cookie, Pdf());
        var written = await uploaded.Content.ReadAsStringAsync();
        var read = await ReadString("/portal/resume", cookie);

        var key = Assert.Single(_store.Written).Key;

        Assert.DoesNotContain(key, written, StringComparison.Ordinal);
        Assert.DoesNotContain(key, read, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_replacement_takes_the_place_of_what_was_there()
    {
        // The reason this endpoint exists. Somebody who uploaded a CV in
        // October and has since finished an internship has to be able to hand
        // us the new one, and there is exactly one resume on an application —
        // so the second upload replaces rather than adds.
        var person = await db.AddPersonAsync(Unique("replace"));
        var application = await AddApplicationAsync(await AddEventAsync(), person);
        var cookie = await SignIn(person);

        (await Upload(cookie, Pdf(1024), "old.pdf")).EnsureSuccessStatusCode();
        (await Upload(cookie, Pdf(2048), "new.pdf")).EnsureSuccessStatusCode();

        var resume = (await ReadJson("/portal/resume", cookie)).GetProperty("resume");
        Assert.Equal("new.pdf", resume.GetProperty("filename").GetString());
        Assert.Equal(2048, resume.GetProperty("size").GetInt32());

        // Two objects, one row. Everybody's resume is called resume.pdf, so a
        // key derived from the name would have made the second upload overwrite
        // the first — and the applicant whose CV vanished would have no way to
        // know.
        Assert.Equal(2, _store.Written.Select(w => w.Key).Distinct().Count());
        Assert.Equal(_store.Written[1].Key, await ResumeKeyOf(application));
    }

    // ---------------------------------------------------------- refusals ---

    [Fact]
    public async Task A_file_that_is_not_a_pdf_is_refused_however_it_is_named()
    {
        // The check that separates "we accept resumes" from "we accept anything
        // and hand it to a sponsor to open". A .pdf on the end of a filename
        // costs nothing to write, and so does an application/pdf content type
        // on the part — both are claims, and neither is consulted.
        var person = await db.AddPersonAsync(Unique("nothtml"));
        await AddApplicationAsync(await AddEventAsync(), person);

        var response = await Upload(
            await SignIn(person),
            Encoding.UTF8.GetBytes("<html><script>alert(1)</script>"),
            "resume.pdf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // A refusal that still writes the bytes is not a refusal.
        Assert.Empty(_store.Written);

        // And it has to say what was wrong, because the person reading it
        // believes they attached a perfectly good PDF.
        Assert.Contains("not a PDF", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_file_over_five_megabytes_is_refused()
    {
        // Measured over the bytes as they are read rather than from a length
        // the request declared, and before any of them are written down.
        var person = await db.AddPersonAsync(Unique("toobig"));
        await AddApplicationAsync(await AddEventAsync(), person);

        var response = await Upload(
            await SignIn(person), Pdf(ResumeFile.MaxBytes + 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_store.Written);
        Assert.Contains("over 5 MB", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_file_of_exactly_five_megabytes_is_accepted()
    {
        // The other side of the same boundary. A cap that quietly refuses the
        // largest file we say we accept is a cap that gets reported as a bug by
        // somebody who did exactly what they were told.
        var person = await db.AddPersonAsync(Unique("exact"));
        await AddApplicationAsync(await AddEventAsync(), person);

        var response = await Upload(await SignIn(person), Pdf(ResumeFile.MaxBytes));

        response.EnsureSuccessStatusCode();
        Assert.Single(_store.Written);
    }

    [Fact]
    public async Task An_empty_file_is_refused_with_something_to_do_about_it()
    {
        // The ordinary cause is a file that had not finished syncing out of a
        // cloud drive when it was picked. "Invalid file" is the version of this
        // message that gets a support email.
        var person = await db.AddPersonAsync(Unique("emptyfile"));
        await AddApplicationAsync(await AddEventAsync(), person);

        var response = await Upload(await SignIn(person), []);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_store.Written);
        Assert.Contains("empty", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_request_with_no_file_on_it_is_refused_rather_than_crashing()
    {
        // A POST that is not multipart at all, which is what a script or a
        // half-written page sends. It has to be an answer, not a 500.
        var person = await db.AddPersonAsync(Unique("nofile"));
        await AddApplicationAsync(await AddEventAsync(), person);

        var request = new HttpRequestMessage(HttpMethod.Post, "/portal/resume")
        {
            Content = JsonContent.Create(new { file = "here you go" }),
        };
        request.Headers.Add("Cookie", await SignIn(person));

        var response = await Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_store.Written);
    }

    // ------------------------------------------------------ somebody else ---

    [Fact]
    public async Task An_upload_never_reaches_another_applicants_row()
    {
        // The rule the whole portal rests on, on the write side. Two applicants
        // with applications on the same event; one uploads, and the other's row
        // has to be untouched.
        //
        // There is deliberately no request this test could make that names the
        // other application, because there is no parameter for it — which is
        // the actual defence. What would break it is a WHERE clause that
        // narrowed on the event, or on nothing at all, and that is what this
        // catches.
        var mine = await db.AddPersonAsync(Unique("mine"));
        var theirs = await db.AddPersonAsync(Unique("theirs"));
        var eventId = await AddEventAsync();

        var minesApplication = await AddApplicationAsync(eventId, mine);
        var untouched = await AddApplicationAsync(eventId, theirs);

        (await Upload(await SignIn(mine), Pdf())).EnsureSuccessStatusCode();

        var key = Assert.Single(_store.Written).Key;

        Assert.Equal(key, await ResumeKeyOf(minesApplication));
        Assert.Null(await ResumeKeyOf(untouched));
    }

    [Fact]
    public async Task An_upload_cannot_be_aimed_at_another_applicant_by_asking()
    {
        // The same rule, attacked the way somebody actually would: by adding
        // the other person's ids to the request in every spelling a binder
        // might pick up. None of them can win, because nothing in the handler
        // or the SQL reads an id from the request at all.
        var mine = await db.AddPersonAsync(Unique("mine"));
        var theirs = await db.AddPersonAsync(Unique("theirs"));
        var eventId = await AddEventAsync();

        await AddApplicationAsync(eventId, mine);
        var untouched = await AddApplicationAsync(eventId, theirs);

        using var body = new MultipartFormDataContent();
        var part = new ByteArrayContent(Pdf());
        part.Headers.ContentType = new MediaTypeHeaderValue(ResumeFile.ContentType);
        body.Add(part, "file", "resume.pdf");
        body.Add(new StringContent(untouched.ToString()), "applicationId");
        body.Add(new StringContent(untouched.ToString()), "id");
        body.Add(new StringContent(theirs.ToString()), "personId");

        var request = new HttpRequestMessage(HttpMethod.Post, "/portal/resume")
        {
            Content = body,
        };
        request.Headers.Add("Cookie", await SignIn(mine));

        // Accepted, because those fields are not an attack on anything — they
        // are ignored. What matters is where the bytes ended up.
        (await Client().SendAsync(request)).EnsureSuccessStatusCode();

        Assert.Null(await ResumeKeyOf(untouched));
    }

    [Fact]
    public async Task An_applicant_cannot_see_that_another_applicant_has_one()
    {
        // The read half. A second applicant asking the same endpoint gets their
        // own empty page, not the other one's filename — which on a resume is
        // usually somebody's full name.
        var theirs = await db.AddPersonAsync(Unique("theirs"));
        var eventId = await AddEventAsync();
        await AddApplicationAsync(eventId, theirs);

        (await Upload(await SignIn(theirs), Pdf(), "Ada Lovelace CV.pdf"))
            .EnsureSuccessStatusCode();

        var nosy = await db.AddPersonAsync(Unique("nosy"));
        await AddApplicationAsync(eventId, nosy);

        var body = await ReadString("/portal/resume", await SignIn(nosy));

        Assert.DoesNotContain("Ada Lovelace", body, StringComparison.Ordinal);
        Assert.Contains("\"resume\":null", body.Replace(" ", string.Empty));
    }

    [Fact]
    public async Task Without_a_session_neither_route_answers()
    {
        // An applicant holds no permissions, so 403 would be the wrong answer
        // for every one of them. The only thing missing is a session.
        var client = Client();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/portal/resume")).StatusCode);

        using var body = new MultipartFormDataContent();
        body.Add(new ByteArrayContent(Pdf()), "file", "resume.pdf");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsync("/portal/resume", body)).StatusCode);

        Assert.Empty(_store.Written);
    }

    // ------------------------------------------------------------- window ---

    [Fact]
    public async Task Somebody_who_has_not_applied_has_nowhere_to_put_one()
    {
        // Signed in, no application. Refused with the sentence rather than a
        // bare 409, and without reading the file first.
        var person = await db.AddPersonAsync(Unique("noapp"));

        var response = await Upload(await SignIn(person), Pdf());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(_store.Written);
        Assert.Contains(
            "not started an application", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(ApplicationStatus.Incomplete)]
    [InlineData(ApplicationStatus.Submitted)]
    [InlineData(ApplicationStatus.UnderReview)]
    [InlineData(ApplicationStatus.Accepted)]
    [InlineData(ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Waitlisted)]
    [InlineData(ApplicationStatus.Confirmed)]
    public async Task A_decided_application_can_still_replace_its_resume(
        ApplicationStatus status)
    {
        // The deliberate difference from ProfileEditing, pinned so nobody
        // "fixes" the two rules into one. The profile locks at the decision
        // because shirts and catering are ordered from it; a resume is read by
        // sponsors at the event, months later, and the version somebody
        // uploaded in a hurry in October is the wrong one by then.
        //
        // Every status here is one where somebody might still be met by a
        // sponsor, or — for rejected and waitlisted — one that has to behave
        // identically to accepted. See the next test for why that matters.
        var person = await db.AddPersonAsync(Unique($"open-{status}"));
        var application = await AddApplicationAsync(
            await AddEventAsync(), person, ApplicationStatus.Incomplete);
        await Decide(application, status);

        var response = await Upload(await SignIn(person), Pdf());

        response.EnsureSuccessStatusCode();
        Assert.Equal(Assert.Single(_store.Written).Key, await ResumeKeyOf(application));
    }

    [Fact]
    public async Task The_picker_does_not_tell_anybody_their_decision_early()
    {
        // The constraint that shaped the status set. Accepted, rejected and
        // waitlisted have to behave identically here, because two friends
        // comparing screens in the week before results would otherwise have
        // been told the answer by a disabled button — which is exactly the leak
        // ProfileEditing closes all three together to avoid.
        var eventId = await AddEventAsync();

        var accepted = await db.AddPersonAsync(Unique("accepted"));
        var rejected = await db.AddPersonAsync(Unique("rejected"));

        await Decide(
            await AddApplicationAsync(eventId, accepted, ApplicationStatus.Incomplete),
            ApplicationStatus.Accepted);
        await Decide(
            await AddApplicationAsync(eventId, rejected, ApplicationStatus.Incomplete),
            ApplicationStatus.Rejected);

        var a = await ReadJson("/portal/resume", await SignIn(accepted));
        var b = await ReadJson("/portal/resume", await SignIn(rejected));

        Assert.True(a.GetProperty("editable").GetBoolean());
        Assert.True(b.GetProperty("editable").GetBoolean());
        Assert.Equal(
            JsonValueKind.Null, a.GetProperty("lockedReason").ValueKind);
        Assert.Equal(
            JsonValueKind.Null, b.GetProperty("lockedReason").ValueKind);
    }

    [Theory]
    [InlineData(ApplicationStatus.Withdrawn)]
    [InlineData(ApplicationStatus.Declined)]
    [InlineData(ApplicationStatus.Expired)]
    public async Task A_closed_application_refuses_the_upload_and_says_why(
        ApplicationStatus status)
    {
        // Refused with a sentence rather than a bare 409, and refused before a
        // byte is read — somebody whose application is closed should not be
        // able to make us hold five megabytes to be told no.
        //
        // These three are safe to close precisely because the applicant already
        // knows they are in them: two are their own doing, and expired is only
        // reachable from accepted.
        var person = await db.AddPersonAsync(Unique($"closed-{status}"));
        var application = await AddApplicationAsync(
            await AddEventAsync(), person, ApplicationStatus.Incomplete);
        await Decide(application, status);

        var response = await Upload(await SignIn(person), Pdf());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(_store.Written);
        Assert.Contains("Email us", await response.Content.ReadAsStringAsync());
        Assert.Null(await ResumeKeyOf(application));
    }

    [Fact]
    public async Task A_closed_application_is_told_why_before_it_picks_a_file()
    {
        // The read carries the same sentence the write would answer with, so
        // the page can explain the disabled picker instead of leaving somebody
        // to discover it by trying. A greyed-out control with no reason is the
        // email this portal exists to prevent.
        var person = await db.AddPersonAsync(Unique("lockedread"));
        var application = await AddApplicationAsync(
            await AddEventAsync(), person, ApplicationStatus.Incomplete);
        await Decide(application, ApplicationStatus.Withdrawn);

        var body = await ReadJson("/portal/resume", await SignIn(person));

        Assert.False(body.GetProperty("editable").GetBoolean());
        Assert.Contains("Email us", body.GetProperty("lockedReason").GetString()!);
    }

    // ------------------------------------------------------------- helpers ---

    private HttpClient Client() => _app.CreateClient(
        new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    /// <summary>Gives a person a live session and returns their cookie header.</summary>
    private async Task<string> SignIn(Guid personId)
    {
        using var scope = _app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return $"mh_session={await sessions.StartAsync(personId)}";
    }

    /// <summary>Bytes that really are a PDF, however short.</summary>
    private static byte[] Pdf(int size = 1024)
    {
        var bytes = new byte[size];
        "%PDF-1.7\n"u8.CopyTo(bytes);
        return bytes;
    }

    /// <summary>A client that accepts Azurite's throwaway certificate.</summary>
    private static HttpClient Trusting() => new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
    });

    private async Task<HttpResponseMessage> Upload(
        string cookie, byte[] content, string filename = "resume.pdf")
    {
        using var body = new MultipartFormDataContent();
        var part = new ByteArrayContent(content);

        // Declared as a PDF on every upload, including the ones that are not
        // one. Nothing on the other side reads this header — the bytes decide —
        // and sending it makes that explicit rather than accidental.
        part.Headers.ContentType = new MediaTypeHeaderValue(ResumeFile.ContentType);
        body.Add(part, "file", filename);

        var request = new HttpRequestMessage(HttpMethod.Post, "/portal/resume")
        {
            Content = body,
        };
        request.Headers.Add("Cookie", cookie);

        return await Client().SendAsync(request);
    }

    private async Task<string> ReadString(string path, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookie);

        var response = await Client().SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<JsonElement> ReadJson(string path, string cookie) =>
        JsonDocument.Parse(await ReadString(path, cookie)).RootElement.Clone();

    private async Task<Guid> AddEventAsync()
    {
        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO applications.events (slug, name)
            VALUES (@slug, 'Test event')
            RETURNING id
            """);
        cmd.Parameters.AddWithValue("slug", $"event-{Guid.NewGuid():N}");
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// An application belonging to a person, complete enough for any status.
    /// </summary>
    /// <remarks>
    /// The MLH-required fields are filled even for a draft, because the
    /// completeness constraint refuses any status past <c>incomplete</c>
    /// without them.
    /// </remarks>
    private async Task<Guid> AddApplicationAsync(
        Guid eventId,
        Guid personId,
        ApplicationStatus status = ApplicationStatus.Submitted)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO applications.applications
                (event_id, person_id, email, status,
                 first_name, last_name, age, phone, school, level_of_study,
                 country, mlh_coc_agreed_at, mlh_data_sharing_at)
            VALUES (@eventId, @personId, @email, @status,
                    'Ada', 'Lovelace', 20, '+15550000000',
                    'Morgan State University', 'undergraduate-3y',
                    'United States', now(), now())
            RETURNING id
            """);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("personId", personId);
        cmd.Parameters.AddWithValue("email", Unique("app"));
        cmd.Parameters.AddWithValue("status", status.ToWire());
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// What is actually on the row, which is the only answer that settles
    /// whether a write landed where it was supposed to.
    /// </summary>
    private async Task<string?> ResumeKeyOf(Guid applicationId)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT resume_key FROM applications.applications WHERE id = @id");
        cmd.Parameters.AddWithValue("id", applicationId);
        return await cmd.ExecuteScalarAsync() as string;
    }

    /// <summary>Moves an application, one legal step at a time.</summary>
    private async Task Decide(Guid applicationId, ApplicationStatus to)
    {
        if (to == ApplicationStatus.Incomplete)
        {
            return;
        }

        await using var connection = await db.DataSource.OpenConnectionAsync();

        foreach (var step in RouteTo(to))
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "UPDATE applications.applications SET status = @s WHERE id = @id";
            cmd.Parameters.AddWithValue("s", step.ToWire());
            cmd.Parameters.AddWithValue("id", applicationId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// The legal route from a fresh application to the status a test wants.
    /// </summary>
    /// <remarks>
    /// Spelled out rather than jumped to, so the rows these tests read have the
    /// lifecycle timestamps a real application would — the triggers only stamp
    /// them on a genuine transition.
    /// </remarks>
    private static ApplicationStatus[] RouteTo(ApplicationStatus to) => to switch
    {
        ApplicationStatus.Submitted => [ApplicationStatus.Submitted],
        ApplicationStatus.UnderReview =>
            [ApplicationStatus.Submitted, ApplicationStatus.UnderReview],
        ApplicationStatus.Accepted or ApplicationStatus.Rejected
            or ApplicationStatus.Waitlisted =>
            [ApplicationStatus.Submitted, ApplicationStatus.UnderReview, to],
        ApplicationStatus.Confirmed or ApplicationStatus.Declined
            or ApplicationStatus.Expired =>
        [
            ApplicationStatus.Submitted, ApplicationStatus.UnderReview,
            ApplicationStatus.Accepted, to,
        ],
        _ => [to],
    };

    /// <summary>
    /// The real Azurite-backed store, with a note of everything written
    /// through it.
    /// </summary>
    /// <remarks>
    /// A wrapper rather than a replacement, so the tests still exercise a real
    /// object store and can still assert that a refused upload wrote nothing.
    /// "It answered 400" and "it answered 400 and kept the file anyway" are
    /// different outcomes, and only the second one is a bug worth catching.
    /// </remarks>
    private sealed class RecordingResumeStore(IResumeStore inner) : IResumeStore
    {
        public List<(Guid EventId, string Key, int Size)> Written { get; } = [];

        public bool Available => inner.Available;

        public async Task<string> StoreAsync(
            Guid eventId, ReadOnlyMemory<byte> content, CancellationToken ct = default)
        {
            var key = await inner.StoreAsync(eventId, content, ct);
            Written.Add((eventId, key, content.Length));
            return key;
        }

        public Task<SignedResume> LinkToAsync(
            string storageKey, string downloadName, CancellationToken ct = default) =>
            inner.LinkToAsync(storageKey, downloadName, ct);
    }
}
