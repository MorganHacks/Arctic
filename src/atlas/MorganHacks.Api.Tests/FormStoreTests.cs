using MorganHacks.Applications.Forms;
using Npgsql;

namespace MorganHacks.Api.Tests;

/// <summary>
/// Versioning, against a real database.
/// </summary>
/// <remarks>
/// The rules here are what make an answer readable a year later: exactly one
/// live form, and a published one that never changes under the people who
/// already answered it.
/// </remarks>
public class FormStoreTests(ApplicationsDatabase db) : IClassFixture<ApplicationsDatabase>
{
    private PostgresFormStore Store => new(db.DataSource);

    /// <summary>An event with an application form on it, ready to build.</summary>
    private async Task<Form> ApplicationFormAsync() =>
        await Store.CreateAsync(await db.AddEventAsync(), "Application", "application", null);

    [Fact]
    public async Task A_new_form_starts_with_MLHs_questions_on_it()
    {
        // Starting empty means every form begins with somebody copying an
        // obligation out of a PDF, and one of those eventually goes wrong.
        var draft = await Store.DraftAsync((await ApplicationFormAsync()).Id, null);

        Assert.Contains(draft.Fields, f => f.Key == "mlh_coc_agreed_at");
        Assert.Contains(draft.Fields, f => f.Key == "phone");
        Assert.Equal("draft", draft.Status);
    }

    [Fact]
    public async Task Asking_for_the_draft_twice_gives_the_same_one()
    {
        // Otherwise opening the builder in two tabs quietly creates two drafts,
        // and publishing becomes a question about which.
        var form = await ApplicationFormAsync();

        var first = await Store.DraftAsync(form.Id, null);
        var second = await Store.DraftAsync(form.Id, null);

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task Publishing_makes_the_draft_the_live_form()
    {
        var form = await ApplicationFormAsync();
        await Store.DraftAsync(form.Id, null);

        var published = await Store.PublishAsync(form.Id, null);

        Assert.Equal("published", published.Status);
        Assert.Equal(published.Id, (await Store.PublishedAsync(form.Id))!.Id);
    }

    [Fact]
    public async Task A_form_with_problems_is_refused_before_anything_is_written()
    {
        // A half-published form is not a state worth writing recovery code for.
        var form = await ApplicationFormAsync();
        var draft = await Store.DraftAsync(form.Id, null);
        await Store.SaveDraftAsync(form.Id, [.. draft.Fields.Where(f => f.Key != "phone")]);

        var refused = await Assert.ThrowsAsync<FormNotPublishableException>(
            () => Store.PublishAsync(form.Id, null));

        Assert.Contains(refused.Problems, p => p.FieldKey == "phone");
        Assert.Null(await Store.PublishedAsync(form.Id));
    }

    [Fact]
    public async Task Publishing_again_retires_the_previous_form()
    {
        // Two live forms would mean applicants answering different questions
        // with nothing to say which was current.
        var form = await ApplicationFormAsync();
        await Store.DraftAsync(form.Id, null);
        var first = await Store.PublishAsync(form.Id, null);

        var draft = await Store.DraftAsync(form.Id, null);
        await Store.SaveDraftAsync(form.Id, [.. draft.Fields]);
        var second = await Store.PublishAsync(form.Id, null);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(second.Id, (await Store.PublishedAsync(form.Id))!.Id);
        Assert.Equal("retired",
            (await Store.HistoryAsync(form.Id)).Single(v => v.Id == first.Id).Status);
    }

    [Fact]
    public async Task A_new_draft_starts_from_what_is_published()
    {
        // Editing a live form should mean changing it, not rebuilding it.
        var form = await ApplicationFormAsync();
        var draft = await Store.DraftAsync(form.Id, null);
        await Store.SaveDraftAsync(form.Id, [.. draft.Fields, new FormField
        {
            Key = "why_apply",
            Type = FieldType.Paragraph,
            Label = "Why do you want to come?",
        }]);
        await Store.PublishAsync(form.Id, null);

        Assert.Contains(await Store.DraftAsync(form.Id, null) is { } next ? next.Fields : [],
            f => f.Key == "why_apply");
    }

    [Fact]
    public async Task Versions_climb()
    {
        var form = await ApplicationFormAsync();
        var one = await Store.DraftAsync(form.Id, null);
        await Store.PublishAsync(form.Id, null);

        Assert.Equal(one.Version + 1, (await Store.DraftAsync(form.Id, null)).Version);
    }

    [Fact]
    public async Task A_published_form_cannot_be_edited_even_by_hand()
    {
        // The guarantee an answer rests on, enforced by a trigger so it holds
        // for a support script at 2am as well as for this code.
        var form = await ApplicationFormAsync();
        await Store.DraftAsync(form.Id, null);
        var published = await Store.PublishAsync(form.Id, null);

        await using var cmd = db.DataSource.CreateCommand(
            "UPDATE applications.form_versions SET fields = '[]'::jsonb WHERE id = @id");
        cmd.Parameters.AddWithValue("id", published.Id);

        var refused = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Contains("published", refused.MessageText);
    }

    [Fact]
    public async Task A_form_is_reachable_by_the_code_in_its_link()
    {
        // The code is what goes on a flyer, so it has to survive the form being
        // edited — which is why it lives on the form rather than on a version.
        var form = await ApplicationFormAsync();

        var found = await Store.ByCodeAsync(form.Code);

        Assert.Equal(form.Id, found!.Id);
        Assert.Matches("^[a-z2-9]{7}$", form.Code);
    }

    [Fact]
    public async Task A_code_is_matched_however_somebody_typed_it()
    {
        // People type these from a whiteboard. Case and stray spaces are not a
        // reason to show somebody a page saying their link is wrong.
        var form = await ApplicationFormAsync();

        Assert.NotNull(await Store.ByCodeAsync($"  {form.Code.ToUpperInvariant()} "));
    }

    [Fact]
    public async Task An_unknown_code_is_absent_rather_than_an_error()
    {
        Assert.Null(await Store.ByCodeAsync("zzzzzzz"));
    }

    [Fact]
    public async Task An_event_has_only_one_application_form()
    {
        // Two would mean two places an applicant could apply, and nothing to
        // say which counted.
        var form = await ApplicationFormAsync();
        var eventId = (await Store.ByCodeAsync(form.Code))!.EventId;

        await Assert.ThrowsAsync<Npgsql.PostgresException>(
            () => Store.CreateAsync(eventId, "Another", "application", null));
    }

    [Fact]
    public async Task An_event_can_have_as_many_surveys_as_it_likes()
    {
        var form = await ApplicationFormAsync();
        var eventId = (await Store.ByCodeAsync(form.Code))!.EventId;

        var one = await Store.CreateAsync(eventId, "Mentor sign-up", "survey", null);
        var two = await Store.CreateAsync(eventId, "Post-event survey", "survey", null);

        Assert.NotEqual(one.Code, two.Code);
    }

    [Fact]
    public async Task Options_survive_the_round_trip()
    {
        // Stored as JSON, so a serialisation mistake shows up as an empty
        // dropdown in front of applicants rather than as an error.
        var draft = await Store.DraftAsync((await ApplicationFormAsync()).Id, null);
        var level = draft.Fields.Single(f => f.Key == "level_of_study");

        Assert.Equal(FieldType.Select, level.Type);
        Assert.Contains(level.Options, o => o.Value == "undergraduate-3y");
    }
}
