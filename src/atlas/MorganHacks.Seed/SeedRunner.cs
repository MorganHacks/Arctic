using System.Text.Json;
using MorganHacks.Applications.Data;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Forms;
using Npgsql;

namespace MorganHacks.Seed;

public sealed record SeedResult(int Created, int Skipped);

/// <summary>Organizer fixtures only. No API or email dependencies, and no schema changes.</summary>
public sealed class SeedRunner(NpgsqlDataSource source)
{
    public static readonly Guid EventId = Guid.Parse("028c6934-b837-44e4-9b40-076083d126ae");
    public const string EventName = "MOCK — Organizer simulation";
    private const string Slug = "mock-organizer-simulation-v1";
    private const long LockId = 728349201;

    public async Task<SeedResult> RunAsync(int count)
    {
        if (count is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(count));
        // Keep a session lock for the whole run even though stores use separate transactions.
        await using var connection = await source.OpenConnectionAsync();
        await using var takeLock = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
        takeLock.Parameters.AddWithValue("key", LockId);
        if (await takeLock.ExecuteScalarAsync() is not true)
            throw new InvalidOperationException("Another seed run is active. Try again when it finishes.");
        try { return await SeedAsync(count); }
        finally
        {
            await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
            release.Parameters.AddWithValue("key", LockId);
            await release.ExecuteScalarAsync();
        }
    }

    private async Task<SeedResult> SeedAsync(int count)
    {
        // Never adopt an arbitrary existing event. Both reserved identifiers must match.
        await using (var check = source.CreateCommand(
            "SELECT id, slug FROM applications.events WHERE id = @id OR slug = @slug"))
        {
            check.Parameters.AddWithValue("id", EventId);
            check.Parameters.AddWithValue("slug", Slug);
            await using var reader = await check.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                if (reader.GetGuid(0) != EventId || reader.GetString(1) != Slug)
                    throw new InvalidOperationException("The reserved mock event conflicts with existing data. Refusing writes.");
        }
        await using (var create = source.CreateCommand("""
            INSERT INTO applications.events (id, slug, name, capacity)
            VALUES (@id, @slug, @name, 100) ON CONFLICT (id) DO NOTHING
            """))
        {
            create.Parameters.AddWithValue("id", EventId);
            create.Parameters.AddWithValue("slug", Slug);
            create.Parameters.AddWithValue("name", EventName);
            await create.ExecuteNonQueryAsync();
        }
        var forms = new PostgresFormStore(source);
        var form = (await forms.ForEventAsync(EventId)).SingleOrDefault(f => f.IsApplication)
            ?? await forms.CreateAsync(EventId, "MOCK application", "application", null);
        var version = await forms.PublishedAsync(form.Id);
        if (version is null)
        {
            await forms.DraftAsync(form.Id, null);
            await forms.SaveDraftAsync(form.Id, Fields);
            version = await forms.PublishAsync(form.Id, null);
        }
        // Validate every answer before inserting applicants. Edited fixture forms fail clearly.
        for (var i = 1; i <= count; i++)
        {
            var problems = SubmissionValidation.Check(version.Fields, Answers(i));
            if (problems.Count != 0 || version.Fields.Count != Fields.Count
                || !version.Fields.Select(f => (f.Key, f.Type, f.Storage, f.Column))
                    .SequenceEqual(Fields.Select(f => (f.Key, f.Type, f.Storage, f.Column))))
                throw new InvalidOperationException("The mock form has changed and no longer matches these fixtures. No applicants were added.");
        }

        var submissions = new PostgresSubmissionStore(source);
        var applications = new PostgresApplicationStore(source);
        var created = 0;
        var skipped = 0;
        for (var i = 1; i <= count; i++)
        {
            var email = Email(i);
            await using var exists = source.CreateCommand(
                "SELECT EXISTS (SELECT 1 FROM applications.applications WHERE event_id=@event AND lower(email)=@email)");
            exists.Parameters.AddWithValue("event", EventId);
            exists.Parameters.AddWithValue("email", email);
            if (await exists.ExecuteScalarAsync() is true) { skipped++; continue; }

            var path = Paths[(i - 1) % Paths.Length];
            Guid? id = null;
            try
            {
                id = path.Length == 0
                    ? await applications.StartAsync(EventId, email)
                    : await submissions.SubmitApplicationAsync(form, version, Answers(i));
                foreach (var status in path.Skip(1))
                    await applications.TransitionAsync(id.Value, status, reason: "Synthetic organizer simulation");
                // A coherent relative timeline gives the dashboard real date buckets without
                // disabling triggers. Only this freshly created application's dates are shifted.
                await using var dates = source.CreateCommand("""
                    UPDATE applications.applications SET
                        created_at = @started, started_at = @started,
                        submitted_at = CASE WHEN submitted_at IS NOT NULL THEN @started + interval '1 hour' END,
                        decided_at = CASE WHEN decided_at IS NOT NULL THEN @started + interval '3 hours' END,
                        confirmed_at = CASE WHEN confirmed_at IS NOT NULL THEN @started + interval '4 hours' END,
                        declined_at = CASE WHEN declined_at IS NOT NULL THEN @started + interval '4 hours' END,
                        checked_in_at = CASE WHEN checked_in_at IS NOT NULL THEN @started + interval '5 hours' END,
                        mlh_coc_agreed_at = CASE WHEN mlh_coc_agreed_at IS NOT NULL THEN @started + interval '1 hour' END,
                        mlh_data_sharing_at = CASE WHEN mlh_data_sharing_at IS NOT NULL THEN @started + interval '1 hour' END,
                        rsvp_deadline = CASE WHEN status = 'accepted' THEN now() + interval '7 days'
                                            WHEN status = 'expired' THEN @started + interval '3 hours 30 minutes' END
                    WHERE id=@id AND event_id=@event;
                    UPDATE applications.status_history
                    SET created_at = @started + interval '1 hour' * CASE to_status
                        WHEN 'incomplete' THEN 0 WHEN 'submitted' THEN 1
                        WHEN 'under_review' THEN 2 WHEN 'withdrawn' THEN 2
                        WHEN 'accepted' THEN 3 WHEN 'rejected' THEN 3 WHEN 'waitlisted' THEN 3
                        WHEN 'checked_in' THEN 5 ELSE 4 END
                    WHERE application_id=@id;
                    """);
                dates.Parameters.AddWithValue("id", id.Value);
                dates.Parameters.AddWithValue("event", EventId);
                dates.Parameters.AddWithValue("started", DateTimeOffset.UtcNow.AddDays(-(i % 30 + 2)));
                await dates.ExecuteNonQueryAsync();
                created++;
            }
            catch
            {
                // A failed row can be retried. Earlier completed applicants remain untouched.
                if (id is not null)
                {
                    await using var undo = source.CreateCommand(
                        "DELETE FROM applications.applications WHERE id=@id AND event_id=@event");
                    undo.Parameters.AddWithValue("id", id.Value);
                    undo.Parameters.AddWithValue("event", EventId);
                    await undo.ExecuteNonQueryAsync();
                }
                throw;
            }
        }
        return new(created, skipped);
    }

    private static readonly ApplicationStatus[][] Paths =
    [
        [],
        [ApplicationStatus.Submitted],
        [ApplicationStatus.Submitted, ApplicationStatus.UnderReview],
        [ApplicationStatus.Submitted, ApplicationStatus.UnderReview, ApplicationStatus.Accepted],
        [ApplicationStatus.Submitted, ApplicationStatus.UnderReview, ApplicationStatus.Waitlisted],
        [ApplicationStatus.Submitted, ApplicationStatus.UnderReview, ApplicationStatus.Rejected],
        [ApplicationStatus.Submitted, ApplicationStatus.UnderReview, ApplicationStatus.Accepted, ApplicationStatus.Confirmed],
        [ApplicationStatus.Submitted, ApplicationStatus.UnderReview, ApplicationStatus.Accepted, ApplicationStatus.Declined],
        [ApplicationStatus.Submitted, ApplicationStatus.UnderReview, ApplicationStatus.Accepted, ApplicationStatus.Expired],
        [ApplicationStatus.Submitted, ApplicationStatus.UnderReview, ApplicationStatus.Accepted, ApplicationStatus.Confirmed, ApplicationStatus.CheckedIn],
        [ApplicationStatus.Submitted, ApplicationStatus.Withdrawn],
    ];

    public static readonly IReadOnlyList<FormField> Fields =
    [
        ..StartingQuestions.All,
        Column("graduation_year", FieldType.Number, "Graduation year"),
        Column("first_time_hacker", FieldType.Consent, "Is this your first hackathon?"),
        Column("shirt_size", FieldType.ShortText, "Shirt size"),
        Column("dietary_needs", FieldType.ShortText, "Dietary needs"),
        new() { Key="major", Label="Major", Type=FieldType.ShortText },
        new() { Key="motivation", Label="What would you like to build?", Type=FieldType.Paragraph },
    ];
    private static FormField Column(string key, FieldType type, string label) =>
        new() { Key = key, Label = label, Type = type, Storage = AnswerStorage.Column, Column = key };
    public static string Email(int i) => $"mock-organizer-{i:D4}@example.com";
    public static Dictionary<string, JsonElement> Answers(int i)
    {
        string[] first = ["Amara", "Jordan", "José", "Mei", "Aaliyah", "Chinedu", "Sam", "Fatima", "Alex", "Zoë"];
        string[] last = ["Johnson", "O’Connor", "Okafor", "Chen", "Williams", "Patel", "García", "Smith"];
        string[] schools = ["Morgan State University", "Howard University", "UMBC", "Towson University", "University of Maryland", "Johns Hopkins University"];
        string[] majors = ["Computer Science", "Information Systems", "Engineering", "Design", "Biology"];
        string[] projects = ["an accessible campus navigation app", "a study group matching tool", "a food pantry inventory dashboard", "a community volunteering platform"];
        var values = new Dictionary<string, object>
        {
            ["email"] = Email(i),
            ["first_name"] = first[(i - 1) % first.Length],
            ["last_name"] = last[(i - 1) % last.Length],
            ["age"] = 18 + i % 9,
            ["phone"] = $"202-555-{100 + i % 100:D4}",
            ["school"] = schools[(i - 1) % schools.Length],
            ["country"] = "United States",
            ["level_of_study"] = i % 7 == 0 ? "graduate" : "undergraduate-3y",
            ["mlh_coc_agreed_at"] = true,
            ["mlh_data_sharing_at"] = true,
            ["mlh_marketing_opt_in"] = i % 3 == 0,
            ["graduation_year"] = 2026 + i % 5,
            ["first_time_hacker"] = i % 2 == 0,
            ["shirt_size"] = new[] { "S", "M", "L", "XL", "2XL" }[i % 5],
            ["major"] = majors[i % majors.Length],
            ["motivation"] = $"[Synthetic application] I would like to build {projects[i % projects.Length]}. "
                + (i % 2 == 0 ? "This is my first hackathon and I want to learn with a team." : "I have built small projects before and want to practice shipping a useful prototype."),
        };
        if (i % 3 != 0) values["dietary_needs"] = i % 2 == 0 ? "Vegetarian" : "Halal";
        return values.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value));
    }
}
