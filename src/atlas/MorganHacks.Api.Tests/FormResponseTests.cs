using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Applications.Forms;
using MorganHacks.Applications.Services;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api.Tests;

/// <summary>
/// Reading answers back, against a real database and the real seeded baselines.
/// </summary>
/// <remarks>
/// Two things are being protected. One is that an answer somebody gave comes
/// back under the question they answered, no matter how the form has been
/// edited since — which is the whole reason a field has a stable key.
/// <para>
/// The other is that this cannot become a way around the permission model.
/// Every applicant's name, address, phone number and essay is on the other
/// side of these three routes, so who is refused matters at least as much as
/// what comes back for the people who are not.
/// </para>
/// </remarks>
public class FormResponseTests(ApplicationsDatabase db)
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
            // holding applications.view_resume and for nobody else.
            b.ConfigureTestServices(s => s.AddSingleton<IResumeStore>(new FakeResumes()));
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------- fixtures ---

    private PostgresFormStore Forms => new(db.DataSource);

    private PostgresSubmissionStore Submissions => new(db.DataSource);

    /// <summary>A live application form on an event of its own.</summary>
    /// <remarks>
    /// An event each, because the dedupe rule is scoped to one and every test
    /// here submits several times.
    /// </remarks>
    private async Task<Form> PublishedAsync(params FormField[] extra)
    {
        var form = await Forms.CreateAsync(
            await db.AddEventAsync(), "Application", "application", null);

        var draft = await Forms.DraftAsync(form.Id, null);
        await Forms.SaveDraftAsync(form.Id, [.. draft.Fields, .. extra]);
        await Forms.PublishAsync(form.Id, null);

        return form;
    }

    /// <summary>Republishes a form with a different question set.</summary>
    private async Task RepublishAsync(Guid formId, params FormField[] fields)
    {
        var draft = await Forms.DraftAsync(formId, null);
        await Forms.SaveDraftAsync(formId, [.. draft.Fields, .. fields]);
        await Forms.PublishAsync(formId, null);
    }

    private static FormField Question(string key, string label = "A question") => new()
    {
        Key = key,
        Type = FieldType.ShortText,
        Label = label,
    };

    private static FormField PageBreak(string key, string label = "Page two") => new()
    {
        Key = key,
        Type = FieldType.Section,
        Label = label,
    };

    /// <summary>A complete set of answers to MLH's questions.</summary>
    private static Dictionary<string, JsonElement> Answers(
        string email, params (string Key, object? Value)[] extra)
    {
        var answers = new Dictionary<string, object?>
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

        return answers.ToDictionary(
            p => p.Key, p => JsonSerializer.SerializeToElement(p.Value));
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@morgan.edu";

    private async Task<Guid> SubmitAsync(Form form, params (string Key, object? Value)[] extra)
    {
        var published = await Forms.PublishedAsync(form.Id);
        return await Submissions.SubmitApplicationAsync(
            form, published!, Answers(Unique("applicant"), extra));
    }

    /// <summary>
    /// Pins when a response was submitted.
    /// </summary>
    /// <remarks>
    /// The ordering and the cursor are both built on this column, and a test
    /// that leaves it to <c>now()</c> is a test whose expected order depends on
    /// how fast the machine ran. Setting it lets the tie case be written down
    /// rather than hoped for.
    /// </remarks>
    private async Task SubmittedAtAsync(Guid applicationId, DateTimeOffset at)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "UPDATE applications.applications SET submitted_at = @at WHERE id = @id");
        cmd.Parameters.AddWithValue("at", at);
        cmd.Parameters.AddWithValue("id", applicationId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Attaches a resume to a row that was submitted without one.</summary>
    private async Task AttachResumeAsync(Guid applicationId)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            UPDATE applications.applications
               SET resume_key = @key, resume_filename = 'ada-cv.pdf',
                   resume_size = 4096, resume_uploaded_at = now()
             WHERE id = @id
            """);
        cmd.Parameters.AddWithValue("key", $"resumes/{Guid.NewGuid():N}.pdf");
        cmd.Parameters.AddWithValue("id", applicationId);
        await cmd.ExecuteNonQueryAsync();
    }

    // --------------------------------------------------------------- callers ---

    private HttpClient Client() => _app.CreateClient(
        new WebApplicationFactoryClientOptions { HandleCookies = false });

    /// <summary>Gives a person a live session and returns their cookie.</summary>
    /// <remarks>
    /// Minted directly rather than by driving a sign-in. Everyone here is an
    /// organizer, and organizers sign in through Google — what is under test is
    /// what a session may do, not how it was got.
    /// </remarks>
    private async Task<string> SignIn(Guid personId)
    {
        using var scope = _app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return $"mh_session={await sessions.StartAsync(personId)}";
    }

    /// <summary>Somebody who may read responses and nothing else.</summary>
    private async Task<string> ReaderAsync()
    {
        var id = await db.AddPersonAsync(Unique("reader"));
        await db.GrantAsync(id, "applications.view_responses");
        return await SignIn(id);
    }

    private async Task<string> TeamAsync(string slug)
    {
        var id = await db.AddPersonAsync(Unique(slug));
        await db.AddToTeamAsync(id, slug);
        return await SignIn(id);
    }

    private static HttpRequestMessage Request(string path, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookie);
        return request;
    }

    private async Task<JsonElement> ReadAsync(string path, string cookie)
    {
        var response = await Client().SendAsync(Request(path, cookie));
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private async Task<string> CsvAsync(string path, string cookie)
    {
        var response = await Client().SendAsync(Request(path, cookie));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    // ------------------------------------------------------------ permissions ---

    [Fact]
    public async Task No_session_is_unauthorized_rather_than_forbidden()
    {
        var form = await PublishedAsync();

        var response = await Client().GetAsync($"/admin/forms/{form.Id}/responses");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reading_the_questions_is_not_reading_the_answers()
    {
        // Logistics holds applications.view for headcount and dietary needs,
        // which is also what the form builder is behind. That must not carry
        // through to several hundred people's essays about themselves.
        var form = await PublishedAsync();
        var cookie = await TeamAsync("logistics");

        var response = await Client().SendAsync(
            Request($"/admin/forms/{form.Id}/responses", cookie));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Comms_can_segment_without_reading_what_anybody_wrote()
    {
        var form = await PublishedAsync();
        var cookie = await TeamAsync("comms");

        var response = await Client().SendAsync(
            Request($"/admin/forms/{form.Id}/responses", cookie));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Registration_reads_the_answers_because_registration_decides_them()
    {
        var form = await PublishedAsync();
        var cookie = await TeamAsync("registration");

        var response = await Client().SendAsync(
            Request($"/admin/forms/{form.Id}/responses", cookie));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Reading_responses_is_not_exporting_them()
    {
        // Taking a copy out of the system is its own permission and is on the
        // sensitive list. Somebody who may read the queue on screen has not
        // been given a spreadsheet of it.
        var form = await PublishedAsync();
        var cookie = await ReaderAsync();

        var response = await Client().SendAsync(
            Request($"/admin/forms/{form.Id}/responses.csv", cookie));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Exporting_is_allowed_with_the_export_permission()
    {
        var form = await PublishedAsync();
        var id = await db.AddPersonAsync(Unique("exporter"));
        await db.GrantAsync(id, "applications.export");

        var response = await Client().SendAsync(
            Request($"/admin/forms/{form.Id}/responses.csv", await SignIn(id)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --------------------------------------------------------------- reading ---

    [Fact]
    public async Task An_answer_comes_back_under_the_key_of_the_question_it_answered()
    {
        var form = await PublishedAsync(Question("why_apply", "Why do you want to come?"));
        await SubmitAsync(form, ("why_apply", "To build something."));

        var body = await ReadAsync($"/admin/forms/{form.Id}/responses", await ReaderAsync());
        var answers = body.GetProperty("items")[0].GetProperty("answers");

        // One from the responses jsonb and one promoted to a column. Both read
        // back as answers, and a caller cannot tell which was which.
        Assert.Equal("To build something.", answers.GetProperty("why_apply").GetString());
        Assert.Equal("Morgan State University", answers.GetProperty("school").GetString());
    }

    [Fact]
    public async Task A_question_nobody_answered_is_absent_rather_than_null()
    {
        // An absent key is how a caller tells "not answered" from "answered
        // with nothing", and a screen drawing a blank line for every optional
        // question nobody filled in is a screen nobody can read.
        var form = await PublishedAsync(Question("why_apply"));
        await SubmitAsync(form);

        var body = await ReadAsync($"/admin/forms/{form.Id}/responses", await ReaderAsync());
        var answers = body.GetProperty("items")[0].GetProperty("answers");

        Assert.False(answers.TryGetProperty("why_apply", out _));
    }

    [Fact]
    public async Task A_survey_has_no_responses_rather_than_the_applications_beside_it()
    {
        // An application carries no form id, so responses are found by event.
        // A survey sharing an event with the application form would otherwise
        // answer with the application's responses under the survey's id, which
        // is a permission check passed against the wrong form.
        var form = await PublishedAsync();
        await SubmitAsync(form);

        var survey = await Forms.CreateAsync(form.EventId, "Feedback", "survey", null);
        await Forms.DraftAsync(survey.Id, null);
        await Forms.PublishAsync(survey.Id, null);

        var body = await ReadAsync($"/admin/forms/{survey.Id}/responses", await ReaderAsync());

        Assert.Empty(body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task A_form_that_is_not_there_is_a_404()
    {
        var response = await Client().SendAsync(
            Request($"/admin/forms/{Guid.NewGuid()}/responses", await ReaderAsync()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ------------------------------------------------------------ pagination ---

    [Fact]
    public async Task Paging_walks_every_response_exactly_once()
    {
        var form = await PublishedAsync();
        var start = DateTimeOffset.UtcNow.AddHours(-1);

        var submitted = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var id = await SubmitAsync(form);
            await SubmittedAtAsync(id, start.AddMinutes(i));
            submitted.Add(id);
        }

        var cookie = await ReaderAsync();
        var seen = new List<Guid>();
        string? cursor = null;

        do
        {
            var query = cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}";
            var page = await ReadAsync(
                $"/admin/forms/{form.Id}/responses?limit=2{query}", cookie);

            seen.AddRange(page.GetProperty("items").EnumerateArray()
                .Select(i => i.GetProperty("id").GetGuid()));

            cursor = page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        // Newest first, every one of them, none of them twice.
        Assert.Equal(Enumerable.Reverse(submitted), seen);
    }

    [Fact]
    public async Task The_last_page_says_there_is_nothing_after_it()
    {
        // Otherwise every reader makes one more request that comes back empty,
        // and a screen with an always-enabled "next" button.
        var form = await PublishedAsync();
        await SubmitAsync(form);
        await SubmitAsync(form);

        var page = await ReadAsync(
            $"/admin/forms/{form.Id}/responses?limit=50", await ReaderAsync());

        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task Two_responses_submitted_in_the_same_instant_both_survive_paging()
    {
        // A launch meeting where a room submits at once is exactly this. A
        // cursor on the timestamp alone either loses the second one or repeats
        // it forever, which is why the id is in there with it.
        var form = await PublishedAsync();
        var moment = DateTimeOffset.UtcNow.AddHours(-2);

        var first = await SubmitAsync(form);
        var second = await SubmitAsync(form);
        await SubmittedAtAsync(first, moment);
        await SubmittedAtAsync(second, moment);

        var cookie = await ReaderAsync();
        var seen = new List<Guid>();
        string? cursor = null;

        do
        {
            var query = cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}";
            var page = await ReadAsync(
                $"/admin/forms/{form.Id}/responses?limit=1{query}", cookie);

            seen.AddRange(page.GetProperty("items").EnumerateArray()
                .Select(i => i.GetProperty("id").GetGuid()));

            cursor = page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        Assert.Equal(2, seen.Count);
        Assert.Contains(first, seen);
        Assert.Contains(second, seen);
    }

    [Fact]
    public async Task The_cursor_says_nothing_about_the_ordering_underneath()
    {
        // Opaque so the ordering columns stay ours. A caller who can read a
        // timestamp out of a cursor is one who will eventually build one, and
        // then submitted_at is a public API.
        var form = await PublishedAsync();
        await SubmitAsync(form);
        await SubmitAsync(form);

        var page = await ReadAsync(
            $"/admin/forms/{form.Id}/responses?limit=1", await ReaderAsync());

        var cursor = page.GetProperty("nextCursor").GetString()!;
        var newest = page.GetProperty("items")[0].GetProperty("id").GetGuid();

        Assert.DoesNotContain(newest.ToString(), cursor, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("submitted", cursor, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_cursor_we_did_not_issue_is_refused_rather_than_ignored()
    {
        // Starting them silently at the top would read as the newest page
        // arriving a second time, halfway down a list somebody is working.
        var form = await PublishedAsync();

        var response = await Client().SendAsync(Request(
            $"/admin/forms/{form.Id}/responses?cursor=not-a-cursor", await ReaderAsync()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --------------------------------------------------------------- resumes ---

    [Fact]
    public async Task The_list_never_signs_a_link_and_never_names_a_storage_key()
    {
        // Signing is a round trip each and the links die in five minutes, so a
        // page of fifty would spend fifty calls on URLs nobody reaches. The
        // key is absent for a longer-lived reason: it is a permanent way to
        // name somebody's CV.
        var form = await PublishedAsync();
        var id = await SubmitAsync(form);
        await AttachResumeAsync(id);

        var person = await db.AddPersonAsync(Unique("both"));
        await db.GrantAsync(person, "applications.view_responses");
        await db.GrantAsync(person, "applications.view_resume");

        var response = await Client().SendAsync(
            Request($"/admin/forms/{form.Id}/responses", await SignIn(person)));

        var raw = await response.Content.ReadAsStringAsync();
        var resume = JsonDocument.Parse(raw).RootElement
            .GetProperty("items")[0].GetProperty("resume");

        Assert.Equal("ada-cv.pdf", resume.GetProperty("filename").GetString());
        Assert.Equal(4096, resume.GetProperty("sizeBytes").GetInt32());
        Assert.Equal(JsonValueKind.Null, resume.GetProperty("url").ValueKind);
        Assert.DoesNotContain("storageKey", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("resumes/", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_response_is_where_a_link_gets_signed()
    {
        var form = await PublishedAsync();
        var id = await SubmitAsync(form);
        await AttachResumeAsync(id);

        var person = await db.AddPersonAsync(Unique("both"));
        await db.GrantAsync(person, "applications.view_responses");
        await db.GrantAsync(person, "applications.view_resume");

        var body = await ReadAsync(
            $"/admin/forms/{form.Id}/responses/{id}", await SignIn(person));

        var resume = body.GetProperty("resume");

        Assert.StartsWith(
            "https://blobs.test/", resume.GetProperty("url").GetString()!, StringComparison.Ordinal);

        // The expiry rides along so the screen embedding the file knows when to
        // ask for a fresh one rather than finding out as a broken frame.
        Assert.NotEqual(JsonValueKind.Null, resume.GetProperty("expiresAt").ValueKind);
    }

    [Fact]
    public async Task Responses_alone_do_not_open_a_resume()
    {
        // The permission model calls a resume more sensitive than the rest of a
        // record. A second route that signs one anyway would quietly undo that.
        var form = await PublishedAsync();
        var id = await SubmitAsync(form);
        await AttachResumeAsync(id);

        var body = await ReadAsync(
            $"/admin/forms/{form.Id}/responses/{id}", await ReaderAsync());

        var resume = body.GetProperty("resume");

        Assert.Equal("ada-cv.pdf", resume.GetProperty("filename").GetString());
        Assert.Equal(JsonValueKind.Null, resume.GetProperty("url").ValueKind);
    }

    [Fact]
    public async Task A_response_belonging_to_another_form_is_not_there()
    {
        // The form in the URL is the only thing the caller was authorized
        // against, so the event has to be part of the lookup rather than
        // checked afterwards.
        var mine = await PublishedAsync();
        var theirs = await PublishedAsync();
        var id = await SubmitAsync(theirs);

        var response = await Client().SendAsync(
            Request($"/admin/forms/{mine.Id}/responses/{id}", await ReaderAsync()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ------------------------------------------------------------------- csv ---

    [Fact]
    public async Task The_columns_are_the_published_questions_in_the_order_they_are_asked()
    {
        var form = await PublishedAsync(Question("why_apply"), Question("dietary"));
        await SubmitAsync(form, ("why_apply", "To build something."));

        var exporter = await db.AddPersonAsync(Unique("exporter"));
        await db.GrantAsync(exporter, "applications.export");

        var csv = await CsvAsync($"/admin/forms/{form.Id}/responses.csv", await SignIn(exporter));
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var header = lines[0];

        Assert.Contains("\"id\",\"submitted_at\",\"form_version\"", header, StringComparison.Ordinal);
        Assert.Contains("\"why_apply\",\"dietary\"", header, StringComparison.Ordinal);
        Assert.EndsWith(
            "\"resume_filename\",\"resume_size\",\"other_answers\"", header, StringComparison.Ordinal);

        // A question nobody answered is still a column, and its cell is empty.
        // An export whose shape depends on its contents is one where two runs
        // cannot be compared.
        var cells = lines[1].Split("\",\"");
        Assert.Equal(header.Split("\",\"").Length, cells.Length);
        Assert.Contains("\"\"", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cell_that_would_run_in_a_spreadsheet_is_defused()
    {
        // Applicants control every answer cell in this file, and Excel runs a
        // cell starting = + - or @ the moment the file is opened. This is a
        // real attack on the organizer who opens the export, not a theoretical
        // one.
        var form = await PublishedAsync(Question("why_apply"));
        await SubmitAsync(form, ("why_apply", "=HYPERLINK(\"http://evil.example\",\"click\")"));

        var exporter = await db.AddPersonAsync(Unique("exporter"));
        await db.GrantAsync(exporter, "applications.export");

        var csv = await CsvAsync($"/admin/forms/{form.Id}/responses.csv", await SignIn(exporter));

        Assert.Contains("\"'=HYPERLINK", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("\"=HYPERLINK", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_leading_plus_is_defused_too_and_the_quotes_are_doubled()
    {
        // The phone number is the everyday case for the plus. The quote is the
        // ordinary CSV rule underneath it, which has to still hold once
        // something has been prefixed.
        var form = await PublishedAsync(Question("why_apply"));
        await SubmitAsync(form, ("why_apply", "+1 said \"hello\""));

        var exporter = await db.AddPersonAsync(Unique("exporter"));
        await db.GrantAsync(exporter, "applications.export");

        var csv = await CsvAsync($"/admin/forms/{form.Id}/responses.csv", await SignIn(exporter));

        Assert.Contains("\"'+1 said \"\"hello\"\"\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_file_arrives_as_a_spreadsheet_named_after_the_form()
    {
        var form = await PublishedAsync();
        var exporter = await db.AddPersonAsync(Unique("exporter"));
        await db.GrantAsync(exporter, "applications.export");

        var response = await Client().SendAsync(
            Request($"/admin/forms/{form.Id}/responses.csv", await SignIn(exporter)));

        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(
            $"responses-{form.Code}.csv",
            response.Content.Headers.ContentDisposition?.ToString() ?? string.Empty,
            StringComparison.Ordinal);
    }

    // -------------------------------------------------- a form that changed ---

    [Fact]
    public async Task A_form_edited_between_two_submissions_still_lines_both_up()
    {
        // The failure this prevents: an answer given against version one
        // appearing under a question that only exists in version two, because
        // the reader used the current form for every row.
        var form = await PublishedAsync(Question("first_project", "Your first project"));
        var early = await SubmitAsync(form, ("first_project", "A calculator."));

        await RepublishAsync(form.Id, Question("proudest_project", "Your proudest project"));
        var late = await SubmitAsync(form, ("proudest_project", "A compiler."));

        var cookie = await ReaderAsync();

        var older = await ReadAsync($"/admin/forms/{form.Id}/responses/{early}", cookie);
        var newer = await ReadAsync($"/admin/forms/{form.Id}/responses/{late}", cookie);

        Assert.Equal(
            "A calculator.",
            older.GetProperty("answers").GetProperty("first_project").GetString());

        Assert.Equal(
            "A compiler.",
            newer.GetProperty("answers").GetProperty("proudest_project").GetString());

        // And each says which questions it was given, so a reviewer reading an
        // answer can tell whether they are looking at the current form.
        Assert.True(
            older.GetProperty("formVersion").GetInt32()
            < newer.GetProperty("formVersion").GetInt32());
    }

    [Fact]
    public async Task An_answer_to_a_retired_question_does_not_vanish_from_the_export()
    {
        // Silent data loss in the one artefact people treat as the record. The
        // published form decides the columns, so an answer whose question has
        // gone has nowhere to sit but the trailing cell.
        var form = await PublishedAsync(Question("first_project", "Your first project"));
        await SubmitAsync(form, ("first_project", "A calculator."));

        // Republishing without the old question. The draft starts from what is
        // published, so it has to be taken back out rather than left off.
        var draft = await Forms.DraftAsync(form.Id, null);
        await Forms.SaveDraftAsync(
            form.Id, [.. draft.Fields.Where(f => f.Key != "first_project")]);
        await Forms.PublishAsync(form.Id, null);

        var exporter = await db.AddPersonAsync(Unique("exporter"));
        await db.GrantAsync(exporter, "applications.export");

        var csv = await CsvAsync($"/admin/forms/{form.Id}/responses.csv", await SignIn(exporter));

        Assert.DoesNotContain("\"first_project\",", csv, StringComparison.Ordinal);
        Assert.Contains("first_project", csv, StringComparison.Ordinal);
        Assert.Contains("A calculator.", csv, StringComparison.Ordinal);
    }


    // -------------------------------------------------------- page breaks ---

    [Fact]
    public async Task A_page_break_is_not_a_column_in_the_export()
    {
        // Nobody answered it, so a column for it would be empty in every row of
        // every export, under a heading that was never a question. The two
        // questions either side of it still get theirs.
        var form = await PublishedAsync(
            Question("why_apply"), PageBreak("section_about"), Question("dietary"));

        await SubmitAsync(form, ("why_apply", "To build something."));

        var exporter = await db.AddPersonAsync(Unique("exporter"));
        await db.GrantAsync(exporter, "applications.export");

        var csv = await CsvAsync($"/admin/forms/{form.Id}/responses.csv", await SignIn(exporter));
        var header = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[0];

        Assert.DoesNotContain("section_about", header, StringComparison.Ordinal);
        Assert.Contains("\"why_apply\",\"dietary\"", header, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_break_is_not_an_unanswered_question_on_a_response()
    {
        // The reader's screen walks the form's fields and shows every one
        // without an answer as "Not answered". A section arriving with the
        // answers absent is indistinguishable from a question nobody filled in,
        // so it must not be in the answer set at all — and it is not, because
        // nothing was ever stored under its key.
        var form = await PublishedAsync(PageBreak("section_about"), Question("why_apply"));
        await SubmitAsync(form, ("why_apply", "To build something."));

        var body = await ReadAsync($"/admin/forms/{form.Id}/responses", await ReaderAsync());
        var answers = body.GetProperty("items")[0].GetProperty("answers");

        Assert.False(answers.TryGetProperty("section_about", out _));
        Assert.Equal("To build something.", answers.GetProperty("why_apply").GetString());
    }

    [Fact]
    public async Task An_answer_given_before_a_question_became_a_page_break_is_not_lost()
    {
        // Turning a question into a section keeps its key, so the answers
        // already given are still filed under it. They stop being a column and
        // become leftovers, which is the whole reason other_answers exists —
        // the alternative is silently dropping what somebody wrote from the one
        // artefact people treat as the record.
        var form = await PublishedAsync(Question("why_apply"));
        await SubmitAsync(form, ("why_apply", "To build something."));

        var draft = await Forms.DraftAsync(form.Id, null);
        await Forms.SaveDraftAsync(
            form.Id,
            [.. draft.Fields.Where(f => f.Key != "why_apply"),
             PageBreak("why_apply", "Page two"),
             Question("dietary")]);
        await Forms.PublishAsync(form.Id, null);

        var exporter = await db.AddPersonAsync(Unique("exporter"));
        await db.GrantAsync(exporter, "applications.export");

        var csv = await CsvAsync($"/admin/forms/{form.Id}/responses.csv", await SignIn(exporter));

        Assert.Contains("To build something.", csv, StringComparison.Ordinal);
    }
    /// <summary>
    /// An object store that signs anything and stores nothing.
    /// </summary>
    /// <remarks>
    /// The deployed one needs a storage account, and the branch that matters
    /// here is not how a signature is built — that is
    /// <see cref="DelegatedResumeLinkTests"/>'s job — but whether a link is
    /// minted at all, and for whom.
    /// </remarks>
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
