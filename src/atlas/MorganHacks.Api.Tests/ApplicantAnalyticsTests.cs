using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Applications.Data;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Forms;
using MorganHacks.Identity.Services;
using NpgsqlTypes;

namespace MorganHacks.Api.Tests;

public class ApplicantAnalyticsTests(ApplicationsDatabase db)
    : IClassFixture<ApplicationsDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;
    private static readonly DateOnly Today = new(2026, 9, 24);
    private PostgresFormStore Forms => new(db.DataSource);
    private PostgresApplicantAnalyticsStore Analytics => new(db.DataSource, Forms);

    public Task InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Postgres", db.ConnectionString));
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Counts_the_whole_event_and_normalizes_schools_without_incomplete_answers()
    {
        var eventId = await db.AddEventAsync();
        for (var index = 0; index < 201; index++)
            await Seed(eventId, school: index % 2 == 0 ? " Morgan  State University " : "morgan state university");
        await Seed(eventId, school: " ");
        await Seed(eventId, status: "incomplete", school: "Draft-only school");
        await Seed(await db.AddEventAsync(), school: "Another event");

        var result = await Analytics.ReadAsync(eventId, false, Today);

        Assert.Equal(203, result.TotalApplicants);
        Assert.Equal(202, result.SubmittedApplications);
        Assert.Equal(202, result.Statuses["submitted"]);
        Assert.Equal(1, result.Statuses["incomplete"]);
        Assert.Equal(201, Assert.Single(result.Schools, bucket => bucket.Label != "Not provided").Count);
        Assert.Equal(1, Assert.Single(result.Schools, bucket => bucket.Label == "Not provided").Count);
        Assert.Null(result.Demographics);
    }

    [Fact]
    public async Task Activity_uses_utc_dates_fills_gaps_and_excludes_dates_outside_ninety_days()
    {
        var eventId = await db.AddEventAsync();
        await Seed(eventId, at: DateTimeOffset.Parse("2026-09-23T23:59:59Z"));
        await Seed(eventId, at: DateTimeOffset.Parse("2026-09-24T00:00:00Z"));
        await Seed(eventId, at: DateTimeOffset.Parse("2026-06-26T00:00:00Z"));
        await Seed(eventId, at: DateTimeOffset.Parse("2026-06-25T23:59:59Z"));
        await Seed(eventId, at: DateTimeOffset.Parse("2026-09-25T00:00:00Z"));
        await Seed(eventId, status: "incomplete", at: DateTimeOffset.Parse("2026-09-24T01:00:00Z"));

        var result = await Analytics.ReadAsync(eventId, false, Today);

        Assert.Equal(90, result.Activity.Count);
        Assert.Equal(new DateOnly(2026, 6, 27), result.Activity[0].Date);
        Assert.Equal(Today, result.Activity[^1].Date);
        Assert.Equal(3, result.Activity.Sum(day => day.Started));
        Assert.Equal(2, result.Activity.Sum(day => day.Submitted));
        Assert.Equal(new ApplicationActivity(Today, 2, 1), result.Activity[^1]);
        Assert.Equal(0, result.Activity[1].Started);
        Assert.Equal(5, result.SubmittedApplications);
    }

    [Fact]
    public async Task Gender_uses_each_published_versions_question_and_option_labels()
    {
        var eventId = await db.AddEventAsync();
        var form = await Forms.CreateAsync(eventId, "Application", "application", null);
        var draft = await Forms.DraftAsync(form.Id, null);
        var question = new FormField
        {
            Key = "question_123",
            Type = FieldType.Radio,
            Label = "What is your gender?",
            Options = [new("a", "Male"), new("b", "Female"), new("c", "Non-binary"), new("d", "Prefer not to say")]
        };
        await Forms.SaveDraftAsync(form.Id, [.. draft.Fields, question]);
        var first = await Forms.PublishAsync(form.Id, null);
        await Seed(eventId, version: first.Version, responses: """{"question_123":"a"}""", firstTime: true);
        await Seed(eventId, version: first.Version, responses: """{"question_123":"c"}""", firstTime: false);
        await Seed(eventId, version: first.Version, responses: """{"question_123":"d"}""");
        await Seed(eventId, version: first.Version);
        await Seed(eventId, version: first.Version, responses: """{"question_123":"b"}""", status: "incomplete");

        draft = await Forms.DraftAsync(form.Id, null);
        await Forms.SaveDraftAsync(form.Id, draft.Fields.Select(field => field.Key == question.Key
            ? field with { Options = [new("a", "Female")] } : field).ToArray());
        var second = await Forms.PublishAsync(form.Id, null);
        await Seed(eventId, version: second.Version, responses: """{"question_123":"a"}""");

        var result = await Analytics.ReadAsync(eventId, true, Today);
        var demographics = Assert.IsType<ApplicantDemographics>(result.Demographics);

        Assert.True(demographics.GenderCollected);
        Assert.Equal(5, demographics.Gender.Sum(bucket => bucket.Count));
        Assert.Equal(1, demographics.Gender.Single(bucket => bucket.Label == "Men").Count);
        Assert.Equal(1, demographics.Gender.Single(bucket => bucket.Label == "Women").Count);
        Assert.Equal(1, demographics.Gender.Single(bucket => bucket.Label == "Non-binary").Count);
        Assert.Equal(1, demographics.Gender.Single(bucket => bucket.Label == "Prefer not to answer").Count);
        Assert.Equal(1, demographics.Gender.Single(bucket => bucket.Label == "Not provided").Count);
        Assert.Equal(1, demographics.Experience.Single(bucket => bucket.Label == "First hackathon").Count);
        Assert.Equal(1, demographics.Experience.Single(bucket => bucket.Label == "Returning hacker").Count);
        Assert.Equal(3, demographics.Experience.Single(bucket => bucket.Label == "Not provided").Count);
        Assert.Equal(5, demographics.Education.Sum(bucket => bucket.Count));
    }

    [Fact]
    public async Task Does_not_treat_unrelated_answers_or_names_as_gender()
    {
        var eventId = await db.AddEventAsync();
        var form = await Forms.CreateAsync(eventId, "Application", "application", null);
        await Forms.DraftAsync(form.Id, null);
        await Forms.PublishAsync(form.Id, null);
        await Seed(eventId, responses: """{"gender":"female","private_note":"private response"}""");

        var result = await Analytics.ReadAsync(eventId, true, Today);

        Assert.False(result.Demographics!.GenderCollected);
        Assert.Equal(new AnalyticsBucket("Not provided", 1), Assert.Single(result.Demographics.Gender));
    }

    [Fact]
    public async Task Empty_event_returns_zeroes_and_an_empty_daily_series()
    {
        var result = await Analytics.ReadAsync(await db.AddEventAsync(), true, Today);
        Assert.Equal(0, result.TotalApplicants);
        Assert.Equal(0, result.SubmittedApplications);
        Assert.Empty(result.Schools);
        Assert.Empty(result.Statuses);
        Assert.Empty(result.Demographics!.Gender);
        Assert.All(result.Activity, day => Assert.Equal((0, 0), (day.Started, day.Submitted)));
    }

    [Fact]
    public async Task Route_requires_an_authenticated_applicant_viewer()
    {
        using var client = _app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/admin/analytics/applicants")).StatusCode);
        var personId = await db.AddPersonAsync($"analytics-{Guid.NewGuid():N}@example.test");
        client.DefaultRequestHeaders.Add("Cookie", await SignIn(personId));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/analytics/applicants")).StatusCode);
    }

    [Fact]
    public async Task Route_restricts_demographics_and_never_returns_individual_answers()
    {
        var eventId = await db.AddEventAsync();
        await Seed(eventId, responses: """{"private_note":"private response"}""");
        var personId = await db.AddPersonAsync($"analytics-{Guid.NewGuid():N}@example.test");
        await db.GrantAsync(personId, "applications.view");
        using var client = _app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", await SignIn(personId));
        using var first = await client.GetAsync($"/admin/analytics/applicants?eventId={eventId}");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.False(firstJson.RootElement.GetProperty("canViewResponses").GetBoolean());
        Assert.Equal(JsonValueKind.Null, firstJson.RootElement.GetProperty("analytics").GetProperty("demographics").ValueKind);

        await db.GrantAsync(personId, "applications.view_responses");
        using var second = await client.GetAsync($"/admin/analytics/applicants?eventId={eventId}");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        using var secondJson = JsonDocument.Parse(body);
        Assert.True(secondJson.RootElement.GetProperty("canViewResponses").GetBoolean());
        Assert.Equal(1, secondJson.RootElement.GetProperty("analytics").GetProperty("submittedApplications").GetInt32());
        Assert.DoesNotContain("private response", body);
        Assert.DoesNotContain("@example.test", body);
        Assert.DoesNotContain("Ada", body);
    }

    [Fact]
    public async Task Unknown_event_does_not_fall_back_to_another_events_data()
    {
        var personId = await db.AddPersonAsync($"analytics-{Guid.NewGuid():N}@example.test");
        await db.GrantAsync(personId, "applications.view");
        using var client = _app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", await SignIn(personId));
        var response = await client.GetAsync($"/admin/analytics/applicants?eventId={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<string> SignIn(Guid personId)
    {
        using var scope = _app.Services.CreateScope();
        return $"mh_session={await scope.ServiceProvider.GetRequiredService<SessionService>().StartAsync(personId)}";
    }

    private async Task Seed(Guid eventId, string school = "Morgan State University", string status = "submitted",
        DateTimeOffset? at = null, int version = 1, string responses = "{}", bool? firstTime = null)
    {
        await using var command = db.DataSource.CreateCommand("""
            INSERT INTO applications.applications
              (event_id, email, first_name, last_name, school, status, created_at, submitted_at,
               age, phone, level_of_study, country, mlh_coc_agreed_at, mlh_data_sharing_at,
               form_version, responses, first_time_hacker)
            VALUES (@event, @email, 'Ada', 'Lovelace', @school, @status, @at,
                    CASE WHEN @status = 'incomplete' THEN NULL ELSE @at END,
                    20, '+15550000000', 'undergraduate', 'United States', now(), now(),
                    @version, @responses, @firstTime)
            """);
        command.Parameters.AddWithValue("event", eventId);
        command.Parameters.AddWithValue("email", $"applicant-{Guid.NewGuid():N}@example.test");
        command.Parameters.AddWithValue("school", school);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("at", (at ?? new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero)).UtcDateTime);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("responses", NpgsqlDbType.Jsonb, responses);
        command.Parameters.AddWithValue("firstTime", NpgsqlDbType.Boolean, (object?)firstTime ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }
}
