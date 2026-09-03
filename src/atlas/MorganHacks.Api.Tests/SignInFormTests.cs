using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Applications.Forms;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api.Tests;

/// <summary>
/// Forms that are for people we already have on file.
/// </summary>
/// <remarks>
/// Everything here is about what the endpoints refuse and what they decline to
/// say. Three rules carry the feature and each has a test whose failure would
/// be a real incident:
/// <list type="bullet">
/// <item>
/// The application form is never gated. Gating it makes applying impossible,
/// because the account it would demand is the one applying creates.
/// </item>
/// <item>
/// The sign-in step answers identically for an address we hold and one we do
/// not. Any difference turns a link handed out on a flyer into a way to ask
/// who applied.
/// </item>
/// <item>
/// A fixed answer comes from the record and not from the request. The page
/// renders those questions without controls, and a page is a suggestion.
/// </item>
/// </list>
/// <para>
/// Against a real database rather than a mock, because most of these are
/// questions about what the SQL and the constraints actually do — a mock would
/// answer whatever the test told it to.
/// </para>
/// </remarks>
public class SignInFormTests(ApplicationsDatabase db)
    : IClassFixture<ApplicationsDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;

    public Task InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString));
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

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@morgan.edu";

    // ------------------------------------------------------------ fixtures ---

    /// <summary>The three questions every form here asks, and nothing else.</summary>
    /// <remarks>
    /// Deliberately small and deliberately mixed: one identity question that
    /// must come from the record, one agreement that must not be re-asked, and
    /// one ordinary question that is theirs to answer. Every rule under test
    /// is about which of the three a submission is allowed to set.
    /// </remarks>
    private static readonly FormField[] Rsvp =
    [
        new()
        {
            Key = "email",
            Type = FieldType.Email,
            Label = "Email",
            Required = true,
            Storage = AnswerStorage.Column,
            Column = "email",
        },
        new()
        {
            Key = "mlh_coc_agreed_at",
            Type = FieldType.Consent,
            Label = "I have read and agree to the MLH Code of Conduct.",
            Required = true,
            Storage = AnswerStorage.Column,
            Column = "mlh_coc_agreed_at",
        },
        new()
        {
            Key = "shirt_size",
            Type = FieldType.ShortText,
            Label = "Shirt size",
            Storage = AnswerStorage.Column,
            Column = "shirt_size",
        },
    ];

    private async Task<(Form Form, Guid EventId)> GatedFormAsync(
        params string[] statuses)
    {
        var eventId = await db.AddEventAsync();
        var form = await Forms.CreateAsync(eventId, "RSVP", "survey", null);

        await Forms.DraftAsync(form.Id, null);
        await Forms.SaveDraftAsync(form.Id, Rsvp);
        await Forms.PublishAsync(form.Id, null);

        var saved = await Forms.SaveAudienceAsync(
            form.Id, requiresSignIn: true, statuses.Length == 0 ? ["accepted"] : statuses);

        return (saved!, eventId);
    }

    /// <summary>An applicant on file, with or without an account.</summary>
    private async Task<Guid> AddApplicationAsync(
        Guid eventId,
        string email,
        string status,
        Guid? personId = null,
        string? shirtSize = null,
        bool agreed = true)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO applications.applications
                (event_id, person_id, email, first_name, last_name, age, phone,
                 school, level_of_study, country, shirt_size,
                 mlh_coc_agreed_at, mlh_data_sharing_at, responses)
            VALUES (@eventId, @personId, @email, 'Ada', 'Lovelace', 20, '+15550000000',
                    'Morgan State University', 'undergraduate-3y', 'United States',
                    @shirtSize,
                    CASE WHEN @agreed THEN now() END, now(),
                    '{"favourite_track": "hardware"}'::jsonb)
            RETURNING id
            """);

        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("personId", (object?)personId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.AddWithValue("shirtSize", (object?)shirtSize ?? DBNull.Value);
        cmd.Parameters.AddWithValue("agreed", agreed);

        var id = (Guid)(await cmd.ExecuteScalarAsync())!;

        // A row starts incomplete, which is what it already is.
        if (status == "incomplete")
        {
            return id;
        }

        // Moved rather than inserted at the target status, so the lifecycle
        // triggers stamp submitted_at exactly as a real submission would.
        await using var move = db.DataSource.CreateCommand(
            "UPDATE applications.applications SET status = 'submitted' WHERE id = @id");
        move.Parameters.AddWithValue("id", id);
        await move.ExecuteNonQueryAsync();

        if (status != "submitted")
        {
            await using var then = db.DataSource.CreateCommand(
                "UPDATE applications.applications SET status = @status WHERE id = @id");
            then.Parameters.AddWithValue("id", id);
            then.Parameters.AddWithValue("status", status);
            await then.ExecuteNonQueryAsync();
        }

        return id;
    }

    private async Task<Guid> AddHackerAsync(string email)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "INSERT INTO identity.people (kind, email) VALUES ('hacker', @e) RETURNING id");
        cmd.Parameters.AddWithValue("e", email);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<string> SignInAsync(Guid personId)
    {
        using var scope = _app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return $"mh_session={await sessions.StartAsync(personId)}";
    }

    private async Task<JsonElement> ReadAsync(string code, string? cookie = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/forms/{code}");
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        var response = await Client().SendAsync(request);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HttpResponseMessage> SubmitAsync(
        string code, object answers, string? cookie = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/forms/{code}/submit")
        {
            Content = JsonContent.Create(new { answers }),
        };

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        return await Client().SendAsync(request);
    }

    // ------------------------------------- the application form is never it ---

    [Fact]
    public async Task The_application_form_cannot_be_gated_at_the_database()
    {
        // The rule that must never bend, checked where it cannot be argued
        // with. Gating this form makes applying impossible, because the
        // account it would demand is created by applying.
        var eventId = await db.AddEventAsync();
        var form = await Forms.CreateAsync(eventId, "Application", "application", null);

        var refused = await Assert.ThrowsAsync<Npgsql.PostgresException>(
            () => Forms.SaveAudienceAsync(form.Id, true, ["accepted"]));

        Assert.Equal("the_application_form_is_never_gated", refused.ConstraintName);
    }

    [Fact]
    public async Task The_application_form_stays_open_to_anybody_with_the_link()
    {
        var eventId = await db.AddEventAsync();
        var form = await Forms.CreateAsync(eventId, "Application", "application", null);
        await Forms.DraftAsync(form.Id, null);
        await Forms.PublishAsync(form.Id, null);

        // No cookie, no session, nobody.
        var body = await ReadAsync(form.Code);

        Assert.Equal("open", body.GetProperty("access").GetString());
        Assert.False(body.GetProperty("requiresSignIn").GetBoolean());
        Assert.NotEmpty(body.GetProperty("fields").EnumerateArray());
    }

    // ------------------------------------------------- who may open a form ---

    [Fact]
    public async Task A_reader_with_no_session_is_asked_to_sign_in()
    {
        var (form, _) = await GatedFormAsync();

        var body = await ReadAsync(form.Code);

        Assert.Equal("signIn", body.GetProperty("access").GetString());

        // No questions with it. An unanswerable form rendered behind a banner
        // is one somebody scrolls past.
        Assert.False(body.TryGetProperty("fields", out _));
    }

    [Fact]
    public async Task An_ineligible_status_is_refused_and_told_apart_from_signed_out()
    {
        // The distinction the whole read is built around: one of these has
        // something to do and the other has nothing.
        var (form, eventId) = await GatedFormAsync("accepted");

        var email = Unique("waitlisted");
        var personId = await AddHackerAsync(email);
        await AddApplicationAsync(eventId, email, "waitlisted", personId);

        var body = await ReadAsync(form.Code, await SignInAsync(personId));

        Assert.Equal("ineligible", body.GetProperty("access").GetString());
        Assert.False(body.TryGetProperty("fields", out _));

        // And nothing about which status they are in, or which the form wants.
        Assert.DoesNotContain("waitlisted", body.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("accepted", body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_ineligible_status_cannot_submit_either()
    {
        // Checked again on the write, because the read and the write are two
        // requests and a decision can land between them.
        var (form, eventId) = await GatedFormAsync("accepted");

        var email = Unique("withdrawn");
        var personId = await AddHackerAsync(email);
        await AddApplicationAsync(eventId, email, "withdrawn", personId);

        var response = await SubmitAsync(
            form.Code, new { shirt_size = "m" }, await SignInAsync(personId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await SubmissionCountAsync(form.Id));
    }

    [Fact]
    public async Task Somebody_signed_in_with_no_application_here_is_not_a_respondent()
    {
        // A person with an account and no application on this event is asked
        // to sign in rather than told they are ineligible — from out here the
        // two are one state, and telling them apart would answer "does this
        // address have an application" for anybody who could get a session.
        var (form, _) = await GatedFormAsync();
        var stranger = await AddHackerAsync(Unique("stranger"));

        var body = await ReadAsync(form.Code, await SignInAsync(stranger));

        Assert.Equal("signIn", body.GetProperty("access").GetString());
    }

    [Fact]
    public async Task Submitting_without_a_session_is_refused()
    {
        var (form, _) = await GatedFormAsync();

        var response = await SubmitAsync(form.Code, new { shirt_size = "m" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -------------------------------------------------- the sign-in itself ---

    [Fact]
    public async Task An_unknown_address_is_answered_exactly_like_a_known_one()
    {
        // The single most important behaviour here, and the same one
        // /auth/magic-link is built around. A difference in status or body
        // turns a form's sign-in box into a lookup service for who applied.
        var (form, eventId) = await GatedFormAsync();

        var known = Unique("known");
        await AddApplicationAsync(eventId, known, "accepted");

        var a = await Client().PostAsJsonAsync(
            $"/forms/{form.Code}/sign-in", new { email = known });
        var b = await Client().PostAsJsonAsync(
            $"/forms/{form.Code}/sign-in", new { email = Unique("nobody") });

        Assert.Equal(HttpStatusCode.Accepted, a.StatusCode);
        Assert.Equal(a.StatusCode, b.StatusCode);
        Assert.Equal(
            await a.Content.ReadAsStringAsync(), await b.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_applicant_who_never_opened_the_portal_gets_an_account()
    {
        // Applying does not create an account, so most applicants are in
        // applications.applications and not in identity.people. Without this
        // the form would refuse everybody it was built for.
        var (form, eventId) = await GatedFormAsync();

        var email = Unique("never-signed-in");
        var applicationId = await AddApplicationAsync(eventId, email, "accepted");

        var response = await Client().PostAsJsonAsync(
            $"/forms/{form.Code}/sign-in", new { email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        await using var cmd = db.DataSource.CreateCommand("""
            SELECT p.id FROM identity.people p
              JOIN applications.applications a ON a.person_id = p.id
             WHERE a.id = @id AND p.kind = 'hacker'
            """);
        cmd.Parameters.AddWithValue("id", applicationId);

        Assert.NotNull(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task An_address_that_is_not_on_file_gets_no_account()
    {
        // The other half of the rule above. This endpoint creates a row for an
        // address it is handed, so what stops it being a way for a stranger to
        // fill identity.people is entirely the check in front of it.
        var (form, _) = await GatedFormAsync();
        var stranger = Unique("stranger");

        await Client().PostAsJsonAsync($"/forms/{form.Code}/sign-in", new { email = stranger });

        await using var cmd = db.DataSource.CreateCommand(
            "SELECT count(*) FROM identity.people WHERE lower(email) = lower(@e)");
        cmd.Parameters.AddWithValue("e", stranger);

        Assert.Equal(0L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task An_applicant_who_already_has_an_account_keeps_the_one_they_have()
    {
        // The conflict half of the upsert. Two sign-in requests for one
        // address arriving together is the ordinary case — a double-tapped
        // button on a slow connection — and the second must find the row
        // rather than fail on the unique index.
        var (form, eventId) = await GatedFormAsync();

        var email = Unique("has-account");
        var personId = await AddHackerAsync(email);
        await AddApplicationAsync(eventId, email, "accepted", personId);

        var response = await Client().PostAsJsonAsync(
            $"/forms/{form.Code}/sign-in", new { email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        await using var cmd = db.DataSource.CreateCommand(
            "SELECT count(*) FROM identity.people WHERE lower(email) = lower(@e)");
        cmd.Parameters.AddWithValue("e", email);

        Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task An_organizers_address_never_becomes_a_hacker_account()
    {
        // Organizers sign in through Google so their access is tied to an
        // allowlisted account and a bound subject id. A hacker link would be a
        // second way in that skips all of it, reachable by anybody who can
        // read their inbox.
        var (form, eventId) = await GatedFormAsync();

        var email = Unique("organizer");
        await db.AddPersonAsync(email);
        await AddApplicationAsync(eventId, email, "accepted");

        var response = await Client().PostAsJsonAsync(
            $"/forms/{form.Code}/sign-in", new { email });

        // Answered like every other address, and mailed nothing.
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        await using var cmd = db.DataSource.CreateCommand(
            "SELECT kind FROM identity.people WHERE lower(email) = lower(@e)");
        cmd.Parameters.AddWithValue("e", email);

        Assert.Equal("organizer", (string)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task A_form_that_does_not_use_sign_in_has_no_sign_in_step()
    {
        var eventId = await db.AddEventAsync();
        var form = await Forms.CreateAsync(eventId, "Application", "application", null);
        await Forms.DraftAsync(form.Id, null);
        await Forms.PublishAsync(form.Id, null);

        var response = await Client().PostAsJsonAsync(
            $"/forms/{form.Code}/sign-in", new { email = Unique("anyone") });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------- prefill ---

    [Fact]
    public async Task Prefill_carries_what_they_told_us_and_only_what_is_asked()
    {
        var (form, eventId) = await GatedFormAsync();

        var email = Unique("eligible");
        var personId = await AddHackerAsync(email);
        await AddApplicationAsync(eventId, email, "accepted", personId, shirtSize: "l");

        var body = await ReadAsync(form.Code, await SignInAsync(personId));
        var prefill = body.GetProperty("prefill");

        Assert.Equal("l", prefill.GetProperty("shirt_size").GetString());
        Assert.Equal(email, prefill.GetProperty("email").GetString());
        Assert.True(prefill.GetProperty("mlh_coc_agreed_at").GetBoolean());

        // Everything else they have told us stays where it is. The form asks
        // three questions, so three is what it gets — sending the rest would
        // hand the page somebody's whole record because it asked one.
        Assert.Equal(3, prefill.EnumerateObject().Count());
        Assert.False(prefill.TryGetProperty("favourite_track", out _));
        Assert.False(prefill.TryGetProperty("school", out _));

        // And the identity comes from the record rather than from a question.
        Assert.Equal(email, body.GetProperty("you").GetProperty("email").GetString());
        Assert.Equal("Ada Lovelace", body.GetProperty("you").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Identity_and_agreements_already_given_are_fixed()
    {
        var (form, eventId) = await GatedFormAsync();

        var email = Unique("eligible");
        var personId = await AddHackerAsync(email);
        await AddApplicationAsync(eventId, email, "accepted", personId);

        var body = await ReadAsync(form.Code, await SignInAsync(personId));

        var locked = body.GetProperty("fixed")
            .EnumerateArray().Select(k => k.GetString()).ToList();

        Assert.Contains("email", locked);
        Assert.Contains("mlh_coc_agreed_at", locked);

        // An ordinary question is theirs. Fixing everything we happen to hold
        // would make the form unanswerable, which is the failure the other way
        // round.
        Assert.DoesNotContain("shirt_size", locked);

        // Shown as fixed rather than hidden: the question is still on the
        // form, it simply has no control.
        Assert.Contains(
            body.GetProperty("fields").EnumerateArray(),
            f => f.GetProperty("key").GetString() == "email");
    }

    [Fact]
    public async Task An_agreement_not_yet_given_is_still_theirs_to_give()
    {
        // An agreement is fixed once it has been given, not before. Somebody
        // eligible on an application that never carried one must be able to
        // give it rather than being frozen out of a box nobody ticked.
        var (form, eventId) = await GatedFormAsync("incomplete");

        var email = Unique("never-agreed");
        var personId = await AddHackerAsync(email);
        await AddApplicationAsync(eventId, email, "incomplete", personId, agreed: false);

        var body = await ReadAsync(form.Code, await SignInAsync(personId));

        var locked = body.GetProperty("fixed")
            .EnumerateArray().Select(k => k.GetString()).ToList();

        Assert.DoesNotContain("mlh_coc_agreed_at", locked);
        Assert.Contains("email", locked);
    }

    // ------------------------------------------------------- what is stored ---

    [Fact]
    public async Task A_crafted_submission_cannot_overwrite_a_fixed_answer()
    {
        // The page renders these without controls, and a page is a suggestion:
        // anybody can post whatever dictionary they like to this endpoint.
        var (form, eventId) = await GatedFormAsync();

        var email = Unique("eligible");
        var personId = await AddHackerAsync(email);
        await AddApplicationAsync(eventId, email, "accepted", personId);

        var response = await SubmitAsync(
            form.Code,
            new
            {
                email = "someone.else@example.com",
                mlh_coc_agreed_at = false,
                shirt_size = "xl",
            },
            await SignInAsync(personId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await AnswersAsync(form.Id, personId);

        // Their own address, from the record. Not the one they sent, which is
        // the whole reason this form asked them to sign in.
        Assert.Equal(email, stored.GetProperty("email").GetString());

        // And the agreement they already gave, not the withdrawal they posted.
        Assert.True(stored.GetProperty("mlh_coc_agreed_at").GetBoolean());

        // The question that was theirs is theirs.
        Assert.Equal("xl", stored.GetProperty("shirt_size").GetString());
    }

    [Fact]
    public async Task An_answer_is_filed_against_the_person_and_replaced_when_they_change_it()
    {
        var (form, eventId) = await GatedFormAsync();

        var email = Unique("eligible");
        var personId = await AddHackerAsync(email);
        var applicationId = await AddApplicationAsync(eventId, email, "accepted", personId);
        var cookie = await SignInAsync(personId);

        await SubmitAsync(form.Code, new { shirt_size = "s" }, cookie);
        await SubmitAsync(form.Code, new { shirt_size = "m" }, cookie);

        // One row, not two. "Are you coming" has one current answer, and two
        // rows would mean every reader deciding which of them counts.
        Assert.Equal(1, await SubmissionCountAsync(form.Id));

        var stored = await AnswersAsync(form.Id, personId);
        Assert.Equal("m", stored.GetProperty("shirt_size").GetString());

        await using var cmd = db.DataSource.CreateCommand(
            "SELECT application_id FROM applications.form_submissions "
            + "WHERE form_id = @form AND person_id = @person");
        cmd.Parameters.AddWithValue("form", form.Id);
        cmd.Parameters.AddWithValue("person", personId);

        Assert.Equal(applicationId, (Guid)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task A_second_answer_is_prefilled_from_the_first()
    {
        var (form, eventId) = await GatedFormAsync();

        var email = Unique("eligible");
        var personId = await AddHackerAsync(email);
        await AddApplicationAsync(eventId, email, "accepted", personId, shirtSize: "l");
        var cookie = await SignInAsync(personId);

        await SubmitAsync(form.Code, new { shirt_size = "s" }, cookie);

        var body = await ReadAsync(form.Code, cookie);

        // What they said last time, not what their application said a month
        // earlier. Somebody reopening an RSVP is looking at their own answer.
        Assert.Equal(
            "s", body.GetProperty("prefill").GetProperty("shirt_size").GetString());
    }

    [Fact]
    public async Task A_gated_form_must_name_an_audience()
    {
        // An empty list has two readings — nobody, or everybody — and the
        // wrong one gets chosen silently on the form that decides catering.
        var eventId = await db.AddEventAsync();
        var form = await Forms.CreateAsync(eventId, "RSVP", "survey", null);

        var refused = await Assert.ThrowsAsync<Npgsql.PostgresException>(
            () => Forms.SaveAudienceAsync(form.Id, true, []));

        Assert.Equal("a_gated_form_names_its_audience", refused.ConstraintName);
    }

    // ------------------------------------------------------------- reading ---

    private async Task<int> SubmissionCountAsync(Guid formId)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT count(*) FROM applications.form_submissions WHERE form_id = @form");
        cmd.Parameters.AddWithValue("form", formId);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<JsonElement> AnswersAsync(Guid formId, Guid personId)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT answers FROM applications.form_submissions "
            + "WHERE form_id = @form AND person_id = @person");
        cmd.Parameters.AddWithValue("form", formId);
        cmd.Parameters.AddWithValue("person", personId);

        return JsonDocument.Parse((string)(await cmd.ExecuteScalarAsync())!).RootElement;
    }
}
