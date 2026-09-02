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

    [Fact]
    public async Task A_new_form_starts_with_MLHs_questions_on_it()
    {
        // Starting empty means every form begins with somebody copying an
        // obligation out of a PDF, and one of those eventually goes wrong.
        var draft = await Store.DraftAsync(await db.AddEventAsync(), null);

        Assert.Contains(draft.Fields, f => f.Key == "mlh_coc_agreed_at");
        Assert.Contains(draft.Fields, f => f.Key == "phone");
        Assert.Equal("draft", draft.Status);
    }

    [Fact]
    public async Task Asking_for_the_draft_twice_gives_the_same_one()
    {
        // Otherwise opening the builder in two tabs quietly creates two drafts,
        // and publishing becomes a question about which.
        var eventId = await db.AddEventAsync();

        var first = await Store.DraftAsync(eventId, null);
        var second = await Store.DraftAsync(eventId, null);

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task Publishing_makes_the_draft_the_live_form()
    {
        var eventId = await db.AddEventAsync();
        await Store.DraftAsync(eventId, null);

        var published = await Store.PublishAsync(eventId, null);

        Assert.Equal("published", published.Status);
        Assert.Equal(published.Id, (await Store.PublishedAsync(eventId))!.Id);
    }

    [Fact]
    public async Task A_form_with_problems_is_refused_before_anything_is_written()
    {
        // A half-published form is not a state worth writing recovery code for.
        var eventId = await db.AddEventAsync();
        var draft = await Store.DraftAsync(eventId, null);
        await Store.SaveDraftAsync(eventId, [.. draft.Fields.Where(f => f.Key != "phone")]);

        var refused = await Assert.ThrowsAsync<FormNotPublishableException>(
            () => Store.PublishAsync(eventId, null));

        Assert.Contains(refused.Problems, p => p.FieldKey == "phone");
        Assert.Null(await Store.PublishedAsync(eventId));
    }

    [Fact]
    public async Task Publishing_again_retires_the_previous_form()
    {
        // Two live forms would mean applicants answering different questions
        // with nothing to say which was current.
        var eventId = await db.AddEventAsync();
        await Store.DraftAsync(eventId, null);
        var first = await Store.PublishAsync(eventId, null);

        var draft = await Store.DraftAsync(eventId, null);
        await Store.SaveDraftAsync(eventId, [.. draft.Fields]);
        var second = await Store.PublishAsync(eventId, null);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(second.Id, (await Store.PublishedAsync(eventId))!.Id);
        Assert.Equal("retired",
            (await Store.HistoryAsync(eventId)).Single(v => v.Id == first.Id).Status);
    }

    [Fact]
    public async Task A_new_draft_starts_from_what_is_published()
    {
        // Editing a live form should mean changing it, not rebuilding it.
        var eventId = await db.AddEventAsync();
        var draft = await Store.DraftAsync(eventId, null);
        await Store.SaveDraftAsync(eventId, [.. draft.Fields, new FormField
        {
            Key = "why_apply",
            Type = FieldType.Paragraph,
            Label = "Why do you want to come?",
        }]);
        await Store.PublishAsync(eventId, null);

        Assert.Contains(await Store.DraftAsync(eventId, null) is { } next ? next.Fields : [],
            f => f.Key == "why_apply");
    }

    [Fact]
    public async Task Versions_climb()
    {
        var eventId = await db.AddEventAsync();
        var one = await Store.DraftAsync(eventId, null);
        await Store.PublishAsync(eventId, null);

        Assert.Equal(one.Version + 1, (await Store.DraftAsync(eventId, null)).Version);
    }

    [Fact]
    public async Task A_published_form_cannot_be_edited_even_by_hand()
    {
        // The guarantee an answer rests on, enforced by a trigger so it holds
        // for a support script at 2am as well as for this code.
        var eventId = await db.AddEventAsync();
        await Store.DraftAsync(eventId, null);
        var published = await Store.PublishAsync(eventId, null);

        await using var cmd = db.DataSource.CreateCommand(
            "UPDATE applications.form_versions SET fields = '[]'::jsonb WHERE id = @id");
        cmd.Parameters.AddWithValue("id", published.Id);

        var refused = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Contains("published", refused.MessageText);
    }

    [Fact]
    public async Task Options_survive_the_round_trip()
    {
        // Stored as JSON, so a serialisation mistake shows up as an empty
        // dropdown in front of applicants rather than as an error.
        var draft = await Store.DraftAsync(await db.AddEventAsync(), null);
        var level = draft.Fields.Single(f => f.Key == "level_of_study");

        Assert.Equal(FieldType.Select, level.Type);
        Assert.Contains(level.Options, o => o.Value == "undergraduate-3y");
    }
}
