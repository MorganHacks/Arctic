using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The builder's API, against a real database and the real seeded baselines.
/// </summary>
/// <remarks>
/// Two things are being tested here that the store's own tests cannot reach.
/// <para>
/// The first is the gate: reading a form and writing one are different
/// permissions, and a filter that is on five routes and missing from the sixth
/// looks exactly like a filter that is on all six.
/// </para>
/// <para>
/// The second is what a draft round trip does and does not change. A question
/// is the author's to reword, reorder or remove, and the tests below check that
/// what was sent is what comes back — the key excepted, which is the one part
/// of a draft the server still refuses to accept in the wrong shape.
/// </para>
/// </remarks>
public class FormBuilderTests(ApplicationsDatabase db)
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

    // -------------------------------------------------------------- the gate ---

    [Fact]
    public async Task Reading_a_form_takes_the_permission_that_reads_applications()
    {
        // Anybody who works the queue should be able to see what was asked,
        // because half of reading an answer is knowing the question. Somebody
        // with no permission at all should not.
        var builder = await OrganizerAsync(Permission.FormsManage.Value);
        var form = await CreateFormAsync(builder, await db.AddEventAsync());

        var reader = await OrganizerAsync(Permission.ApplicationsView.Value);
        var nobody = await OrganizerAsync();

        Assert.Equal(HttpStatusCode.OK,
            (await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", reader)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", nobody)).StatusCode);
    }

    [Fact]
    public async Task Reading_a_form_is_not_enough_to_change_one()
    {
        // The split the permission exists for. The queue is read by comms and
        // logistics too; deciding what several hundred people are asked, once,
        // with no way to correct it afterwards, is a smaller group than that.
        var reader = await OrganizerAsync(Permission.ApplicationsView.Value);
        var eventId = await db.AddEventAsync();
        var form = await CreateFormAsync(
            await OrganizerAsync(Permission.FormsManage.Value), eventId);

        var created = await Send(HttpMethod.Post, "/admin/forms", reader,
            new { name = "Refused", kind = "survey" });
        var saved = await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", reader,
            new { fields = Array.Empty<object>() });
        var published = await Send(HttpMethod.Post, $"/admin/forms/{form}/publish", reader);
        var renamed = await Send(HttpMethod.Put, $"/admin/forms/{form}/name", reader, new { name = "Refused" });
        var removed = await Send(HttpMethod.Delete, $"/admin/forms/{form}", reader);

        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, saved.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, published.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, removed.StatusCode);
    }

    [Fact]
    public async Task Registration_can_build_forms_and_logistics_cannot()
    {
        // The baseline the migration writes, not a grant made up by this test.
        // Granting forms.manage by hand here would pass whether or not the
        // migration that puts it on the registration team ever ran.
        var eventId = await db.AddEventAsync();
        var registration = await TeamMemberAsync("registration");
        var logistics = await TeamMemberAsync("logistics");

        var allowed = await Send(HttpMethod.Post, $"/admin/forms?eventId={eventId}",
            registration, new { name = "Mentor sign-up", kind = "survey" });
        var refused = await Send(HttpMethod.Post, $"/admin/forms?eventId={eventId}",
            logistics, new { name = "Mentor sign-up", kind = "survey" });

        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task Every_route_refuses_a_caller_with_no_session()
    {
        // On every route rather than the one somebody remembered. A route
        // reachable without a session is reachable by the internet, and this
        // one hands out the questions several hundred people will answer.
        var id = Guid.NewGuid();
        (HttpMethod Method, string Path)[] routes =
        [
            (HttpMethod.Get, "/admin/forms"),
            (HttpMethod.Post, "/admin/forms"),
            (HttpMethod.Get, $"/admin/forms/{id}/draft"),
            (HttpMethod.Put, $"/admin/forms/{id}/draft"),
            (HttpMethod.Post, $"/admin/forms/{id}/publish"),
            (HttpMethod.Get, $"/admin/forms/{id}/versions"),
            (HttpMethod.Put, $"/admin/forms/{id}/name"),
            (HttpMethod.Delete, $"/admin/forms/{id}"),
        ];

        foreach (var (method, path) in routes)
        {
            var response = await _app.CreateClient()
                .SendAsync(new HttpRequestMessage(method, path));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    // ----------------------------------------------- the questions it starts with ---

    [Fact]
    public async Task A_new_application_form_starts_with_the_usual_questions()
    {
        // The one thing the starting set still guarantees. An author opening a
        // new application form finds the ordinary questions already written
        // rather than an empty page.
        var (cookie, form, _) = await OpenBuilderAsync();

        var keys = await KeysOfDraft(cookie, form);
        Assert.Contains("email", keys);
        Assert.Contains("mlh_coc_agreed_at", keys);
    }

    [Fact]
    public async Task A_starting_question_can_be_taken_off_the_form()
    {
        // It used to be refused outright. The author decides now, and a save
        // that omits a question means the author took it off.
        var (cookie, form, fields) = await OpenBuilderAsync();
        Remove(fields, "mlh_coc_agreed_at");

        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie, new { fields }))
            .EnsureSuccess();

        Assert.DoesNotContain("mlh_coc_agreed_at", await KeysOfDraft(cookie, form));
    }

    [Fact]
    public async Task Rewording_a_starting_question_takes()
    {
        // The wording used to be put straight back from the server, which made
        // the label box on those questions a control that did nothing.
        var (cookie, form, fields) = await OpenBuilderAsync();
        Field(fields, "mlh_coc_agreed_at")!["label"] = "I agree to the code of conduct.";
        Field(fields, "mlh_coc_agreed_at")!["required"] = false;

        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie, new { fields }))
            .EnsureSuccess();

        var saved = Field(await DraftFieldsAsync(cookie, form), "mlh_coc_agreed_at")!;
        Assert.Equal("I agree to the code of conduct.", saved["label"]!.GetValue<string>());
        Assert.False(saved["required"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_form_that_keeps_none_of_the_starting_questions_can_be_published()
    {
        // The end of the rule, seen from the far side: publishing no longer
        // checks that any particular question is on the form.
        var (cookie, form, _) = await OpenBuilderAsync();
        JsonArray fields = [Question("why_apply", "paragraph", "Why do you want to come?")];

        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie, new { fields }))
            .EnsureSuccess();

        await (await Send(HttpMethod.Post, $"/admin/forms/{form}/publish", cookie))
            .EnsureSuccess();
    }

    // ------------------------------------------------------------------ keys ---

    [Fact]
    public async Task A_question_with_an_unusable_key_is_refused_rather_than_given_one()
    {
        // A key generated on the server would be a different one on every
        // autosave, and the key is what an answer is filed under — the one
        // property in the whole document that must never change by itself.
        var (cookie, form, fields) = await OpenBuilderAsync();
        fields.Add(Question("Favourite Language!", label: "Favourite language"));

        var response = await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie,
            new { fields });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Editing_a_question_leaves_the_key_its_answers_are_filed_under_alone()
    {
        // Renaming the question is the ordinary thing to do the week before
        // launch. Renaming the key would orphan every answer already given,
        // and nothing on screen would look wrong while it happened.
        var (cookie, form, fields) = await OpenBuilderAsync();
        fields.Add(Question("why_apply", label: "Why do you want to come?"));
        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie, new { fields }))
            .EnsureSuccess();

        var reopened = await DraftFieldsAsync(cookie, form);
        Field(reopened, "why_apply")!["label"] = "What are you hoping to build?";
        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie,
            new { fields = reopened })).EnsureSuccess();

        var saved = Field(await DraftFieldsAsync(cookie, form), "why_apply")!;
        Assert.Equal("What are you hoping to build?", saved["label"]!.GetValue<string>());
        Assert.Equal("why_apply", saved["key"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_choice_questions_options_survive_the_round_trip()
    {
        // Stored as JSON, so a serialisation mistake shows up as an empty
        // dropdown in front of applicants rather than as an error anybody sees.
        var (cookie, form, fields) = await OpenBuilderAsync();
        fields.Add(Question("shirt", "radio", "Shirt size", options:
            [new { value = "m", label = "Medium" }, new { value = "l", label = "Large" }]));

        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie, new { fields }))
            .EnsureSuccess();

        var saved = Field(await DraftFieldsAsync(cookie, form), "shirt")!;
        Assert.Equal("radio", saved["type"]!.GetValue<string>());
        Assert.Equal("Large", saved["options"]![1]!["label"]!.GetValue<string>());
    }

    [Fact]
    public async Task Reordering_questions_keeps_the_order_it_was_sent_in()
    {
        // Where a question sits on the page is the author's decision, and the
        // order is taken from the submission rather than reimposed.
        var (cookie, form, fields) = await OpenBuilderAsync();
        var moved = Field(fields, "mlh_coc_agreed_at")!.DeepClone();
        Remove(fields, "mlh_coc_agreed_at");
        fields.Insert(0, moved);

        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie, new { fields }))
            .EnsureSuccess();

        var saved = await DraftFieldsAsync(cookie, form);
        Assert.Equal("mlh_coc_agreed_at", saved[0]!["key"]!.GetValue<string>());
    }

    // ------------------------------------------------------------ publishing ---

    [Fact]
    public async Task A_refused_publish_names_every_problem_against_its_own_question()
    {
        // One problem at a time turns fixing a form into a guessing game where
        // each fix reveals the next complaint, and a problem with no key
        // attached is one the author has to go hunting for.
        var (cookie, form, fields) = await OpenBuilderAsync();
        fields.Add(Question("shirt", "select", "Shirt size"));
        fields.Add(Question("why_apply", "paragraph", ""));
        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie, new { fields }))
            .EnsureSuccess();

        var response = await Send(HttpMethod.Post, $"/admin/forms/{form}/publish", cookie);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var keys = await KeysOfProblems(response);
        Assert.Contains("shirt", keys);
        Assert.Contains("why_apply", keys);
    }

    [Fact]
    public async Task A_refused_publish_leaves_nothing_live()
    {
        // A half-published form is not a state worth writing recovery code
        // for, and an applicant who lands mid-attempt must not see one.
        var (cookie, form, fields) = await OpenBuilderAsync();
        fields.Add(Question("shirt", "select", "Shirt size"));
        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie, new { fields }))
            .EnsureSuccess();

        await Send(HttpMethod.Post, $"/admin/forms/{form}/publish", cookie);

        var body = await ReadAsync(await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", cookie));
        Assert.Null(body["published"]);
    }

    [Fact]
    public async Task Publishing_a_form_that_is_ready_puts_it_in_front_of_applicants()
    {
        var (cookie, form, fields) = await OpenBuilderAsync();
        fields.Add(Question("why_apply", "paragraph", "Why do you want to come?"));
        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie, new { fields }))
            .EnsureSuccess();

        var response = await Send(HttpMethod.Post, $"/admin/forms/{form}/publish", cookie);
        response.EnsureSuccessStatusCode();

        var reopened = await ReadAsync(
            await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", cookie));
        Assert.Equal(1, reopened["published"]!["version"]!.GetValue<int>());

        // Reopening starts the next draft from what is live, so editing a
        // published form means changing it rather than rebuilding it.
        Assert.Equal(2, reopened["draft"]!["version"]!.GetValue<int>());
        Assert.Contains("why_apply",
            reopened["draft"]!["fields"]!.AsArray().Select(f => f!["key"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Publishing_reports_the_problems_a_draft_still_has_as_it_is_saved()
    {
        // Advisory, so the builder can show them beside the questions as
        // somebody works rather than only at the moment they press publish and
        // are told no. The refusal itself is still decided server-side, in the
        // store, inside the transaction that writes.
        var (cookie, form, fields) = await OpenBuilderAsync();
        fields.Add(Question("shirt", "select", "Shirt size"));

        var response = await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie,
            new { fields });
        response.EnsureSuccessStatusCode();

        Assert.Contains("shirt", await KeysOfProblems(response));
    }

    // ------------------------------------------------------- listing forms ---

    [Fact]
    public async Task The_list_shows_each_forms_code_and_whether_it_is_live()
    {
        // "Is this the one on the flyer" is the question somebody opens this
        // screen to answer, and both halves of it are on this row.
        var (cookie, form, fields) = await OpenBuilderAsync();
        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie, new { fields }))
            .EnsureSuccess();
        await (await Send(HttpMethod.Post, $"/admin/forms/{form}/publish", cookie))
            .EnsureSuccess();

        var eventId = (await ReadAsync(
            await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", cookie)))
            ["form"]!["eventId"]!.GetValue<Guid>();

        var listed = (await ReadAsync(
            await Send(HttpMethod.Get, $"/admin/forms?eventId={eventId}", cookie)))
            ["forms"]!.AsArray().Single()!;

        Assert.Matches("^[a-z2-9]{7}$", listed["code"]!.GetValue<string>());
        Assert.True(listed["published"]!.GetValue<bool>());
        Assert.Equal(1, listed["publishedVersion"]!.GetValue<int>());
        Assert.Equal(fields[0]!["label"]!.GetValue<string>(),
            listed["preview"]!["fields"]![0]!["label"]!.GetValue<string>());

        fields[0]!["label"] = "An unpublished change";
        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie, new { fields }))
            .EnsureSuccess();
        var unchanged = (await ReadAsync(
            await Send(HttpMethod.Get, $"/admin/forms?eventId={eventId}", cookie)))
            ["forms"]!.AsArray().Single()!;
        Assert.NotEqual("An unpublished change",
            unchanged["preview"]!["fields"]![0]!["label"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_list_previews_saved_draft_questions_and_choices_without_publishing()
    {
        var (cookie, form, fields) = await OpenBuilderAsync();
        fields.Insert(0, Question("interest", "radio", "What would you like to build?", true,
            [new { value = "web", label = "Web" }, new { value = "mobile", label = "Mobile" },
             new { value = "hardware", label = "Hardware" }, new { value = "other", label = "Other" }]));
        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie, new { fields }))
            .EnsureSuccess();
        var eventId = (await ReadAsync(
            await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", cookie)))
            ["form"]!["eventId"]!.GetValue<Guid>();

        var listed = (await ReadAsync(
            await Send(HttpMethod.Get, $"/admin/forms?eventId={eventId}", cookie)))
            ["forms"]!.AsArray().Single()!;
        Assert.False(listed["published"]!.GetValue<bool>());
        Assert.Equal(fields.Count, listed["preview"]!["questions"]!.GetValue<int>());
        var preview = listed["preview"]!["fields"]!.AsArray();
        Assert.Equal(4, preview.Count);
        Assert.Equal("What would you like to build?", preview[0]!["label"]!.GetValue<string>());
        Assert.Equal("radio", preview[0]!["type"]!.GetValue<string>());
        Assert.True(preview[0]!["required"]!.GetValue<bool>());
        Assert.Equal(["Web", "Mobile", "Hardware"], preview[0]!["options"]!.AsArray()
            .Select(option => option!["label"]!.GetValue<string>()));
        Assert.Null(preview[0]!["storage"]);
        Assert.Equal(fields.Count, (await DraftFieldsAsync(cookie, form)).Count);
    }

    [Fact]
    public async Task List_previews_follow_the_published_theme_and_keep_unpublished_changes_private()
    {
        var (cookie, form, fields) = await OpenBuilderAsync();
        var theme = new
        {
            accent = "#6750a4",
            background = "tint",
            font = "serif",
            size = "large",
            headerImage = "data:image/webp;base64,UklGRjgAAABXRUJQVlA4ICwAAADQAQCdASoEAAEAAUAmJaACdLoB+AADsAD++X2//yU1/jblq/+LORiOy8AAAA=="
        };
        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie, new { fields, theme })).EnsureSuccess();
        var draft = await ReadAsync(await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", cookie));
        var eventId = draft["form"]!["eventId"]!.GetValue<Guid>();
        var code = draft["form"]!["code"]!.GetValue<string>();

        async Task<JsonNode> PreviewTheme() => (await ReadAsync(
            await Send(HttpMethod.Get, $"/admin/forms?eventId={eventId}", cookie)))
            ["forms"]!.AsArray().Single()!["preview"]!["theme"]!;

        var preview = await PreviewTheme();
        Assert.Equal(theme.headerImage, preview["headerImage"]!.GetValue<string>());
        Assert.Equal(theme.accent, preview["accent"]!.GetValue<string>());
        Assert.Equal(theme.font, preview["font"]!.GetValue<string>());
        await (await Send(HttpMethod.Post, $"/admin/forms/{form}/publish", cookie)).EnsureSuccess();
        var live = await ReadAsync(await _app.CreateClient().GetAsync($"/forms/{code}"));
        Assert.True(JsonNode.DeepEquals(live["theme"], await PreviewTheme()));

        await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", cookie);
        var next = new { accent = "#28734f", background = "white", font = "mono", size = "small" };
        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie, new { fields, theme = next })).EnsureSuccess();
        Assert.True(JsonNode.DeepEquals(live["theme"], await PreviewTheme()));

        await (await Send(HttpMethod.Post, $"/admin/forms/{form}/publish", cookie)).EnsureSuccess();
        live = await ReadAsync(await _app.CreateClient().GetAsync($"/forms/{code}"));
        preview = await PreviewTheme();
        Assert.True(JsonNode.DeepEquals(live["theme"], preview));
        Assert.Equal(next.accent, preview["accent"]!.GetValue<string>());
        Assert.Null(preview["headerImage"]);
    }

    [Fact]
    public async Task Listing_a_new_form_does_not_create_a_draft_for_its_preview()
    {
        var eventId = await db.AddEventAsync();
        var cookie = await OrganizerAsync(Permission.FormsManage.Value, Permission.ApplicationsView.Value);
        var form = await CreateFormAsync(cookie, eventId);

        var listed = (await ReadAsync(
            await Send(HttpMethod.Get, $"/admin/forms?eventId={eventId}", cookie)))
            ["forms"]!.AsArray().Single()!;
        Assert.Null(listed["preview"]);
        var history = await ReadAsync(await Send(HttpMethod.Get, $"/admin/forms/{form}/versions", cookie));
        Assert.Empty(history["versions"]!.AsArray());
    }

    [Fact]
    public async Task A_second_application_form_on_one_event_is_a_conflict_not_a_crash()
    {
        // Two would mean two places an applicant could apply, and nothing to
        // say which counted. The database refuses it either way; this is about
        // the person filling in the form getting a sentence they can act on
        // rather than a 500.
        var eventId = await db.AddEventAsync();
        var cookie = await OrganizerAsync(Permission.FormsManage.Value);
        await CreateFormAsync(cookie, eventId);

        var second = await Send(HttpMethod.Post, $"/admin/forms?eventId={eventId}", cookie,
            new { name = "Application", kind = "application" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("application form", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_form_id_nobody_recognises_is_absent_rather_than_created()
    {
        // Asking for a draft creates one, which is right for a form that
        // exists and wrong for an id somebody mistyped: an orphan version row
        // nothing can ever reach again.
        var cookie = await OrganizerAsync(Permission.ApplicationsView.Value);

        var response = await Send(
            HttpMethod.Get, $"/admin/forms/{Guid.NewGuid()}/draft", cookie);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task History_records_each_version_and_what_became_of_it()
    {
        var (cookie, form, fields) = await OpenBuilderAsync();
        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie, new { fields }))
            .EnsureSuccess();
        await (await Send(HttpMethod.Post, $"/admin/forms/{form}/publish", cookie))
            .EnsureSuccess();
        await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", cookie);

        var versions = (await ReadAsync(
            await Send(HttpMethod.Get, $"/admin/forms/{form}/versions", cookie)))
            ["versions"]!.AsArray();

        // Newest first, so the draft somebody is holding is the top line.
        Assert.Equal("draft", versions[0]!["status"]!.GetValue<string>());
        Assert.Equal("published", versions[1]!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Version_history_names_the_creator_latest_editor_and_publisher()
    {
        var (creator, form, fields) = await OpenBuilderAsync();
        var first = (await ReadAsync(await Send(HttpMethod.Get, $"/admin/forms/{form}/versions", creator)))
            ["versions"]![0]!;
        var creatorId = first["actor"]!["id"]!.GetValue<Guid>();
        Assert.Null(first["updatedAt"]);

        var editorId = await db.AddPersonAsync($"editor-{Guid.NewGuid():N}@example.com");
        await db.GrantAsync(editorId, Permission.FormsManage.Value);
        await using (var profile = db.DataSource.CreateCommand(
            "UPDATE identity.people SET full_name = 'Form Editor', avatar_url = 'https://example.com/editor.png' WHERE id = @id"))
        {
            profile.Parameters.AddWithValue("id", editorId);
            await profile.ExecuteNonQueryAsync();
        }
        var editor = await SignInAsync(editorId);
        var reader = await OrganizerAsync(Permission.ApplicationsView.Value);
        fields[0]!["label"] = "Updated email question";
        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", editor, new { fields })).EnsureSuccess();

        var edited = (await ReadAsync(await Send(HttpMethod.Get, $"/admin/forms/{form}/versions", reader)))
            ["versions"]![0]!;
        Assert.Equal(editorId, edited["actor"]!["id"]!.GetValue<Guid>());
        Assert.Equal("Form Editor", edited["actor"]!["fullName"]!.GetValue<string>());
        Assert.Equal("https://example.com/editor.png", edited["actor"]!["avatarUrl"]!.GetValue<string>());
        Assert.NotNull(edited["updatedAt"]);

        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", creator, new { fields })).EnsureSuccess();
        var unchanged = (await ReadAsync(await Send(HttpMethod.Get, $"/admin/forms/{form}/versions", reader)))
            ["versions"]![0]!;
        Assert.True(JsonNode.DeepEquals(edited, unchanged));

        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", creator,
            new { fields, theme = new { accent = "#7c3aed" } })).EnsureSuccess();
        var themed = (await ReadAsync(await Send(HttpMethod.Get, $"/admin/forms/{form}/versions", reader)))
            ["versions"]![0]!;
        Assert.Equal(creatorId, themed["actor"]!["id"]!.GetValue<Guid>());

        await (await Send(HttpMethod.Post, $"/admin/forms/{form}/publish", editor)).EnsureSuccess();
        await (await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", creator)).EnsureSuccess();
        var versions = (await ReadAsync(await Send(HttpMethod.Get, $"/admin/forms/{form}/versions", reader)))
            ["versions"]!.AsArray();
        Assert.Equal(creatorId, versions[0]!["actor"]!["id"]!.GetValue<Guid>());
        Assert.Equal(editorId, versions[1]!["actor"]!["id"]!.GetValue<Guid>());
        Assert.Equal("published", versions[1]!["status"]!.GetValue<string>());
        Assert.NotNull(versions[1]!["publishedAt"]);

        await (await Send(HttpMethod.Post, $"/admin/forms/{form}/unpublish", editor)).EnsureSuccess();
        var retired = (await ReadAsync(await Send(HttpMethod.Get, $"/admin/forms/{form}/versions", reader)))
            ["versions"]![1]!;
        Assert.Equal(editorId, retired["actor"]!["id"]!.GetValue<Guid>());
        Assert.Equal("retired", retired["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Renaming_a_live_form_keeps_its_code_questions_and_published_version()
    {
        var (cookie, form, _) = await OpenBuilderAsync();
        await (await Send(HttpMethod.Post, $"/admin/forms/{form}/publish", cookie)).EnsureSuccess();
        var before = await ReadAsync(await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", cookie));

        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/name", cookie,
            new { name = "  New application name  " })).EnsureSuccess();

        var after = await ReadAsync(await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", cookie));
        Assert.Equal("New application name", after["form"]!["name"]!.GetValue<string>());
        Assert.Equal(before["form"]!["code"]!.GetValue<string>(), after["form"]!["code"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(before["draft"], after["draft"]));
        Assert.True(JsonNode.DeepEquals(before["published"], after["published"]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_rename_is_refused_without_changing_the_form(string? name)
    {
        var (cookie, form, _) = await OpenBuilderAsync();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Send(HttpMethod.Put, $"/admin/forms/{form}/name", cookie, new { name })).StatusCode);
        var after = await ReadAsync(await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", cookie));
        Assert.Equal("Application", after["form"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_long_name_and_a_missing_form_are_handled_without_server_errors()
    {
        var (cookie, form, _) = await OpenBuilderAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put,
            $"/admin/forms/{form}/name", cookie, new { name = new string('a', 201) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Put,
            $"/admin/forms/{Guid.NewGuid()}/name", cookie, new { name = "Missing" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(HttpMethod.Delete, $"/admin/forms/{Guid.NewGuid()}", cookie)).StatusCode);
    }

    [Fact]
    public async Task Removing_a_live_form_hides_it_retires_it_and_preserves_its_answers_and_history()
    {
        var (cookie, form, _) = await OpenBuilderAsync();
        await (await Send(HttpMethod.Post, $"/admin/forms/{form}/publish", cookie)).EnsureSuccess();
        using var scope = _app.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<MorganHacks.Applications.Forms.IFormStore>();
        var original = (await store.ByIdAsync(form))!;
        var history = await store.HistoryAsync(form);
        var person = await db.AddPersonAsync($"answer-{Guid.NewGuid():N}@example.com");
        await using (var insert = db.DataSource.CreateCommand("""
            INSERT INTO applications.form_submissions (form_id, form_version, person_id, answers)
            VALUES (@form, @version, @person, '{"test_answer":"Keep this answer"}')
            """))
        {
            insert.Parameters.AddWithValue("form", form);
            insert.Parameters.AddWithValue("version", history[0].Version);
            insert.Parameters.AddWithValue("person", person);
            await insert.ExecuteNonQueryAsync();
        }

        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(HttpMethod.Delete, $"/admin/forms/{form}", cookie)).StatusCode);

        Assert.Null(await store.ByCodeAsync(original.Code));
        Assert.Null(await store.ByIdAsync(form));
        Assert.Null(await store.PublishedAsync(form));
        Assert.DoesNotContain(await store.ForEventAsync(original.EventId), item => item.Id == form);
        var retained = await store.HistoryAsync(form);
        Assert.Equal(history.Count, retained.Count);
        Assert.Equal(history[0].Id, retained[0].Id);
        Assert.Equal("retired", retained[0].Status);
        await using var answer = db.DataSource.CreateCommand(
            "SELECT answers->>'test_answer' FROM applications.form_submissions WHERE form_id = @form");
        answer.Parameters.AddWithValue("form", form);
        Assert.Equal("Keep this answer", await answer.ExecuteScalarAsync());
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(HttpMethod.Post, $"/admin/forms/{form}/publish", cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(HttpMethod.Delete, $"/admin/forms/{form}", cookie)).StatusCode);
        var replacement = await CreateFormAsync(cookie, original.EventId);
        Assert.NotEqual(form, replacement);
    }

    [Fact]
    public async Task Removing_an_untouched_draft_does_not_create_a_version_or_touch_another_form()
    {
        var cookie = await OrganizerAsync(Permission.FormsManage.Value, Permission.ApplicationsView.Value);
        var eventId = await db.AddEventAsync();
        var first = await CreateFormAsync(cookie, eventId);
        var other = await ReadAsync(await Send(HttpMethod.Post, $"/admin/forms?eventId={eventId}", cookie,
            new { name = "Keep this survey", kind = "survey" }));
        await (await Send(HttpMethod.Delete, $"/admin/forms/{first}", cookie)).EnsureSuccess();

        using var scope = _app.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<MorganHacks.Applications.Forms.IFormStore>();
        Assert.Empty(await store.HistoryAsync(first));
        var remaining = Assert.Single(await store.ForEventAsync(eventId));
        Assert.Equal(other["id"]!.GetValue<Guid>(), remaining.Id);
    }

    [Fact]
    public async Task Themes_save_with_the_draft_and_only_reach_the_public_form_after_publishing()
    {
        var (cookie, form, fields) = await OpenBuilderAsync();
        var theme = new
        {
            accent = "#6750a4",
            background = "tint",
            font = "serif",
            size = "large",
            showMlhBadge = true,
            headerImage = "data:image/webp;base64,UklGRjgAAABXRUJQVlA4ICwAAADQAQCdASoEAAEAAUAmJaACdLoB+AADsAD++X2//yU1/jblq/+LORiOy8AAAA=="
        };
        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie, new { fields, theme })).EnsureSuccess();
        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie, new { fields })).EnsureSuccess();
        var draft = await ReadAsync(await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", cookie));
        Assert.Equal(theme.accent, draft["draft"]!["theme"]!["accent"]!.GetValue<string>());
        Assert.True(draft["draft"]!["theme"]!["showMlhBadge"]!.GetValue<bool>());
        Assert.Equal(theme.headerImage, draft["draft"]!["theme"]!["headerImage"]!.GetValue<string>());
        await (await Send(HttpMethod.Post, $"/admin/forms/{form}/publish", cookie)).EnsureSuccess();
        var code = draft["form"]!["code"]!.GetValue<string>();
        var live = await ReadAsync(await _app.CreateClient().GetAsync($"/forms/{code}"));
        Assert.Equal(theme.font, live["theme"]!["font"]!.GetValue<string>());
        Assert.True(live["theme"]!["showMlhBadge"]!.GetValue<bool>());
        Assert.Equal(theme.headerImage, live["theme"]!["headerImage"]!.GetValue<string>());
        draft = await ReadAsync(await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", cookie));
        Assert.Equal(theme.accent, draft["draft"]!["theme"]!["accent"]!.GetValue<string>());
        Assert.Equal(theme.headerImage, draft["draft"]!["theme"]!["headerImage"]!.GetValue<string>());
        var updated = new { accent = "#28734f", background = "white", font = "mono", size = "small" };
        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie, new { fields, theme = updated })).EnsureSuccess();
        live = await ReadAsync(await _app.CreateClient().GetAsync($"/forms/{code}"));
        Assert.Equal(theme.accent, live["theme"]!["accent"]!.GetValue<string>());
        Assert.True(live["theme"]!["showMlhBadge"]!.GetValue<bool>());
        Assert.Equal(theme.headerImage, live["theme"]!["headerImage"]!.GetValue<string>());
        await (await Send(HttpMethod.Post, $"/admin/forms/{form}/publish", cookie)).EnsureSuccess();
        live = await ReadAsync(await _app.CreateClient().GetAsync($"/forms/{code}"));
        Assert.Equal(updated.accent, live["theme"]!["accent"]!.GetValue<string>());
        Assert.Equal(updated.size, live["theme"]!["size"]!.GetValue<string>());
        Assert.False(live["theme"]!["showMlhBadge"]!.GetValue<bool>());
        Assert.Null(live["theme"]!["headerImage"]);
        await using var mutate = db.DataSource.CreateCommand(
            "UPDATE applications.form_versions SET theme = '{}' WHERE form_id = @form AND status = 'published'");
        mutate.Parameters.AddWithValue("form", form);
        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => mutate.ExecuteNonQueryAsync());
        Assert.Equal("23001", error.SqlState);
    }

    [Fact]
    public async Task Mlh_badge_uses_the_forms_event_date_and_updates_when_the_event_moves()
    {
        var (cookie, form, fields) = await OpenBuilderAsync();
        var draft = await ReadAsync(await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", cookie));
        Assert.False(draft["draft"]!["theme"]!["showMlhBadge"]!.GetValue<bool>());
        Assert.Null(draft["mlhSeason"]);
        var eventId = draft["form"]!["eventId"]!.GetValue<Guid>();
        var code = draft["form"]!["code"]!.GetValue<string>();
        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie,
            new { fields, theme = new { showMlhBadge = true } })).EnsureSuccess();
        await (await Send(HttpMethod.Post, $"/admin/forms/{form}/publish", cookie)).EnsureSuccess();
        var live = await ReadAsync(await _app.CreateClient().GetAsync($"/forms/{code}"));
        Assert.Null(live["mlhSeason"]);

        using var scope = _app.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<MorganHacks.Applications.Services.IEventStore>();
        foreach (var (date, season) in new[] { ("2027-04-03T13:00:00Z", 2027), ("2027-09-04T13:00:00Z", 2028) })
        {
            await events.UpdateAsync(eventId, new MorganHacks.Applications.Services.EventEdit
            {
                StartsAt = MorganHacks.Applications.Services.Patch<DateTimeOffset>.To(DateTimeOffset.Parse(date))
            });
            draft = await ReadAsync(await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", cookie));
            live = await ReadAsync(await _app.CreateClient().GetAsync($"/forms/{code}"));
            Assert.Equal(season, draft["mlhSeason"]!.GetValue<int>());
            Assert.Equal(season, live["mlhSeason"]!.GetValue<int>());
        }

        await (await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie,
            new { fields, theme = new { showMlhBadge = false } })).EnsureSuccess();
        await (await Send(HttpMethod.Post, $"/admin/forms/{form}/publish", cookie)).EnsureSuccess();
        live = await ReadAsync(await _app.CreateClient().GetAsync($"/forms/{code}"));
        Assert.Null(live["mlhSeason"]);
    }

    [Theory]
    [InlineData("https://example.com/header.png")]
    [InlineData("data:image/svg+xml;base64,PHN2Zz4=")]
    [InlineData("data:image/webp;base64,not-base64")]
    [InlineData("data:image/webp;base64,aGVsbG8=")]
    public async Task Invalid_header_images_do_not_change_the_saved_theme(string headerImage)
    {
        var (cookie, form, fields) = await OpenBuilderAsync();
        var response = await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie,
            new { fields, theme = new { headerImage } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var draft = await ReadAsync(await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", cookie));
        Assert.Null(draft["draft"]!["theme"]!["headerImage"]);
        Assert.False(new MorganHacks.Applications.Forms.FormTheme(HeaderImage: "data:image/webp;base64," + new string('A', 350_000)).IsValid());
    }

    [Theory]
    [InlineData("red", "neutral", "sans", "medium")]
    [InlineData("#003970", "url(evil)", "sans", "medium")]
    [InlineData("#003970", "neutral", "unknown", "medium")]
    [InlineData("#003970", "neutral", "sans", "huge")]
    public async Task Invalid_themes_are_rejected_without_changing_the_draft(string accent, string background, string font, string size)
    {
        var (cookie, form, fields) = await OpenBuilderAsync();
        var response = await Send(HttpMethod.Put, $"/admin/forms/{form}/draft", cookie,
            new { fields, theme = new { accent, background, font, size } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var draft = await ReadAsync(await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", cookie));
        Assert.Equal("#003970", draft["draft"]!["theme"]!["accent"]!.GetValue<string>());
    }

    [Fact]
    public async Task Survey_response_count_includes_anonymous_and_signed_in_submissions_across_versions()
    {
        var cookie = await OrganizerAsync(Permission.FormsManage.Value, Permission.ApplicationsView.Value);
        var eventId = await db.AddEventAsync();
        using var scope = _app.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<MorganHacks.Applications.Forms.IFormStore>();
        var form = await store.CreateAsync(eventId, "Survey", "survey", null);
        var other = await store.CreateAsync(eventId, "Other survey", "survey", null);
        await store.DraftAsync(form.Id, null);
        await store.DraftAsync(other.Id, null);
        var first = await store.PublishAsync(form.Id, null);
        await store.DraftAsync(form.Id, null);
        var second = await store.PublishAsync(form.Id, null);
        var unrelated = await store.PublishAsync(other.Id, null);
        var person = await db.AddPersonAsync($"response-{Guid.NewGuid():N}@example.com");
        await using var insert = db.DataSource.CreateCommand("""
            INSERT INTO applications.form_submissions (form_id, form_version, person_id, submission_key, answers)
            VALUES (@form, @first, @person, NULL, '{}'),
                   (@form, @second, NULL, @key, '{}'),
                   (@other, @unrelated, NULL, @otherKey, '{}')
            """);
        insert.Parameters.AddWithValue("form", form.Id);
        insert.Parameters.AddWithValue("first", first.Version);
        insert.Parameters.AddWithValue("second", second.Version);
        insert.Parameters.AddWithValue("person", person);
        insert.Parameters.AddWithValue("key", Guid.NewGuid());
        insert.Parameters.AddWithValue("other", other.Id);
        insert.Parameters.AddWithValue("unrelated", unrelated.Version);
        insert.Parameters.AddWithValue("otherKey", Guid.NewGuid());
        await insert.ExecuteNonQueryAsync();
        var draft = await ReadAsync(await Send(HttpMethod.Get, $"/admin/forms/{form.Id}/draft", cookie));
        Assert.Equal(2, draft["responseCount"]!.GetValue<long>());
    }

    // --------------------------------------------------------------- helpers ---

    /// <summary>A form on a fresh event, opened for editing.</summary>
    /// <remarks>
    /// A new event per test rather than one shared. These tests publish and
    /// retire forms, and an event carries a unique index allowing exactly one
    /// live application form — so sharing one would make every test depend on
    /// the order the others ran in.
    /// </remarks>
    private async Task<(string Cookie, Guid Form, JsonArray Fields)> OpenBuilderAsync()
    {
        var cookie = await OrganizerAsync(
            Permission.FormsManage.Value, Permission.ApplicationsView.Value);
        var form = await CreateFormAsync(cookie, await db.AddEventAsync());
        return (cookie, form, await DraftFieldsAsync(cookie, form));
    }

    private async Task<Guid> CreateFormAsync(string cookie, Guid eventId)
    {
        var response = await Send(HttpMethod.Post, $"/admin/forms?eventId={eventId}", cookie,
            new { name = "Application", kind = "application" });
        response.EnsureSuccessStatusCode();

        return (await ReadAsync(response))["id"]!.GetValue<Guid>();
    }

    private async Task<JsonArray> DraftFieldsAsync(string cookie, Guid form)
    {
        var response = await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", cookie);
        response.EnsureSuccessStatusCode();

        return (await ReadAsync(response))["draft"]!["fields"]!.AsArray().DeepClone().AsArray();
    }

    /// <summary>A question as the builder would send one.</summary>
    private static JsonNode Question(
        string key,
        string type = "shortText",
        string label = "A question",
        bool required = false,
        object[]? options = null) =>
        JsonSerializer.SerializeToNode(new
        {
            key,
            type,
            label,
            required,
            options = options ?? [],
            storage = "responses",
        })!;

    private static JsonNode? Field(JsonArray fields, string key) =>
        fields.FirstOrDefault(f => f!["key"]!.GetValue<string>() == key);

    private static void Remove(JsonArray fields, string key) =>
        fields.Remove(Field(fields, key));

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    private static async Task<IReadOnlyList<string?>> KeysOfProblems(
        HttpResponseMessage response) =>
        [.. (await ReadAsync(response))["problems"]!.AsArray()
            .Select(p => p!["fieldKey"]?.GetValue<string>())];

    private async Task<IReadOnlyList<string>> KeysOfDraft(string cookie, Guid form) =>
        [.. (await DraftFieldsAsync(cookie, form)).Select(f => f!["key"]!.GetValue<string>())];

    /// <summary>An organizer holding exactly the permissions named, and a session.</summary>
    private async Task<string> OrganizerAsync(params string[] permissions)
    {
        var id = await db.AddPersonAsync($"builder-{Guid.NewGuid():N}@example.com");
        foreach (var permission in permissions)
        {
            await db.GrantAsync(id, permission);
        }

        return await SignInAsync(id);
    }

    private async Task<string> TeamMemberAsync(string slug)
    {
        var id = await db.AddPersonAsync($"team-{Guid.NewGuid():N}@example.com");
        await db.AddToTeamAsync(id, slug);
        return await SignInAsync(id);
    }

    /// <summary>
    /// Mints a session directly rather than driving a login.
    /// </summary>
    /// <remarks>
    /// These tests are about what a session is permitted to do, not how it was
    /// obtained — and everyone here is an organizer, who signs in through
    /// Google rather than by magic link.
    /// </remarks>
    private async Task<string> SignInAsync(Guid personId)
    {
        using var scope = _app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return $"mh_session={await sessions.StartAsync(personId)}";
    }

    private Task<HttpResponseMessage> Send(
        HttpMethod method, string path, string cookie, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", cookie);

        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        return _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        }).SendAsync(request);
    }
}

/// <summary>Lets a set-up call read as one line without losing its assertion.</summary>
internal static class ResponseAssertions
{
    public static async Task EnsureSuccess(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            Assert.Fail(
                $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }
}
