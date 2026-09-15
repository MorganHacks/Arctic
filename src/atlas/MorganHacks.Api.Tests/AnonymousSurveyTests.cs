using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Applications.Forms;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api.Tests;

/// <summary>
/// A survey nobody has to sign in to answer.
/// </summary>
/// <remarks>
/// The path that used to answer 501 and lose the answers. Everything here is
/// about the three things that are genuinely different once there is no person
/// behind a submission, and each has a failure that is a real incident:
/// <list type="bullet">
/// <item>
/// The answer is kept, and it is readable beside the signed-in answers to the
/// same form. A responses screen that silently shows half a survey is worse
/// than one that shows none, because nothing on it says a half is missing.
/// </item>
/// <item>
/// A second submission is a second response, and a retry of one submission is
/// not. Guessing either way round loses somebody's words — and on an anonymous
/// survey the words are the whole product.
/// </item>
/// <item>
/// The endpoint is unauthenticated and now writes a row per request, with no
/// unique index doing the real work the way the application form has one. What
/// stops a script is the only thing here that is not visible on a screen, so it
/// is the thing most worth a test.
/// </item>
/// </list>
/// <para>
/// Against a real database, because most of these are questions about what the
/// partial indexes and the check constraints in 0027 actually do, and a mock
/// would answer whatever the test told it to.
/// </para>
/// </remarks>
public class AnonymousSurveyTests(ApplicationsDatabase db)
    : IClassFixture<ApplicationsDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;

    public Task InitializeAsync()
    {
        _app = Factory();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// An app pointed at the shared database.
    /// </summary>
    /// <param name="perHour">
    /// The anonymous submission cap, when a test needs a smaller one than the
    /// hundred a deployment runs with. A test that had to make a hundred and
    /// one real submissions to see the limiter would be slow enough that
    /// somebody would eventually delete it, and it would also spend the
    /// endpoint's own per-caller budget on the way there.
    /// </param>
    /// <param name="proxySecret">
    /// The shared secret a front end presents, when a test is about whether a
    /// forwarded caller address is believed.
    /// </param>
    private WebApplicationFactory<Program> Factory(
        int? perHour = null, string? proxySecret = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);

            if (perHour is { } limit)
            {
                b.UseSetting(
                    "Forms:AnonymousSubmissionsPerHour",
                    limit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (proxySecret is not null)
            {
                b.UseSetting("Network:ProxySecret", proxySecret);
            }
        });

    private PostgresFormStore Forms => new(db.DataSource);

    private HttpClient Client(WebApplicationFactory<Program>? app = null) =>
        (app ?? _app).CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@morgan.edu";

    // ------------------------------------------------------------ fixtures ---

    /// <summary>
    /// A published survey anybody with the link can answer.
    /// </summary>
    /// <remarks>
    /// One required question and one optional one with a fixed set of choices,
    /// because the validation tests below need both a "you left it blank" and a
    /// "that is not on the form" to have something to be about.
    /// </remarks>
    private async Task<Form> SurveyAsync()
    {
        var form = await Forms.CreateAsync(
            await db.AddEventAsync(), "Feedback", "survey", null);

        await Forms.DraftAsync(form.Id, null);
        await Forms.SaveDraftAsync(form.Id, [
            new FormField
            {
                Key = "pizza",
                Type = FieldType.ShortText,
                Label = "Which pizza?",
                Required = true,
            },
            new FormField
            {
                Key = "again",
                Type = FieldType.Radio,
                Label = "Would you come again?",
                Options = [new FieldOption("yes", "Yes"), new FieldOption("no", "No")],
            },
        ]);

        await Forms.PublishAsync(form.Id, null);

        return form;
    }

    /// <param name="from">
    /// The caller's own address, as a front end would forward it, together with
    /// the secret that makes it believable.
    /// </param>
    private async Task<HttpResponseMessage> SubmitAsync(
        string code,
        object answers,
        string? submissionKey = null,
        WebApplicationFactory<Program>? app = null,
        string? from = null,
        string? proxySecret = null)
    {
        object body = submissionKey is null
            ? new { answers }
            : new { answers, submissionKey };

        var request = new HttpRequestMessage(HttpMethod.Post, $"/forms/{code}/submit")
        {
            Content = JsonContent.Create(body),
        };

        if (from is not null)
        {
            request.Headers.Add("X-Real-IP", from);
        }

        if (proxySecret is not null)
        {
            request.Headers.Add("X-MH-Proxy", proxySecret);
        }

        return await Client(app).SendAsync(request);
    }

    private async Task<int> CountAsync(Guid formId, string? extra = null)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT count(*) FROM applications.form_submissions WHERE form_id = @id"
            + (extra is null ? string.Empty : $" AND {extra}"));

        cmd.Parameters.AddWithValue("id", formId);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<string?> AnswerAsync(Guid formId, string key)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT answers->>@key FROM applications.form_submissions WHERE form_id = @id");

        cmd.Parameters.AddWithValue("id", formId);
        cmd.Parameters.AddWithValue("key", key);
        return await cmd.ExecuteScalarAsync() as string;
    }

    /// <summary>
    /// Writes a signed-in answer to the same form, the way the gated submit
    /// does.
    /// </summary>
    /// <remarks>
    /// Through the store the endpoint calls rather than over HTTP, because
    /// getting a real session onto a gated form needs an application, an
    /// account, an audience and a status — all of which SignInFormTests already
    /// covers end to end, and none of which is what this file is about. What
    /// matters here is that a row with a person on it and a row without one end
    /// up in the same list, and this writes the first kind exactly as
    /// production does.
    /// <para>
    /// A form carrying both is not a contrived state either: an organizer who
    /// opens a survey to anybody and gates it halfway through the week has one,
    /// and so does one who goes the other way.
    /// </para>
    /// </remarks>
    private async Task<Guid> SignedInAnswerAsync(Form form, string answer)
    {
        var published = await Forms.PublishedAsync(form.Id);
        var person = await db.AddPersonAsync(Unique("respondent"));

        var respondent = new Respondent(
            person, null, Unique("respondent"), null, null, "accepted",
            AgreedToCodeOfConduct: true, AgreedToDataSharing: true,
            Known: new Dictionary<string, JsonElement>(StringComparer.Ordinal));

        await new PostgresRespondentStore(db.DataSource).RecordAsync(
            form.Id, published!.Version, respondent,
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["pizza"] = JsonSerializer.SerializeToElement(answer),
            });

        return person;
    }

    private async Task<string> SignIn(Guid personId)
    {
        using var scope = _app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return $"mh_session={await sessions.StartAsync(personId)}";
    }

    /// <summary>Somebody who may read responses, and optionally export them.</summary>
    private async Task<string> ReaderAsync(bool export = false)
    {
        var id = await db.AddPersonAsync(Unique("reader"));
        await db.GrantAsync(id, "applications.view_responses");

        if (export)
        {
            await db.GrantAsync(id, "applications.export");
        }

        return await SignIn(id);
    }

    private async Task<JsonElement> ReadAsync(string path, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookie);

        var response = await Client().SendAsync(request);
        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private async Task<string> CsvAsync(Guid formId, string cookie)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get, $"/admin/forms/{formId}/responses.csv");
        request.Headers.Add("Cookie", cookie);

        var response = await Client().SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    // -------------------------------------------------- the answer is kept ---

    [Fact]
    public async Task An_anonymous_answer_is_stored_and_read_back_beside_a_signed_in_one()
    {
        // The whole feature in one test. The submit used to answer 501 and the
        // answers were lost; now it writes a row, and that row has to turn up
        // on the same screen, in the same list, as the answers from people who
        // signed in to give theirs.
        var form = await SurveyAsync();
        await SignedInAnswerAsync(form, "Margherita");

        var submitted = await SubmitAsync(form.Code, new { pizza = "Pepperoni" });
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);

        var page = await ReadAsync(
            $"/admin/forms/{form.Id}/responses", await ReaderAsync());

        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);

        var byAnswer = items.ToDictionary(
            i => i.GetProperty("answers").GetProperty("pizza").GetString()!,
            StringComparer.Ordinal);

        // Both halves, and each carrying which half it is. Without the flag
        // these two rows are indistinguishable on this payload — a gated form
        // gets the respondent from their session and deliberately does not put
        // their address in the answers — so an organizer would be reading a
        // list where some rows can be chased up and some cannot, with nothing
        // saying which.
        Assert.True(byAnswer["Pepperoni"].GetProperty("anonymous").GetBoolean());
        Assert.False(byAnswer["Margherita"].GetProperty("anonymous").GetBoolean());
    }

    [Fact]
    public async Task An_anonymous_answer_can_be_opened_on_its_own_and_still_says_so()
    {
        // The list and the single read are two handlers and two queries. An
        // organizer who clicks into a response must not lose the one fact that
        // says whether there is anybody to go back to.
        var form = await SurveyAsync();
        await SubmitAsync(form.Code, new { pizza = "Hawaiian" });

        var cookie = await ReaderAsync();
        var page = await ReadAsync($"/admin/forms/{form.Id}/responses", cookie);
        var id = page.GetProperty("items").EnumerateArray().First()
                     .GetProperty("id").GetString();

        var one = await ReadAsync($"/admin/forms/{form.Id}/responses/{id}", cookie);

        Assert.Equal("Hawaiian", one.GetProperty("answers").GetProperty("pizza").GetString());
        Assert.True(one.GetProperty("anonymous").GetBoolean());
    }

    [Fact]
    public async Task An_anonymous_answer_exports_as_anonymous_and_a_signed_in_one_does_not()
    {
        // The CSV is the artefact people actually work from, and it is the one
        // organizer-facing surface that says who gave an answer. A file where
        // every row looks the same is one somebody will spend an afternoon
        // trying to match against the applicant list.
        var form = await SurveyAsync();
        await SignedInAnswerAsync(form, "Margherita");
        await SubmitAsync(form.Code, new { pizza = "Pepperoni" });

        var csv = await CsvAsync(form.Id, await ReaderAsync(export: true));
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains(
            "\"id\",\"submitted_at\",\"form_version\",\"anonymous\"",
            lines[0],
            StringComparison.Ordinal);

        var anonymous = lines.Single(l => l.Contains("Pepperoni", StringComparison.Ordinal));
        var signedIn = lines.Single(l => l.Contains("Margherita", StringComparison.Ordinal));

        Assert.Contains("\"yes\"", anonymous, StringComparison.Ordinal);
        Assert.Contains("\"no\"", signedIn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_anonymous_answer_is_filed_against_nobody_and_no_application()
    {
        // The schema, not the handler. A row that carried an application_id
        // with no person on it would read as attribution on a screen whose
        // whole job is to say who answered, and the check constraint is what
        // makes that unreachable however the row is written.
        var form = await SurveyAsync();
        await SubmitAsync(form.Code, new { pizza = "Four cheese" });

        Assert.Equal(1, await CountAsync(form.Id));
        Assert.Equal(1, await CountAsync(form.Id, "person_id IS NULL"));
        Assert.Equal(1, await CountAsync(form.Id, "application_id IS NULL"));
    }

    // ---------------------------------------- what a second submission means ---

    [Fact]
    public async Task Two_people_answering_are_two_responses()
    {
        // The rule. Two submissions carrying different keys are two people,
        // because two people load the page separately, and neither of them is
        // going to be discarded to tidy up a list.
        var form = await SurveyAsync();

        await SubmitAsync(form.Code, new { pizza = "Pepperoni" }, Guid.NewGuid().ToString());
        await SubmitAsync(form.Code, new { pizza = "Margherita" }, Guid.NewGuid().ToString());

        Assert.Equal(2, await CountAsync(form.Id));
    }

    [Fact]
    public async Task Two_identical_answers_with_no_key_are_still_two_responses()
    {
        // The case every content-based deduplication gets wrong. "Would you
        // come again? yes" is what most people answer, and collapsing those
        // rows undercounts the result the survey exists to produce.
        var form = await SurveyAsync();

        await SubmitAsync(form.Code, new { pizza = "Pepperoni", again = "yes" });
        await SubmitAsync(form.Code, new { pizza = "Pepperoni", again = "yes" });

        Assert.Equal(2, await CountAsync(form.Id));
    }

    [Fact]
    public async Task A_retry_of_one_submission_replaces_it_rather_than_adding_one()
    {
        // The duplicate that is not a guess: the same attempt arriving twice
        // because a thumb landed twice or a response never came back. The page
        // sends the key it minted the first time, so this collapses exactly and
        // never on a resemblance.
        var form = await SurveyAsync();
        var key = Guid.NewGuid().ToString();

        await SubmitAsync(form.Code, new { pizza = "Pepperoni" }, key);
        var second = await SubmitAsync(form.Code, new { pizza = "Margherita" }, key);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, await CountAsync(form.Id));

        // The later answer wins. Somebody who corrected a box and pressed
        // Submit again meant the correction, and a retry that kept the first
        // version would be a form that quietly ignored them.
        Assert.Equal("Margherita", await AnswerAsync(form.Id, "pizza"));
    }

    [Fact]
    public async Task One_key_on_two_forms_is_two_responses()
    {
        // Keys are unique per form rather than globally, because they come from
        // the caller. Two surveys open in two tabs must not be able to overwrite
        // one another, whatever the browser sends.
        var mine = await SurveyAsync();
        var theirs = await SurveyAsync();
        var key = Guid.NewGuid().ToString();

        await SubmitAsync(mine.Code, new { pizza = "Mine" }, key);
        await SubmitAsync(theirs.Code, new { pizza = "Theirs" }, key);

        Assert.Equal(1, await CountAsync(mine.Id));
        Assert.Equal(1, await CountAsync(theirs.Id));
        Assert.Equal("Mine", await AnswerAsync(mine.Id, "pizza"));
    }

    [Fact]
    public async Task A_submission_key_that_is_not_one_is_refused()
    {
        // Refused rather than ignored. Dropping a key we cannot read would
        // leave the page believing its retries are being collapsed while every
        // one of them wrote another row — a protection that is off and says
        // nothing, which is the kind that survives for a year.
        var form = await SurveyAsync();

        var response = await SubmitAsync(form.Code, new { pizza = "Pepperoni" }, "not-a-uuid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountAsync(form.Id));
    }

    [Fact]
    public async Task The_signed_in_upsert_still_upserts()
    {
        // 0027 narrowed form_submissions_form_person_key to the rows that have
        // a person, and an arbiter for a partial index has to repeat that
        // index's predicate. Without the matching WHERE clause on the signed-in
        // INSERT, either the statement will not plan or the upsert quietly
        // becomes an insert — and an RSVP somebody changed their mind about
        // would then be two rows with nothing to say which counts.
        var form = await SurveyAsync();
        var published = await Forms.PublishedAsync(form.Id);
        var person = await db.AddPersonAsync(Unique("rsvp"));
        var store = new PostgresRespondentStore(db.DataSource);

        var respondent = new Respondent(
            person, null, Unique("rsvp"), null, null, "accepted",
            AgreedToCodeOfConduct: true, AgreedToDataSharing: true,
            Known: new Dictionary<string, JsonElement>(StringComparer.Ordinal));

        await store.RecordAsync(form.Id, published!.Version, respondent, Answer("Pepperoni"));
        await store.RecordAsync(form.Id, published.Version, respondent, Answer("Margherita"));

        Assert.Equal(1, await CountAsync(form.Id));
        Assert.Equal("Margherita", await AnswerAsync(form.Id, "pizza"));

        static Dictionary<string, JsonElement> Answer(string pizza) =>
            new(StringComparer.Ordinal)
            {
                ["pizza"] = JsonSerializer.SerializeToElement(pizza),
            };
    }

    // ------------------------------------------------------------- throttle ---

    [Fact]
    public async Task Too_many_answers_from_one_caller_to_one_form_are_refused()
    {
        // The only protection here that is not visible on a screen. This
        // endpoint is unauthenticated and writes a row per request, and unlike
        // the application form there is no unique index on an address doing the
        // real work — so without this, a form left open over a weekend is a
        // table anybody can fill.
        using var app = Factory(perHour: 3);
        var form = await SurveyAsync();

        for (var i = 0; i < 3; i++)
        {
            var allowed = await SubmitAsync(form.Code, new { pizza = "Pepperoni" }, app: app);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        var refused = await SubmitAsync(form.Code, new { pizza = "Pepperoni" }, app: app);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);

        // And the refusal is a refusal. A 429 with the row written anyway would
        // be the worst of both: the caller is told to stop and the table fills
        // regardless.
        Assert.Equal(3, await CountAsync(form.Id));
    }

    [Fact]
    public async Task The_throttle_is_per_form_rather_than_per_caller()
    {
        // A cap shared across every ungated form would mean one flooded survey
        // closing every other survey to the same building. The counter is keyed
        // by form for that reason, and this is the test that would fail if
        // somebody dropped the form out of the key to simplify it.
        using var app = Factory(perHour: 1);
        var mine = await SurveyAsync();
        var theirs = await SurveyAsync();

        await SubmitAsync(mine.Code, new { pizza = "Mine" }, app: app);
        var other = await SubmitAsync(theirs.Code, new { pizza = "Theirs" }, app: app);

        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Equal(1, await CountAsync(theirs.Id));
    }

    [Fact]
    public async Task A_refused_submission_still_costs_a_turn()
    {
        // A limiter that only counts the submissions that pass validation is
        // one that can be walked straight past: a script posting rubbish is
        // hammering this endpoint exactly as hard as one posting valid answers.
        using var app = Factory(perHour: 2);
        var form = await SurveyAsync();

        // Refused for being empty, and counted anyway.
        var blank = await SubmitAsync(form.Code, new { }, app: app);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

        await SubmitAsync(form.Code, new { pizza = "Pepperoni" }, app: app);

        var refused = await SubmitAsync(form.Code, new { pizza = "Pepperoni" }, app: app);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }

    [Fact]
    public async Task The_throttle_counts_the_caller_rather_than_the_front_end()
    {
        // Both front ends call the API from their own server, so the connection
        // atlas sees is Vercel and every respondent in the world arrives from
        // the same handful of addresses. Bucketed on that, a cap of a hundred
        // per form would be a hundred for everybody — which refuses real people
        // and stops nobody, because a script does not need a hundred requests
        // from one address to be a problem, it needs them from anywhere.
        using var app = Factory(perHour: 1, proxySecret: "shared");
        var form = await SurveyAsync();

        var first = await SubmitAsync(
            form.Code, new { pizza = "Mine" },
            app: app, from: "203.0.113.1", proxySecret: "shared");

        var second = await SubmitAsync(
            form.Code, new { pizza = "Theirs" },
            app: app, from: "203.0.113.2", proxySecret: "shared");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(2, await CountAsync(form.Id));
    }

    [Fact]
    public async Task A_forwarded_address_with_no_secret_behind_it_buys_nothing()
    {
        // The other half, and the more important one. This endpoint has a
        // public hostname, so a header anybody can set would hand out a fresh
        // bucket per request — the limit would not be weakened, it would be
        // gone. Believed only with the secret the front ends hold, and a
        // request without it is counted on the address the socket reports.
        using var app = Factory(perHour: 1, proxySecret: "shared");
        var form = await SurveyAsync();

        await SubmitAsync(form.Code, new { pizza = "Mine" }, app: app, from: "203.0.113.1");

        var second = await SubmitAsync(
            form.Code, new { pizza = "Theirs" }, app: app, from: "203.0.113.2");

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(1, await CountAsync(form.Id));
    }

    // ------------------------------------------------- the rules still hold ---

    [Fact]
    public async Task A_form_that_requires_sign_in_still_refuses_an_anonymous_answer()
    {
        // The branch that must not have moved. Opening a survey to anybody is a
        // per-form setting, and the new path has to be reachable only from the
        // side of that setting it was written for — otherwise gating a form
        // becomes decoration.
        var form = await SurveyAsync();
        var gated = await Forms.SaveAudienceAsync(form.Id, true, ["accepted"]);

        var response = await SubmitAsync(gated!.Code, new { pizza = "Pepperoni" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await CountAsync(form.Id));
    }

    [Fact]
    public async Task A_gated_form_ignores_a_submission_key_rather_than_taking_one()
    {
        // The person is the key on a form that requires sign-in, and a
        // caller-supplied one would be a second deduplication rule with nothing
        // to say which wins. Sending one changes nothing, including whether the
        // request is refused.
        var form = await SurveyAsync();
        var gated = await Forms.SaveAudienceAsync(form.Id, true, ["accepted"]);

        var response = await SubmitAsync(
            gated!.Code, new { pizza = "Pepperoni" }, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await CountAsync(form.Id));
    }

    [Fact]
    public async Task A_required_question_left_blank_is_still_refused()
    {
        // Validation is the browser's courtesy and this side's rule. An ungated
        // form is the one most likely to be posted to by something that is not
        // the page.
        var form = await SurveyAsync();

        var response = await SubmitAsync(form.Code, new { again = "yes" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var problems = body.GetProperty("problems").EnumerateArray().ToList();

        // Keyed by field, so the page can put the message against the question
        // rather than stacking a sentence at the top.
        Assert.Equal("pizza", problems.Single().GetProperty("field").GetString());
        Assert.Equal(0, await CountAsync(form.Id));
    }

    [Fact]
    public async Task An_answer_that_is_not_on_the_form_is_still_refused()
    {
        // The value is what gets stored and later counted, so an unlisted one
        // does not fail loudly — it turns up months later as a category on a
        // report that nobody put there.
        var form = await SurveyAsync();

        var response = await SubmitAsync(
            form.Code, new { pizza = "Pepperoni", again = "maybe" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountAsync(form.Id));
    }

    [Fact]
    public async Task A_closed_survey_refuses_an_anonymous_answer()
    {
        var form = await SurveyAsync();
        await Forms.SaveScheduleAsync(form.Id, DateTimeOffset.UtcNow.AddMinutes(-1));

        var response = await SubmitAsync(form.Code, new { pizza = "Pepperoni" });

        // 410 rather than 404: the form was here and is not accepting any more,
        // which is the difference between "check your link" and "you missed the
        // deadline".
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal(0, await CountAsync(form.Id));
    }

    [Fact]
    public async Task An_unpublished_survey_is_not_found()
    {
        // A draft is not a form anybody can fill in, and saying "this exists
        // but is not ready" would tell a stranger a form is coming.
        var form = await Forms.CreateAsync(
            await db.AddEventAsync(), "Feedback", "survey", null);

        await Forms.DraftAsync(form.Id, null);

        var response = await SubmitAsync(form.Code, new { pizza = "Pepperoni" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await CountAsync(form.Id));
    }
}
