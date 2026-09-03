using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Applications.Data;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Forms;
using MorganHacks.Applications.Services;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The applicant screen, against a real database and the real seeded baselines.
/// </summary>
/// <remarks>
/// Three things are being protected.
/// <list type="bullet">
/// <item>Paging holds while rows are arriving. Registration reads this list on
/// the morning applications close, which is the morning they are still landing
/// — and a page that skipped somebody would mean a person who never gets a
/// decision, silently.</item>
/// <item>Who is refused. Every applicant's name, school, answers and internal
/// notes are behind these routes, and the permission split is only real if
/// something checks it.</item>
/// <item>A decision leaves a trail with the right person on it. The trigger
/// reads a transaction-local setting; anything that writes a status without
/// setting it records a null actor, which looks exactly like a row fixed by
/// hand.</item>
/// </list>
/// </remarks>
public class ApplicantEndpointTests(ApplicationsDatabase db)
    : IClassFixture<ApplicationsDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;

    public Task InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);

            // A stand-in object store, because the deployed one needs a storage
            // account and this is not a test about Azure. What it buys is the
            // branch that matters here: that a link is minted for somebody
            // holding applications.view_resume, and that the key behind it
            // never reaches the wire.
            b.ConfigureTestServices(s => s.AddSingleton<IResumeStore>(new FakeResumes()));
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    private PostgresApplicationStore Applications => new(db.DataSource);

    private PostgresFormStore Forms => new(db.DataSource);

    private PostgresSubmissionStore Submissions => new(db.DataSource);

    // ------------------------------------------------------------------ list ---

    [Fact]
    public async Task A_page_ends_exactly_where_the_next_one_begins()
    {
        // The property that matters more than the page size: walking the
        // cursors visits every applicant once. An OFFSET here would show one
        // twice and skip another the moment somebody submits mid-read.
        var (eventId, cookie) = await ReadyAsync();

        var expected = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            // Descending by created_at, so the newest is first and the last
            // seeded row is the first one back.
            expected.Insert(0, await SeedAsync(eventId, at: Clock.AddMinutes(i)));
        }

        var seen = new List<Guid>();
        string? cursor = null;

        do
        {
            var page = await PageAsync(eventId, cookie, limit: 2, cursor: cursor);
            seen.AddRange(page.Items.Select(i => i.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(expected, seen);
    }

    [Fact]
    public async Task The_last_page_says_it_is_the_last_one()
    {
        // Null on the last page rather than on the page after it. Anything else
        // is a round trip that always comes back empty, once per reader.
        var (eventId, cookie) = await ReadyAsync();
        await SeedAsync(eventId);
        await SeedAsync(eventId);

        var page = await PageAsync(eventId, cookie, limit: 50);

        Assert.Equal(2, page.Items.Count);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task A_page_marker_that_is_not_ours_is_refused()
    {
        // Refused rather than ignored. Silently starting at the top would read
        // as the newest page arriving twice.
        var (eventId, cookie) = await ReadyAsync();

        var response = await Client().SendAsync(
            Get($"/admin/applicants?eventId={eventId}&cursor=not-a-cursor", cookie));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------------------------------------------------------- search ---

    [Fact]
    public async Task Search_finds_somebody_by_part_of_their_surname()
    {
        // Half a surname somebody said out loud is what registration actually
        // types. A prefix match would answer nothing for it.
        var (eventId, cookie) = await ReadyAsync();
        var surname = $"Okonkwo{Guid.NewGuid():N}"[..14];

        var wanted = await SeedAsync(eventId, last: surname);
        await SeedAsync(eventId, last: "Lovelace");

        var page = await PageAsync(eventId, cookie, q: surname[4..10]);

        Assert.Equal([wanted], page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Search_finds_somebody_by_part_of_their_address()
    {
        var (eventId, cookie) = await ReadyAsync();
        var email = $"jbaptiste-{Guid.NewGuid():N}@morgan.edu";

        var wanted = await SeedAsync(eventId, email: email);
        await SeedAsync(eventId);

        var page = await PageAsync(eventId, cookie, q: "jbaptiste");

        Assert.Equal([wanted], page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Search_matches_a_full_name_typed_as_one()
    {
        // "ada lovelace" matches neither column on its own, and somebody who
        // types it and sees nothing concludes the applicant is not there.
        var (eventId, cookie) = await ReadyAsync();
        var surname = $"Hopper{Guid.NewGuid():N}"[..12];

        var wanted = await SeedAsync(eventId, first: "Grace", last: surname);

        var page = await PageAsync(eventId, cookie, q: $"grace {surname}");

        Assert.Equal([wanted], page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Search_is_case_insensitive()
    {
        var (eventId, cookie) = await ReadyAsync();
        var surname = $"Turing{Guid.NewGuid():N}"[..12];
        var wanted = await SeedAsync(eventId, last: surname);

        var page = await PageAsync(eventId, cookie, q: surname.ToUpperInvariant());

        Assert.Equal([wanted], page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task A_wildcard_typed_into_the_search_box_is_a_character()
    {
        // Somebody searching for an underscore means the character. A bare '%'
        // reaching the pattern would match every applicant on the event
        // through a predicate that cannot use an index.
        var (eventId, cookie) = await ReadyAsync();
        await SeedAsync(eventId);
        await SeedAsync(eventId);

        var page = await PageAsync(eventId, cookie, q: "%");

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Filtering_by_status_leaves_out_everything_else()
    {
        var (eventId, cookie) = await ReadyAsync();
        var accepted = await SeedAsync(eventId, status: "accepted");
        await SeedAsync(eventId, status: "rejected");
        await SeedAsync(eventId, status: "incomplete");

        var page = await PageAsync(eventId, cookie, status: ["accepted"]);

        Assert.Equal([accepted], page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Two_statuses_are_one_filter()
    {
        // The useful filters are groups. One at a time would mean the screen
        // stitching pages together itself, and getting the ordering wrong.
        var (eventId, cookie) = await ReadyAsync();
        var accepted = await SeedAsync(eventId, status: "accepted", at: Clock);
        var waitlisted = await SeedAsync(
            eventId, status: "waitlisted", at: Clock.AddMinutes(1));
        await SeedAsync(eventId, status: "rejected");

        var page = await PageAsync(eventId, cookie, status: ["accepted", "waitlisted"]);

        Assert.Equal([waitlisted, accepted], page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task A_status_nobody_recognises_is_refused_rather_than_matching_nothing()
    {
        var (eventId, cookie) = await ReadyAsync();

        var response = await Client().SendAsync(
            Get($"/admin/applicants?eventId={eventId}&status=shortlisted", cookie));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_counts_are_of_the_event_and_not_of_the_filter()
    {
        // They are what the filters are chosen from. Counts that moved with the
        // filter would only ever confirm what the filter already said.
        var (eventId, cookie) = await ReadyAsync();
        await SeedAsync(eventId, status: "accepted");
        await SeedAsync(eventId, status: "rejected");

        var body = await Json($"/admin/applicants?eventId={eventId}&status=accepted", cookie);
        var counts = body.GetProperty("counts");

        Assert.Equal(1, counts.GetProperty("accepted").GetInt32());
        Assert.Equal(1, counts.GetProperty("rejected").GetInt32());
    }

    // ----------------------------------------------------------- permissions ---

    [Fact]
    public async Task Signing_in_is_not_enough_to_read_the_list()
    {
        // Nothing is granted by default. Being able to log in is not being able
        // to see several hundred people's names and schools.
        var cookie = await SignIn(await db.AddPersonAsync(Unique("nobody")));

        var response = await Client().SendAsync(Get("/admin/applicants", cookie));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task No_session_is_unauthorized_not_forbidden()
    {
        var response = await Client().GetAsync("/admin/applicants");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logistics_can_read_the_list_and_cannot_decide()
    {
        // The split the schema draws. Logistics holds applications.view for
        // headcount and dietary needs; it does not hold applications.decide,
        // and the route says so rather than relying on nobody trying.
        var eventId = await db.AddEventAsync();
        var id = await SeedAsync(eventId, status: "under_review");

        var person = await db.AddPersonAsync(Unique("logistics"));
        await db.AddToTeamAsync(person, "logistics");
        var cookie = await SignIn(person);

        Assert.Equal(
            HttpStatusCode.OK,
            (await Client().SendAsync(
                Get($"/admin/applicants?eventId={eventId}", cookie))).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await Decide(id, cookie, "accepted")).StatusCode);
    }

    [Fact]
    public async Task Logistics_sees_a_record_with_the_answers_and_the_notes_withheld()
    {
        // Null rather than empty, on both. "You cannot see this" and "there is
        // nothing here" are different sentences and only one of them is true.
        var eventId = await db.AddEventAsync();
        var id = await SeedAsync(eventId, status: "submitted");

        var person = await db.AddPersonAsync(Unique("logistics"));
        await db.AddToTeamAsync(person, "logistics");
        var cookie = await SignIn(person);

        var body = await Json($"/admin/applicants/{id}", cookie);

        Assert.Equal(JsonValueKind.Null, body.GetProperty("answers").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("notes").ValueKind);

        // The half they are entitled to is still there. Withholding the
        // sensitive parts is not the same as refusing the record.
        Assert.Equal("submitted", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Logistics_cannot_write_a_note()
    {
        var eventId = await db.AddEventAsync();
        var id = await SeedAsync(eventId);

        var person = await db.AddPersonAsync(Unique("logistics"));
        await db.AddToTeamAsync(person, "logistics");
        var cookie = await SignIn(person);

        var response = await Client().SendAsync(
            Post($"/admin/applicants/{id}/notes", cookie, new { body = "Nope." }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // -------------------------------------------------------------- decisions ---

    [Fact]
    public async Task A_decision_lands_in_the_trail_with_the_person_who_made_it()
    {
        // The one that matters. The trigger reads app.actor_id off the
        // transaction; a write that does not set it records a null actor, which
        // is indistinguishable from a row somebody fixed by hand at 2am.
        var eventId = await db.AddEventAsync();
        var id = await SeedAsync(eventId, status: "under_review");

        var decider = await db.AddPersonAsync(Unique("registration"));
        await db.AddToTeamAsync(decider, "registration");
        var cookie = await SignIn(decider);

        var response = await Decide(id, cookie, "accepted", "Strong project history.");
        response.EnsureSuccessStatusCode();

        var history = await Applications.HistoryOfAsync(id);

        Assert.Equal(ApplicationStatus.UnderReview, history[^1].From);
        Assert.Equal(ApplicationStatus.Accepted, history[^1].To);
        Assert.Equal(decider, history[^1].ActorId);
        Assert.Equal("Strong project history.", history[^1].Reason);

        // And the row's own decided_by, which the same setting feeds. Two
        // records of one decision that disagree would be worse than one.
        var (decidedAt, decidedBy) = await db.DecisionOf(id);
        Assert.NotNull(decidedAt);
        Assert.Equal(decider, decidedBy);
    }

    [Fact]
    public async Task A_decision_is_not_part_of_a_batch()
    {
        // batch_id is how somebody finds the other three hundred and ninety
        // nine when one of a bulk accept was wrong. Filling it in for a single
        // decision would make one indistinguishable from one of those.
        var eventId = await db.AddEventAsync();
        var id = await SeedAsync(eventId, status: "under_review");

        var cookie = await SignIn(await Registration());
        (await Decide(id, cookie, "accepted")).EnsureSuccessStatusCode();

        Assert.Null((await Applications.HistoryOfAsync(id))[^1].BatchId);
    }

    [Fact]
    public async Task A_move_the_lifecycle_forbids_is_refused_and_says_what_is_allowed()
    {
        // 409 rather than 400: the request was well formed and the application
        // is simply not where the caller thought it was, which on a shared
        // queue usually means somebody else moved it first.
        var eventId = await db.AddEventAsync();
        var id = await SeedAsync(eventId, status: "submitted");

        var cookie = await SignIn(await Registration());

        var response = await Decide(id, cookie, "accepted");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await Body(response);
        Assert.Equal("submitted", body.GetProperty("status").GetString());
        Assert.Contains(
            "under_review",
            body.GetProperty("allowedNext").EnumerateArray().Select(s => s.GetString()));
    }

    [Fact]
    public async Task A_refused_move_changes_nothing_and_writes_no_history()
    {
        var eventId = await db.AddEventAsync();
        var id = await SeedAsync(eventId, status: "submitted");
        var before = await db.HistoryCountAsync(id);

        var cookie = await SignIn(await Registration());
        await Decide(id, cookie, "accepted");

        Assert.Equal(ApplicationStatus.Submitted, await Applications.StatusOfAsync(id));
        Assert.Equal(before, await db.HistoryCountAsync(id));
    }

    [Fact]
    public async Task A_status_nobody_recognises_is_refused()
    {
        var eventId = await db.AddEventAsync();
        var id = await SeedAsync(eventId, status: "under_review");
        var cookie = await SignIn(await Registration());

        var response = await Decide(id, cookie, "shortlisted");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deciding_an_applicant_who_is_not_there_is_a_404()
    {
        var cookie = await SignIn(await Registration());

        var response = await Decide(Guid.NewGuid(), cookie, "accepted");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ------------------------------------------------------------------ notes ---

    [Fact]
    public async Task A_note_comes_back_on_the_record_with_its_author()
    {
        var eventId = await db.AddEventAsync();
        var id = await SeedAsync(eventId);

        var author = await Registration();
        var cookie = await SignIn(author);

        var written = await Client().SendAsync(
            Post($"/admin/applicants/{id}/notes", cookie, new { body = "Asked about travel." }));

        Assert.Equal(HttpStatusCode.Created, written.StatusCode);

        var notes = (await Json($"/admin/applicants/{id}", cookie))
            .GetProperty("notes").EnumerateArray().ToList();

        Assert.Single(notes);
        Assert.Equal("Asked about travel.", notes[0].GetProperty("body").GetString());
        Assert.Equal(author, notes[0].GetProperty("authorId").GetGuid());
    }

    [Fact]
    public async Task An_empty_note_is_refused()
    {
        var eventId = await db.AddEventAsync();
        var id = await SeedAsync(eventId);
        var cookie = await SignIn(await Registration());

        var response = await Client().SendAsync(
            Post($"/admin/applicants/{id}/notes", cookie, new { body = "   " }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------- answers and files ---

    [Fact]
    public async Task The_answers_come_back_under_the_questions_that_were_asked()
    {
        var form = await PublishedAsync(Question("favourite_language", "Favourite language"));
        var id = await SubmitAsync(form, ("favourite_language", "Elixir"));

        var cookie = await SignIn(await Registration());

        var answers = (await Json($"/admin/applicants/{id}", cookie))
            .GetProperty("answers").EnumerateArray().ToList();

        var answer = answers.Single(
            a => a.GetProperty("key").GetString() == "favourite_language");

        Assert.Equal("Favourite language", answer.GetProperty("label").GetString());
        Assert.Equal("Elixir", answer.GetProperty("value").GetString());
    }

    [Fact]
    public async Task An_application_nobody_finished_has_no_answers_rather_than_no_record()
    {
        // The row exists from the moment somebody starts the form, so a
        // half-filled draft is an ordinary thing to open.
        var eventId = await db.AddEventAsync();
        var id = await Applications.StartAsync(eventId, Unique("draft"));

        var cookie = await SignIn(await Registration());
        var body = await Json($"/admin/applicants/{id}", cookie);

        Assert.Equal("incomplete", body.GetProperty("status").GetString());
        Assert.Empty(body.GetProperty("answers").EnumerateArray());
    }

    [Fact]
    public async Task A_resume_comes_back_as_a_link_and_never_as_a_key()
    {
        // The column holds a key and not a URL precisely so that a way to read
        // somebody's CV cannot be copied out of a response and kept. A screen
        // that leaked the key would undo that on its own.
        var eventId = await db.AddEventAsync();
        var id = await SeedAsync(eventId, status: "submitted");
        await AttachResumeAsync(id, "resumes/2027/secret-object-name");

        var cookie = await SignIn(await Registration());
        var response = await Client().SendAsync(Get($"/admin/applicants/{id}", cookie));
        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("secret-object-name", raw, StringComparison.Ordinal);

        var resume = JsonDocument.Parse(raw).RootElement.GetProperty("resume");
        Assert.Equal("ada.pdf", resume.GetProperty("filename").GetString());
        Assert.StartsWith(
            "https://blobs.test/",
            resume.GetProperty("url").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Comms_sees_that_there_is_a_resume_and_gets_no_link_to_it()
    {
        // Comms holds applications.view to build segments and not
        // applications.view_resume. Knowing a file exists is not reading it.
        var eventId = await db.AddEventAsync();
        var id = await SeedAsync(eventId, status: "submitted");
        await AttachResumeAsync(id, "resumes/2027/another-object-name");

        var person = await db.AddPersonAsync(Unique("comms"));
        await db.AddToTeamAsync(person, "comms");
        var cookie = await SignIn(person);

        var body = await Json($"/admin/applicants/{id}", cookie);

        Assert.True(body.GetProperty("hasResume").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("resume").ValueKind);
    }

    // -------------------------------------------------------------- fixtures ---

    /// <summary>
    /// Fixed, so a seeded ordering is written down rather than left to how
    /// fast the machine ran.
    /// </summary>
    private static readonly DateTimeOffset Clock =
        new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private sealed record ApplicantRow(Guid Id, string Email, string Status);

    private sealed record ListedPage(IReadOnlyList<ApplicantRow> Items, string? NextCursor);

    /// <summary>An event, and somebody on registration looking at it.</summary>
    private async Task<(Guid EventId, string Cookie)> ReadyAsync() =>
        (await db.AddEventAsync(), await SignIn(await Registration()));

    private async Task<Guid> Registration()
    {
        var person = await db.AddPersonAsync(Unique("registration"));
        await db.AddToTeamAsync(person, "registration");
        return person;
    }

    /// <summary>
    /// One applicant, written straight into the table.
    /// </summary>
    /// <remarks>
    /// Raw SQL rather than the submission path, because these tests are about
    /// the list rather than about the form: they need a chosen name, a chosen
    /// status and a chosen <c>created_at</c>, and the submission path decides
    /// all three for itself. The MLH columns are filled in because the
    /// completeness constraint insists on them the moment a row stops being a
    /// draft, which is the constraint working.
    /// </remarks>
    private async Task<Guid> SeedAsync(
        Guid eventId,
        string? email = null,
        string first = "Ada",
        string last = "Lovelace",
        string status = "submitted",
        DateTimeOffset? at = null)
    {
        const string sql = """
            INSERT INTO applications.applications
                (event_id, email, first_name, last_name, school, status, created_at,
                 age, phone, level_of_study, country,
                 mlh_coc_agreed_at, mlh_data_sharing_at, submitted_at)
            VALUES (@eventId, @email, @first, @last, 'Morgan State University',
                    @status, @at, 20, '+1 555 0100', 'undergraduate',
                    'United States', now(), now(),
                    CASE WHEN @status = 'incomplete' THEN NULL ELSE @at END)
            RETURNING id
            """;

        await using var cmd = db.DataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("email", email ?? Unique("applicant"));
        cmd.Parameters.AddWithValue("first", first);
        cmd.Parameters.AddWithValue("last", last);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("at", at ?? Clock);

        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Points an application at bytes the fake store will sign for.</summary>
    private async Task AttachResumeAsync(Guid id, string key)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            UPDATE applications.applications
               SET resume_key = @key, resume_filename = 'ada.pdf', resume_size = 1024
             WHERE id = @id
            """);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("key", key);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>A live application form on an event of its own.</summary>
    private async Task<Form> PublishedAsync(params FormField[] extra)
    {
        var form = await Forms.CreateAsync(
            await db.AddEventAsync(), "Application", "application", null);

        var draft = await Forms.DraftAsync(form.Id, null);
        await Forms.SaveDraftAsync(form.Id, [.. draft.Fields, .. extra]);
        await Forms.PublishAsync(form.Id, null);

        return form;
    }

    private static FormField Question(string key, string label) => new()
    {
        Key = key,
        Type = FieldType.ShortText,
        Label = label,
    };

    private async Task<Guid> SubmitAsync(
        Form form, params (string Key, object? Value)[] extra)
    {
        var answers = new Dictionary<string, object?>
        {
            ["email"] = Unique("applicant"),
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
            answers[key] = value;
        }

        var published = await Forms.PublishedAsync(form.Id);

        return await Submissions.SubmitApplicationAsync(
            form,
            published!,
            answers.ToDictionary(a => a.Key, a => JsonSerializer.SerializeToElement(a.Value)));
    }

    // ------------------------------------------------------------------ wire ---

    private HttpClient Client() => _app.CreateClient(
        new WebApplicationFactoryClientOptions { HandleCookies = false });

    /// <summary>Gives a person a live session and returns their cookie.</summary>
    /// <remarks>
    /// Minted directly rather than by driving a login flow. These tests are
    /// about what a session is permitted to do, not about how it was obtained,
    /// and everyone here is an organizer — who signs in through Google.
    /// </remarks>
    private async Task<string> SignIn(Guid personId)
    {
        using var scope = _app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return $"mh_session={await sessions.StartAsync(personId)}";
    }

    private static HttpRequestMessage Get(string path, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookie);
        return request;
    }

    private static HttpRequestMessage Post(string path, string cookie, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };

        request.Headers.Add("Cookie", cookie);
        return request;
    }

    private Task<HttpResponseMessage> Decide(
        Guid id, string cookie, string status, string? reason = null) =>
        Client().SendAsync(
            Post($"/admin/applicants/{id}/status", cookie, new { status, reason }));

    private async Task<JsonElement> Json(string path, string cookie)
    {
        var response = await Client().SendAsync(Get(path, cookie));
        response.EnsureSuccessStatusCode();
        return await Body(response);
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<ListedPage> PageAsync(
        Guid eventId,
        string cookie,
        int limit = 50,
        string? cursor = null,
        string? q = null,
        IEnumerable<string>? status = null)
    {
        var query = new List<string>
        {
            $"eventId={eventId}",
            $"limit={limit}",
        };

        if (cursor is not null)
        {
            query.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        if (q is not null)
        {
            query.Add($"q={Uri.EscapeDataString(q)}");
        }

        foreach (var one in status ?? [])
        {
            query.Add($"status={Uri.EscapeDataString(one)}");
        }

        var body = await Json($"/admin/applicants?{string.Join('&', query)}", cookie);

        var items = body.GetProperty("items").EnumerateArray()
            .Select(i => new ApplicantRow(
                i.GetProperty("id").GetGuid(),
                i.GetProperty("email").GetString()!,
                i.GetProperty("status").GetString()!))
            .ToList();

        var next = body.GetProperty("nextCursor");

        return new ListedPage(
            items, next.ValueKind == JsonValueKind.Null ? null : next.GetString());
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@morgan.edu";

    /// <summary>A stand-in object store. See the note in InitializeAsync.</summary>
    private sealed class FakeResumes : IResumeStore
    {
        public bool Available => true;

        public Task<string> StoreAsync(
            Guid eventId, ReadOnlyMemory<byte> content, CancellationToken ct = default) =>
            throw new NotSupportedException("Nothing here uploads.");

        public Task<SignedResume> LinkToAsync(
            string storageKey, string downloadName, CancellationToken ct = default) =>
            Task.FromResult(new SignedResume(
                new Uri($"https://blobs.test/{downloadName}?sig=signed"),
                DateTimeOffset.UtcNow.Add(IResumeStore.LinkLifetime)));
    }
}
