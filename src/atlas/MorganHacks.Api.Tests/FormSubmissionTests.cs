using System.Text.Json;
using MorganHacks.Applications.Forms;

namespace MorganHacks.Api.Tests;

/// <summary>
/// What happens to a completed form, against a real database.
/// </summary>
/// <remarks>
/// Two things are being protected here. One is that an answer ends up
/// somewhere it can be read again — the right column, or <c>responses</c>, and
/// never quietly nowhere. The other is that the rules hold against a caller
/// who did not use the page: the endpoint is unauthenticated and open to the
/// internet, so every rule that only exists in the browser is decoration.
/// </remarks>
public class FormSubmissionTests(ApplicationsDatabase db) : IClassFixture<ApplicationsDatabase>
{
    private PostgresFormStore Forms => new(db.DataSource);
    private PostgresSubmissionStore Submissions => new(db.DataSource);

    /// <summary>A live application form on an event of its own.</summary>
    /// <remarks>
    /// An event each, because the dedupe rule is scoped to one — sharing an
    /// event would make every test after the first fail on the address the
    /// one before it used.
    /// </remarks>
    private async Task<(Form Form, FormVersion Version)> PublishedAsync(
        params FormField[] extra)
    {
        var form = await Forms.CreateAsync(
            await db.AddEventAsync(), "Application", "application", null);

        var draft = await Forms.DraftAsync(form.Id, null);
        await Forms.SaveDraftAsync(form.Id, [.. draft.Fields, .. extra]);

        return (form, await Forms.PublishAsync(form.Id, null));
    }

    /// <summary>A complete, valid set of answers to MLH's questions.</summary>
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

    private async Task<T?> ColumnAsync<T>(Guid applicationId, string column)
    {
        await using var cmd = db.DataSource.CreateCommand(
            $"SELECT {column} FROM applications.applications WHERE id = @id");
        cmd.Parameters.AddWithValue("id", applicationId);

        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    private async Task<string?> ResponseAsync(Guid applicationId, string key)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT responses->>@key FROM applications.applications WHERE id = @id");
        cmd.Parameters.AddWithValue("id", applicationId);
        cmd.Parameters.AddWithValue("key", key);

        return await cmd.ExecuteScalarAsync() as string;
    }

    // ------------------------------------------------------- what is stored ---

    [Fact]
    public async Task A_completed_form_becomes_a_submitted_application()
    {
        var (form, version) = await PublishedAsync();

        var id = await Submissions.SubmitApplicationAsync(
            form, version, Answers(Unique("submitted")));

        Assert.Equal("submitted", await ColumnAsync<string>(id, "status"));

        // submitted_at is what the review queue orders on. It is stamped by a
        // trigger on UPDATE OF status, so a row inserted as 'submitted'
        // outright would have a null here and sit outside every queue.
        Assert.NotNull(await ColumnAsync<DateTime?>(id, "submitted_at"));
    }

    [Fact]
    public async Task An_answer_the_form_routes_to_a_column_lands_in_that_column()
    {
        // The whole reason a field carries a Storage and a Column. These are
        // the answers that get filtered, exported and read at check-in, and an
        // export that has to dig through JSON is one somebody gets wrong.
        var (form, version) = await PublishedAsync();

        var id = await Submissions.SubmitApplicationAsync(
            form, version, Answers(Unique("columns")));

        Assert.Equal("Ada", await ColumnAsync<string>(id, "first_name"));
        Assert.Equal(20, await ColumnAsync<int?>(id, "age"));
        Assert.Equal("Morgan State University", await ColumnAsync<string>(id, "school"));
    }

    [Fact]
    public async Task Everything_else_lands_in_responses()
    {
        // The other half of the split: a question the team invented this year
        // must not need a migration, and must not grow the table by a column
        // per question per year.
        var (form, version) = await PublishedAsync(new FormField
        {
            Key = "why_apply",
            Type = FieldType.Paragraph,
            Label = "Why do you want to come?",
        });

        var id = await Submissions.SubmitApplicationAsync(
            form, version, Answers(Unique("responses"), ("why_apply", "To build something.")));

        Assert.Equal("To build something.", await ResponseAsync(id, "why_apply"));
    }

    [Fact]
    public async Task A_ticked_agreement_records_the_moment_it_was_ticked()
    {
        // "They agreed" is weaker evidence than "they agreed at 14:03 on the
        // 12th against form version 3", and this is a legal agreement we may
        // have to show.
        var (form, version) = await PublishedAsync();

        var id = await Submissions.SubmitApplicationAsync(
            form, version, Answers(Unique("agreed")));

        Assert.NotNull(await ColumnAsync<DateTime?>(id, "mlh_coc_agreed_at"));
        Assert.NotNull(await ColumnAsync<DateTime?>(id, "mlh_data_sharing_at"));
    }

    [Fact]
    public async Task An_agreement_stored_as_a_flag_stays_a_flag()
    {
        // Marketing consent is a boolean rather than a timestamp — nobody
        // needs to evidence when somebody opted into email — and the same tick
        // on the page has to mean different things in the two columns.
        var (form, version) = await PublishedAsync();

        var declined = await Submissions.SubmitApplicationAsync(
            form, version, Answers(Unique("no-marketing")));
        var accepted = await Submissions.SubmitApplicationAsync(
            form, version, Answers(Unique("marketing"), ("mlh_marketing_opt_in", true)));

        Assert.False(await ColumnAsync<bool?>(declined, "mlh_marketing_opt_in"));
        Assert.True(await ColumnAsync<bool?>(accepted, "mlh_marketing_opt_in"));
    }

    [Fact]
    public async Task The_version_they_answered_is_recorded_against_the_row()
    {
        // Without it an answer cannot be read against the questions it was
        // actually given, which is the guarantee the whole versioning scheme
        // exists to provide.
        var (form, version) = await PublishedAsync();

        var id = await Submissions.SubmitApplicationAsync(
            form, version, Answers(Unique("version")));

        Assert.Equal(version.Version, await ColumnAsync<int?>(id, "form_version"));
    }

    [Fact]
    public async Task A_submission_leaves_the_trail_it_is_supposed_to()
    {
        // Started, then submitted. An application whose history begins at its
        // second status is one whose history cannot be trusted, and the
        // triggers write these rows whether or not this code remembers to.
        var (form, version) = await PublishedAsync();

        var id = await Submissions.SubmitApplicationAsync(
            form, version, Answers(Unique("trail")));

        // Asserted on the transitions rather than on the order they come back
        // in. created_at is now(), which is transaction time, so both rows
        // carry the same instant and nothing orders them — the pair
        // (from, to) is what actually says what happened.
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT coalesce(from_status, '-') || '>' || to_status "
            + "FROM applications.status_history WHERE application_id = @id");
        cmd.Parameters.AddWithValue("id", id);

        var trail = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            trail.Add(reader.GetString(0));
        }

        Assert.Equal(2, trail.Count);
        Assert.Contains("->incomplete", trail);
        Assert.Contains("incomplete>submitted", trail);
    }

    [Fact]
    public async Task Several_choices_all_survive()
    {
        var (form, version) = await PublishedAsync(new FormField
        {
            Key = "tracks",
            Type = FieldType.Checkboxes,
            Label = "Which tracks interest you?",
            Options = [new("web", "Web"), new("ml", "ML"), new("hardware", "Hardware")],
        });

        var id = await Submissions.SubmitApplicationAsync(
            form, version, Answers(Unique("tracks"), ("tracks", new[] { "web", "hardware" })));

        // Read back through jsonb rather than by comparing the serialised
        // text, so this asserts what was stored and not how Postgres prints it.
        await using var cmd = db.DataSource.CreateCommand("""
            SELECT jsonb_array_length(responses->'tracks'),
                   responses->'tracks'->>0,
                   responses->'tracks'->>1
              FROM applications.applications WHERE id = @id
            """);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        Assert.Equal(2, reader.GetInt32(0));
        Assert.Equal("web", reader.GetString(1));
        Assert.Equal("hardware", reader.GetString(2));
    }

    // ------------------------------------------------ what is not trusted ---

    [Fact]
    public async Task An_answer_to_a_question_the_form_does_not_ask_is_ignored()
    {
        // The rule the endpoint rests on. The questions come from the
        // published version loaded here, so an extra key in the request is not
        // a question — and one named after a column would otherwise be a way
        // to accept your own application.
        var (form, version) = await PublishedAsync();

        var id = await Submissions.SubmitApplicationAsync(
            form, version,
            Answers(Unique("extra"), ("status", "accepted"), ("decided_by", "me")));

        Assert.Equal("submitted", await ColumnAsync<string>(id, "status"));
        Assert.Null(await ResponseAsync(id, "status"));
    }

    [Fact]
    public async Task A_question_pointed_at_a_column_it_may_not_write_is_kept_in_responses()
    {
        // A form author can type any column name into the builder. Only the
        // ones on the allow-list are written as columns; the rest keep their
        // answers in responses rather than either being lost or letting a form
        // decide its own application had been accepted.
        var (form, version) = await PublishedAsync(new FormField
        {
            Key = "sneaky",
            Type = FieldType.ShortText,
            Label = "Anything else?",
            Storage = AnswerStorage.Column,
            Column = "status",
        });

        var id = await Submissions.SubmitApplicationAsync(
            form, version, Answers(Unique("sneaky"), ("sneaky", "accepted")));

        Assert.Equal("submitted", await ColumnAsync<string>(id, "status"));
        Assert.Equal("accepted", await ResponseAsync(id, "sneaky"));
    }

    [Fact]
    public async Task A_second_application_from_one_address_is_refused()
    {
        // The dedupe rule is the unique index, not a lookup. Checking first
        // and inserting after leaves a gap exactly wide enough for somebody
        // double-tapping Submit on a slow connection.
        var (form, version) = await PublishedAsync();
        var email = Unique("twice");

        await Submissions.SubmitApplicationAsync(form, version, Answers(email));

        await Assert.ThrowsAsync<DuplicateApplicationException>(
            () => Submissions.SubmitApplicationAsync(form, version, Answers(email)));
    }

    [Fact]
    public async Task Capitalising_an_address_does_not_buy_a_second_application()
    {
        // The index is on lower(email). Somebody typing their address with a
        // capital the second time is the ordinary way this gets tried.
        var (form, version) = await PublishedAsync();
        var email = Unique("case");

        await Submissions.SubmitApplicationAsync(form, version, Answers(email));

        await Assert.ThrowsAsync<DuplicateApplicationException>(
            () => Submissions.SubmitApplicationAsync(
                form, version, Answers(email.ToUpperInvariant())));
    }

    [Fact]
    public async Task A_form_that_never_asks_for_an_address_cannot_create_an_applicant()
    {
        // Refused with something a person can act on rather than a not-null
        // violation. Nothing an applicant types fixes this: the form needs
        // republishing with an email question on it.
        var (form, version) = await PublishedAsync();
        var withoutEmail = version with
        {
            Fields = [.. version.Fields.Where(f => f.Key != "email")],
        };

        await Assert.ThrowsAsync<FormCannotCreateApplicantsException>(
            () => Submissions.SubmitApplicationAsync(
                form, withoutEmail, Answers(Unique("nowhere"))));
    }

    // ---------------------------------------------------------- validation ---

    [Fact]
    public void A_required_answer_that_was_left_out_is_refused()
    {
        var problems = SubmissionValidation.Check(
            MlhFields.All, Answers(Unique("missing"), ("phone", null)));

        Assert.Contains(problems, p => p.FieldKey == "phone");
    }

    [Fact]
    public void An_agreement_that_was_not_ticked_is_the_same_as_not_answering_it()
    {
        // false arriving for a required consent is not an answer, it is the
        // absence of one. Reading it as "present" is how an application gets
        // submitted without the agreement MLH affiliation depends on.
        var problems = SubmissionValidation.Check(
            MlhFields.All, Answers(Unique("unticked"), ("mlh_coc_agreed_at", false)));

        Assert.Contains(problems, p => p.FieldKey == "mlh_coc_agreed_at");
    }

    [Fact]
    public void An_option_that_is_not_on_the_form_is_refused()
    {
        // The value is what gets stored and counted later, so an unlisted one
        // does not fail loudly — it turns up months on as a category on a
        // report nobody put there.
        var problems = SubmissionValidation.Check(
            MlhFields.All, Answers(Unique("option"), ("level_of_study", "wizard")));

        Assert.Contains(problems, p => p.FieldKey == "level_of_study");
    }

    [Fact]
    public void A_number_outside_the_range_the_form_set_is_refused()
    {
        var age = MlhFields.All.Single(f => f.Key == "age") with { Min = 18, Max = 100 };
        var fields = MlhFields.All.Select(f => f.Key == "age" ? age : f).ToList();

        var problems = SubmissionValidation.Check(
            fields, Answers(Unique("range"), ("age", 4)));

        Assert.Contains(problems, p => p.FieldKey == "age");
    }

    [Fact]
    public void An_age_of_twenty_and_a_half_is_refused_rather_than_rounded()
    {
        // The column is an int. Rounding on the way in would file somebody
        // under an age they did not give, and failing at the INSERT would show
        // them a 500 for a typo.
        var problems = SubmissionValidation.Check(
            MlhFields.All, Answers(Unique("half"), ("age", 20.5)));

        Assert.Contains(problems, p => p.FieldKey == "age");
    }

    [Fact]
    public void An_address_that_is_not_one_is_refused()
    {
        // This is the only way we can reach them. Getting it wrong is not
        // caught later by anything.
        var problems = SubmissionValidation.Check(
            MlhFields.All, Answers("ada at morgan dot edu"));

        Assert.Contains(problems, p => p.FieldKey == "email");
    }

    [Fact]
    public void An_answer_longer_than_the_column_can_reasonably_hold_is_refused()
    {
        // No authentication on this endpoint, and responses is a jsonb with
        // nothing bounding it. A cap has to exist even when the form sets none.
        var problems = SubmissionValidation.Check(
            MlhFields.All, Answers(Unique("long"), ("first_name", new string('a', 5_000))));

        Assert.Contains(problems, p => p.FieldKey == "first_name");
    }

    [Fact]
    public void Every_problem_comes_back_at_once()
    {
        // This is a phone form somebody is filling in on a bus. One complaint
        // at a time turns a single pass into six round trips.
        var problems = SubmissionValidation.Check(
            MlhFields.All,
            Answers(Unique("several"), ("phone", null), ("school", null), ("age", "old")));

        Assert.Equal(3, problems.Count);
    }
}
