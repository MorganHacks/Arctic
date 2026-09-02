using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using MorganHacks.Applications.Forms;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The two endpoints the public forms site talks to.
/// </summary>
/// <remarks>
/// Both are unauthenticated and reachable by anybody holding a seven-character
/// code, so what they say when the answer is no matters as much as what they
/// say when it is yes: a 404 that is only a 404 for real codes is a way to
/// find out which codes are real.
/// </remarks>
public class FormEndpointTests(ApplicationsDatabase db)
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

    private async Task<Form> PublishedAsync(
        string kind = "application", DateTimeOffset? closesAt = null)
    {
        var form = await Forms.CreateAsync(await db.AddEventAsync(), "Application", kind, null);
        await Forms.DraftAsync(form.Id, null);
        await Forms.PublishAsync(form.Id, null);

        if (closesAt is not null)
        {
            await using var cmd = db.DataSource.CreateCommand(
                "UPDATE applications.forms SET closes_at = @at WHERE id = @id");
            cmd.Parameters.AddWithValue("at", closesAt.Value);
            cmd.Parameters.AddWithValue("id", form.Id);
            await cmd.ExecuteNonQueryAsync();
        }

        return form;
    }

    /// <summary>
    /// A complete set of answers. A null value takes the question out.
    /// </summary>
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

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@morgan.edu";

    private Task<HttpResponseMessage> Submit(string code, object body) =>
        _app.CreateClient().PostAsJsonAsync($"/forms/{code}/submit", body);

    // -------------------------------------------------------- reading one ---

    [Fact]
    public async Task An_unknown_code_is_simply_not_there()
    {
        var response = await _app.CreateClient().GetAsync("/forms/zzzzzzz");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_form_that_has_only_ever_been_a_draft_is_not_there_either()
    {
        // Same answer as a code nobody issued, on purpose. "This exists but is
        // not ready" tells a stranger a form is coming.
        var form = await Forms.CreateAsync(
            await db.AddEventAsync(), "Application", "application", null);
        await Forms.DraftAsync(form.Id, null);

        var response = await _app.CreateClient().GetAsync($"/forms/{form.Code}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_published_form_comes_back_with_its_questions()
    {
        var form = await PublishedAsync();

        var body = await _app.CreateClient().GetFromJsonAsync<JsonElement>($"/forms/{form.Code}");

        Assert.True(body.GetProperty("open").GetBoolean());
        Assert.Contains(
            body.GetProperty("fields").EnumerateArray(),
            f => f.GetProperty("key").GetString() == "mlh_coc_agreed_at");
    }

    [Fact]
    public async Task The_question_types_are_spelled_the_way_the_stored_form_spells_them()
    {
        // The page switches on this string. If the endpoint and the builder
        // disagree about how to write "shortText", every question of that type
        // renders as nothing at all.
        var form = await PublishedAsync();

        var body = await _app.CreateClient().GetFromJsonAsync<JsonElement>($"/forms/{form.Code}");
        var level = body.GetProperty("fields").EnumerateArray()
            .Single(f => f.GetProperty("key").GetString() == "level_of_study");

        Assert.Equal("select", level.GetProperty("type").GetString());
        Assert.Contains(
            level.GetProperty("options").EnumerateArray(),
            o => o.GetProperty("value").GetString() == "undergraduate-3y");
    }

    [Fact]
    public async Task The_page_is_never_told_where_an_answer_is_stored()
    {
        // Where an answer lives is this side's business. Handing the column
        // names of the applications table to an unauthenticated page is free
        // information for somebody probing it.
        var form = await PublishedAsync();

        var body = await _app.CreateClient().GetStringAsync($"/forms/{form.Code}");

        Assert.DoesNotContain("storage", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"column\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_closed_form_says_it_closed_rather_than_disappearing()
    {
        // Somebody following a link off a flyer in March needs to be told the
        // deadline passed. A 404 reads as a broken link they will report.
        var form = await PublishedAsync(closesAt: DateTimeOffset.UtcNow.AddDays(-1));

        var body = await _app.CreateClient().GetFromJsonAsync<JsonElement>($"/forms/{form.Code}");

        Assert.False(body.GetProperty("open").GetBoolean());

        // And no questions with it, so no page can render the form behind a
        // banner somebody scrolls past.
        Assert.False(body.TryGetProperty("fields", out _));
    }

    [Fact]
    public async Task A_code_typed_off_a_whiteboard_still_resolves()
    {
        // Case and stray spaces are how people transcribe these. Neither is a
        // reason to tell somebody their link is wrong.
        var form = await PublishedAsync();

        var response = await _app.CreateClient()
            .GetAsync($"/forms/{Uri.EscapeDataString($" {form.Code.ToUpperInvariant()} ")}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------------------------------------------------- submitting ---

    [Fact]
    public async Task A_complete_form_is_accepted()
    {
        var form = await PublishedAsync();

        var response = await Submit(form.Code, Answers(Unique("accepted")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_answer_comes_back_naming_the_question()
    {
        // The page puts each message against the box it belongs to. A single
        // sentence at the top makes somebody hunt for which of thirty
        // questions it means.
        var form = await PublishedAsync();

        var response = await Submit(
            form.Code, Answers(Unique("missing"), ("phone", null)));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            body.GetProperty("problems").EnumerateArray(),
            p => p.GetProperty("field").GetString() == "phone");
    }

    [Fact]
    public async Task An_empty_body_is_answered_in_the_endpoints_own_words()
    {
        // Not a 400 from the binder before the handler ever ran. A caller who
        // sent nothing is told which questions are missing, which is the same
        // answer as a caller who filled in nothing.
        var form = await PublishedAsync();

        var response = await _app.CreateClient()
            .PostAsJsonAsync($"/forms/{form.Code}/submit", new { });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(body.GetProperty("problems").EnumerateArray());
    }

    [Fact]
    public async Task A_second_application_from_one_address_is_a_conflict_not_a_duplicate()
    {
        // The unique index surfaced as a sentence. This is somebody applying
        // twice rather than an attack, so the message has to leave them
        // somewhere to go.
        var form = await PublishedAsync();
        var email = Unique("twice");

        var first = await Submit(form.Code, Answers(email));
        var second = await Submit(form.Code, Answers(email));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Submitting_to_a_closed_form_is_refused()
    {
        // The form still resolves, so the page renders. Nothing stops a caller
        // posting to it anyway, which is why the deadline is enforced here and
        // not only in what the page chooses to show.
        var form = await PublishedAsync(closesAt: DateTimeOffset.UtcNow.AddDays(-1));

        var response = await Submit(form.Code, Answers(Unique("late")));

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task A_survey_is_told_it_has_nowhere_to_put_an_answer()
    {
        // Answering 200 and dropping the answers would be the worst option:
        // somebody would believe they had replied.
        var form = await PublishedAsync(kind: "survey");

        var response = await Submit(form.Code, Answers(Unique("survey")));

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    [Fact]
    public async Task An_answer_to_a_question_the_form_does_not_ask_cannot_decide_anything()
    {
        // The questions are loaded from the published version, never read from
        // the request. Without that, a key named after a column is a way to
        // accept your own application.
        var form = await PublishedAsync();
        var email = Unique("forged");

        var response = await Submit(
            form.Code, Answers(email, ("status", "accepted"), ("form_version", 99)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var cmd = db.DataSource.CreateCommand(
            "SELECT status FROM applications.applications WHERE lower(email) = lower(@e)");
        cmd.Parameters.AddWithValue("e", email);

        Assert.Equal("submitted", (string?)await cmd.ExecuteScalarAsync());
    }
}
