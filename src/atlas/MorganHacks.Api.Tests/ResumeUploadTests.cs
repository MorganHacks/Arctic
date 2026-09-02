using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Forms;
using MorganHacks.Applications.Services;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The resume, end to end: uploaded by a stranger, attached to an application,
/// read back by an organizer.
/// </summary>
/// <remarks>
/// This is the only place in the platform where arbitrary bytes from the
/// public internet are kept and later opened in somebody's browser, so most of
/// what is asserted here is a refusal. The upload endpoint has no
/// authentication of any kind — the form's code is the whole permission — and
/// every rule that only exists in the page is a rule that does not exist.
/// <para>
/// The object store is a stand-in. What it replaces is a network call to
/// Azure, and none of the decisions worth protecting live there: what is
/// accepted, what the key is made of, and who may ask for a link are all
/// decided on this side of it.
/// </para>
/// </remarks>
public class ResumeUploadTests(ApplicationsDatabase db)
    : IClassFixture<ApplicationsDatabase>, IAsyncLifetime
{
    private readonly RecordingResumeStore _store = new();
    private WebApplicationFactory<Program> _app = null!;

    public Task InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
            b.ConfigureServices(s => s.AddSingleton<IResumeStore>(_store));
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    private PostgresFormStore Forms => new(db.DataSource);

    private HttpClient Client() => _app.CreateClient(
        new WebApplicationFactoryClientOptions { HandleCookies = false });

    /// <summary>The key an applicant's resume question is stored under.</summary>
    private const string ResumeKey = "resume";

    /// <summary>An application form with a resume question on it.</summary>
    /// <remarks>
    /// An event each, because the dedupe rule is scoped to one and every test
    /// here submits with an address of its own.
    /// </remarks>
    private async Task<Form> PublishedAsync()
    {
        var form = await Forms.CreateAsync(
            await db.AddEventAsync(), "Application", "application", null);

        var draft = await Forms.DraftAsync(form.Id, null);
        await Forms.SaveDraftAsync(form.Id, [.. draft.Fields, new FormField
        {
            Key = ResumeKey,
            Type = FieldType.File,
            Label = "Your resume",
        }]);

        await Forms.PublishAsync(form.Id, null);
        return form;
    }

    /// <summary>Bytes that really are a PDF, however short.</summary>
    private static byte[] Pdf(int size = 1024)
    {
        var bytes = new byte[size];
        "%PDF-1.7\n"u8.CopyTo(bytes);
        return bytes;
    }

    private async Task<HttpResponseMessage> Upload(
        string code, byte[] content, string filename = "resume.pdf")
    {
        using var body = new MultipartFormDataContent();
        var part = new ByteArrayContent(content);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        body.Add(part, "file", filename);

        return await Client().PostAsync($"/forms/{code}/resume", body);
    }

    private static async Task<Guid> UploadIdOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("upload").GetGuid();
    }

    /// <summary>A complete set of answers. A null value takes the question out.</summary>
    private static object Answers(string email, params (string Key, object? Value)[] extra)
    {
        var answers = new Dictionary<string, object>
        {
            ["email"] = email,
            ["first_name"] = "Ada",
            ["last_name"] = "Lovelace",
            ["age"] = 20,
            ["phone"] = "+1 555 0100",
            ["school"] = "Morgan State University",
            ["country"] = "United States",
            ["level_of_study"] = "undergraduate-3y",
            ["mlh_coc_agreed_at"] = true,
            ["mlh_data_sharing_at"] = true,
        };

        foreach (var (key, value) in extra)
        {
            if (value is null)
            {
                answers.Remove(key);
            }
            else
            {
                answers[key] = value;
            }
        }

        return new { answers };
    }

    private Task<HttpResponseMessage> Submit(string code, object body) =>
        Client().PostAsJsonAsync($"/forms/{code}/submit", body);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@morgan.edu";

    private async Task<T?> ColumnAsync<T>(string email, string column)
    {
        await using var cmd = db.DataSource.CreateCommand(
            $"SELECT {column} FROM applications.applications WHERE lower(email) = lower(@e)");
        cmd.Parameters.AddWithValue("e", email);

        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    // ------------------------------------------------------------ refusals ---

    [Fact]
    public async Task A_file_that_is_not_a_pdf_is_refused_however_it_is_named()
    {
        // The check that separates "we accept resumes" from "we accept
        // anything and hand it to an organizer to open". A .pdf on the end of
        // a filename is a claim by whoever uploaded it and costs nothing to
        // write, so the content has to be what decides.
        var form = await PublishedAsync();

        var response = await Upload(
            form.Code, Encoding.UTF8.GetBytes("<html><script>alert(1)</script>"), "resume.pdf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // And nothing reached the store. A refusal that still writes the bytes
        // is not a refusal.
        Assert.Empty(_store.Written);

        // The message has to say what was wrong and what to do, because the
        // person reading it believes they attached a perfectly good PDF.
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("not a PDF", body);
    }

    [Fact]
    public async Task A_file_over_five_megabytes_is_refused()
    {
        // Enforced over the bytes as they are read rather than from the length
        // the request declared, and before any of them are written down. The
        // page checks the size too; that check saves somebody pushing five
        // megabytes up campus wifi to be told no, and it is not a limit.
        var form = await PublishedAsync();

        var response = await Upload(form.Code, Pdf(ResumeFile.MaxBytes + 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_store.Written);
        Assert.Contains("over 5 MB", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_file_of_exactly_five_megabytes_is_accepted()
    {
        // The other side of the same boundary. A cap that quietly refuses the
        // largest file we say we accept is a cap that gets reported as a bug
        // by somebody who did exactly what they were told.
        var form = await PublishedAsync();

        var response = await Upload(form.Code, Pdf(ResumeFile.MaxBytes));

        response.EnsureSuccessStatusCode();
        Assert.Single(_store.Written);
    }

    [Fact]
    public void A_pdf_header_has_to_be_at_the_very_start()
    {
        // Some readers tolerate junk before the header. Matching them would
        // mean accepting a file that is an HTML page for its first kilobyte
        // and a PDF afterwards, which is the whole trick.
        var padded = new byte[64];
        "<html>"u8.CopyTo(padded);
        "%PDF-"u8.CopyTo(padded.AsSpan(32));

        Assert.Equal(ResumeRejection.NotAPdf, ResumeFile.Inspect(padded));
    }

    // ---------------------------------------------------------------- keys ---

    [Fact]
    public async Task The_key_a_resume_is_stored_under_is_nothing_like_its_name()
    {
        // A filename is attacker-controlled and path traversal is the obvious
        // consequence of building a key out of one. The key is generated, so
        // there is nothing in it to traverse with and no name to collide on.
        var form = await PublishedAsync();

        var response = await Upload(form.Code, Pdf(), "../../etc/passwd.pdf");
        response.EnsureSuccessStatusCode();

        var key = Assert.Single(_store.Written).Key;

        Assert.DoesNotContain("passwd", key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("..", key, StringComparison.Ordinal);

        // Foldered by event and named by a fresh guid, so a year can be found
        // as a unit and two applicants can upload the same file without one
        // overwriting the other.
        Assert.StartsWith($"{form.EventId:N}/", key, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_uploads_of_the_same_file_get_different_keys()
    {
        // Everybody's resume is called resume.pdf. Keys derived from the name
        // would make the second upload overwrite the first, and the applicant
        // whose CV disappeared would have no way to know.
        var form = await PublishedAsync();

        (await Upload(form.Code, Pdf())).EnsureSuccessStatusCode();
        (await Upload(form.Code, Pdf())).EnsureSuccessStatusCode();

        Assert.Equal(2, _store.Written.Select(w => w.Key).Distinct().Count());
    }

    // ------------------------------------------------------- attaching one ---

    [Fact]
    public async Task A_resume_reaches_the_application_it_was_uploaded_for()
    {
        var form = await PublishedAsync();
        var email = Unique("attached");

        var uploaded = await Upload(form.Code, Pdf(2048), "Ada Lovelace CV.pdf");
        uploaded.EnsureSuccessStatusCode();
        var upload = await UploadIdOf(uploaded);

        var submitted = await Submit(
            form.Code, Answers(email, (ResumeKey, new { upload })));
        submitted.EnsureSuccessStatusCode();

        var key = Assert.Single(_store.Written).Key;
        Assert.Equal(key, await ColumnAsync<string>(email, "resume_key"));

        // The size is what was measured while we held the bytes, not a number
        // that arrived with the answers. The old shape took both the name and
        // the size from the request, which made resume_size a claim.
        Assert.Equal(2048, await ColumnAsync<int?>(email, "resume_size"));
        Assert.Equal("Ada Lovelace CV.pdf", await ColumnAsync<string>(email, "resume_filename"));
        Assert.NotNull(await ColumnAsync<DateTime?>(email, "resume_uploaded_at"));
    }

    [Fact]
    public async Task An_upload_id_nobody_was_issued_is_refused()
    {
        // The reason the page is handed an id rather than the key: an id can
        // be checked against something we wrote, and a key cannot be checked
        // against anything at all.
        var form = await PublishedAsync();

        var response = await Submit(
            form.Code,
            Answers(Unique("invented"), (ResumeKey, new { upload = Guid.NewGuid() })));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_upload_cannot_be_attached_to_two_applications()
    {
        // Spent once, inside the transaction that writes the key. Without
        // that, one upload backs any number of applications and two people can
        // be filed under one person's resume.
        var form = await PublishedAsync();

        var uploaded = await Upload(form.Code, Pdf());
        uploaded.EnsureSuccessStatusCode();
        var upload = await UploadIdOf(uploaded);

        var first = await Submit(
            form.Code, Answers(Unique("first"), (ResumeKey, new { upload })));
        first.EnsureSuccessStatusCode();

        var second = await Submit(
            form.Code, Answers(Unique("second"), (ResumeKey, new { upload })));

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task An_upload_made_for_one_form_cannot_be_spent_on_another()
    {
        // Two forms are two events, so an upload spent across them would file
        // a resume under a cycle it was never sent to.
        var mine = await PublishedAsync();
        var theirs = await PublishedAsync();

        var uploaded = await Upload(mine.Code, Pdf());
        uploaded.EnsureSuccessStatusCode();
        var upload = await UploadIdOf(uploaded);

        var response = await Submit(
            theirs.Code, Answers(Unique("elsewhere"), (ResumeKey, new { upload })));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_submission_that_is_refused_leaves_the_upload_spendable()
    {
        // The duplicate is the ordinary way a submit fails: somebody applies,
        // then applies again. Their upload has to survive it, or a second
        // attempt asks them to find the file again for a reason that is
        // nothing to do with them.
        var form = await PublishedAsync();
        var email = Unique("retried");

        (await Submit(form.Code, Answers(email))).EnsureSuccessStatusCode();

        var uploaded = await Upload(form.Code, Pdf());
        uploaded.EnsureSuccessStatusCode();
        var upload = await UploadIdOf(uploaded);

        // Refused on the address, with the upload already claimed inside the
        // transaction that then rolled back.
        var duplicate = await Submit(
            form.Code, Answers(email, (ResumeKey, new { upload })));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var accepted = await Submit(
            form.Code, Answers(Unique("retried-again"), (ResumeKey, new { upload })));
        accepted.EnsureSuccessStatusCode();
    }

    // --------------------------------------------------------- reading one ---

    [Fact]
    public async Task A_signed_link_is_only_given_to_somebody_who_may_read_resumes()
    {
        // The permission model separates applications.view_resume from
        // applications.view on purpose: a CV carries a home address, a phone
        // number and often a photograph that the form never asked for.
        // Logistics holds view and not view_resume, which is exactly the case
        // this has to refuse.
        var applicationId = await AnApplicationWithAResume();

        var stranger = await Client().GetAsync($"/applications/{applicationId}/resume");
        Assert.Equal(HttpStatusCode.Unauthorized, stranger.StatusCode);

        var logistics = await db.AddPersonAsync(Unique("logistics"));
        await db.AddToTeamAsync(logistics, "logistics");

        var refused = await Client().SendAsync(
            Request($"/applications/{applicationId}/resume", await SignIn(logistics)));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task A_reviewer_gets_a_link_that_stops_working_in_five_minutes()
    {
        // Short enough that a URL copied out of an address bar and pasted into
        // a group chat is already dead by the time anybody clicks it. The
        // expiry comes back with it so the review screen can ask for a fresh
        // one rather than showing a broken frame.
        var applicationId = await AnApplicationWithAResume();

        var reviewer = await db.AddPersonAsync(Unique("reviewer"));
        await db.AddToTeamAsync(reviewer, "registration");

        var response = await Client().SendAsync(
            Request($"/applications/{applicationId}/resume", await SignIn(reviewer)));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var expiresAt = body.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange(
            expiresAt - DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(4),
            TimeSpan.FromMinutes(5));

        // The link carries the type and the disposition, so a file a stranger
        // uploaded arrives as a PDF and nothing else. Without them a browser
        // decides for itself what it is looking at.
        var url = body.GetProperty("url").GetString()!;
        Assert.Contains("rsct=application%2Fpdf", url, StringComparison.Ordinal);
        Assert.Contains("inline", url, StringComparison.Ordinal);

        // And the name in the disposition is ours, not the applicant's. That
        // value goes into a header, where a newline is header injection and a
        // quote changes what the rest of it means.
        Assert.DoesNotContain("Lovelace", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_application_with_no_resume_says_so()
    {
        // Ordinary rather than exceptional: the resume question is not always
        // required, and a reviewer opening one of those should be told there
        // is nothing rather than shown an error.
        var form = await PublishedAsync();
        var email = Unique("bare");
        (await Submit(form.Code, Answers(email))).EnsureSuccessStatusCode();

        var reviewer = await db.AddPersonAsync(Unique("bare-reviewer"));
        await db.AddToTeamAsync(reviewer, "registration");

        var id = await ColumnAsync<Guid?>(email, "id");
        var response = await Client().SendAsync(
            Request($"/applications/{id}/resume", await SignIn(reviewer)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------- plumbing ---

    private async Task<Guid> AnApplicationWithAResume()
    {
        var form = await PublishedAsync();
        var email = Unique("reviewed");

        var uploaded = await Upload(form.Code, Pdf(), "Ada Lovelace CV.pdf");
        uploaded.EnsureSuccessStatusCode();

        var submitted = await Submit(
            form.Code, Answers(email, (ResumeKey, new { upload = await UploadIdOf(uploaded) })));
        submitted.EnsureSuccessStatusCode();

        return (await ColumnAsync<Guid?>(email, "id"))!.Value;
    }

    /// <summary>Gives a person a live session and returns their cookie.</summary>
    private async Task<string> SignIn(Guid personId)
    {
        using var scope = _app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return $"mh_session={await sessions.StartAsync(personId)}";
    }

    private static HttpRequestMessage Request(string path, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookie);
        return request;
    }

    /// <summary>
    /// An object store that keeps what it was given, in memory.
    /// </summary>
    /// <remarks>
    /// Stands in for the network call to Azure and for nothing else. The keys
    /// it generates and the link it signs are shaped like the real ones,
    /// because the tests above are about what the key is made of and what the
    /// link says — neither of which is Azure's decision.
    /// </remarks>
    private sealed class RecordingResumeStore : IResumeStore
    {
        public List<(string Key, byte[] Content)> Written { get; } = [];

        public bool Available => true;

        public Task<string> StoreAsync(
            Guid eventId, ReadOnlyMemory<byte> content, CancellationToken ct = default)
        {
            var key = ResumeFile.NewKey(eventId);
            Written.Add((key, content.ToArray()));
            return Task.FromResult(key);
        }

        public Task<SignedResume> LinkToAsync(
            string storageKey, string downloadName, CancellationToken ct = default)
        {
            if (!Written.Any(w => w.Key == storageKey))
            {
                throw new ResumeMissingException("No resume is stored under that key.");
            }

            var expiresAt = DateTimeOffset.UtcNow + IResumeStore.LinkLifetime;

            // The same query parameters Azure signs a read link with: rsct is
            // the content type it will be served as and rscd is the
            // disposition. Spelled out here so the assertions above are about
            // the contract rather than about this class.
            var url = new Uri(
                $"https://example.invalid/resumes/{storageKey}"
                + $"?se={Uri.EscapeDataString(expiresAt.ToString("O"))}"
                + $"&rsct={Uri.EscapeDataString(ResumeFile.ContentType)}"
                + $"&rscd={Uri.EscapeDataString($"inline; filename=\"{downloadName}\"")}");

            return Task.FromResult(new SignedResume(url, expiresAt));
        }
    }
}
