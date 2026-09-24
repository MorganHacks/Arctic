using System.Globalization;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Forms;
using Npgsql;
using NpgsqlTypes;

namespace MorganHacks.Applications.Data;

public sealed class PostgresApplicantAnalyticsStore(NpgsqlDataSource dataSource, IFormStore forms)
{
    public async Task<ApplicantAnalytics> ReadAsync(
        Guid eventId, bool includeResponses, DateOnly today, CancellationToken ct = default)
    {
        const string sql = """
            WITH cohort AS MATERIALIZED (
                SELECT status, school, created_at, submitted_at
                  FROM applications.applications WHERE event_id = @eventId
            )
            SELECT 'status', status, count(*) FROM cohort GROUP BY status
            UNION ALL
            SELECT 'submitted', '', count(*) FROM cohort WHERE submitted_at IS NOT NULL
            UNION ALL
            SELECT 'school', min(regexp_replace(btrim(school), '\s+', ' ', 'g')), count(*)
              FROM cohort WHERE submitted_at IS NOT NULL
             GROUP BY lower(regexp_replace(btrim(school), '\s+', ' ', 'g'))
            UNION ALL
            SELECT 'started', (created_at AT TIME ZONE 'UTC')::date::text, count(*)
              FROM cohort WHERE created_at >= @from AND created_at < @until
             GROUP BY (created_at AT TIME ZONE 'UTC')::date
            UNION ALL
            SELECT 'daily_submitted', (submitted_at AT TIME ZONE 'UTC')::date::text, count(*)
              FROM cohort WHERE submitted_at >= @from AND submitted_at < @until
             GROUP BY (submitted_at AT TIME ZONE 'UTC')::date
            """;
        var from = today.AddDays(-89);
        var statuses = new Dictionary<string, int>();
        var schools = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var started = new Dictionary<DateOnly, int>();
        var submitted = new Dictionary<DateOnly, int>();
        var totalSubmitted = 0;

        await using (var command = dataSource.CreateCommand(sql))
        {
            command.Parameters.AddWithValue("eventId", eventId);
            command.Parameters.AddWithValue("from", from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            command.Parameters.AddWithValue("until", today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var label = reader.IsDBNull(1) ? "Not provided" : reader.GetString(1);
                var count = checked((int)reader.GetInt64(2));
                switch (reader.GetString(0))
                {
                    case "status": statuses[label] = count; break;
                    case "submitted": totalSubmitted = count; break;
                    case "school": Add(schools, string.IsNullOrWhiteSpace(label) ? "Not provided" : label, count); break;
                    case "started": started[DateOnly.Parse(label, CultureInfo.InvariantCulture)] = count; break;
                    case "daily_submitted": submitted[DateOnly.Parse(label, CultureInfo.InvariantCulture)] = count; break;
                }
            }
        }

        var activity = Enumerable.Range(0, 90).Select(offset =>
        {
            var date = from.AddDays(offset);
            return new ApplicationActivity(date, started.GetValueOrDefault(date), submitted.GetValueOrDefault(date));
        }).ToArray();

        var demographics = includeResponses ? await DemographicsAsync(eventId, ct) : null;
        return new ApplicantAnalytics(statuses.Values.Sum(), totalSubmitted, statuses, Buckets(schools), activity, demographics);
    }

    private async Task<ApplicantDemographics> DemographicsAsync(Guid eventId, CancellationToken ct)
    {
        var applicationForm = (await forms.ForEventAsync(eventId, ct)).FirstOrDefault(form => form.IsApplication);
        var versions = applicationForm is null ? [] : (await forms.HistoryAsync(applicationForm.Id, ct))
            .Where(version => version.Status != "draft").ToArray();
        var genderFields = versions.Select(version => (version.Version, Field: AnalyticsAnswers.GenderField(version.Fields)))
            .Where(pair => pair.Field is not null).ToDictionary(pair => pair.Version, pair => pair.Field!);
        var educationFields = versions.ToDictionary(version => version.Version,
            version => version.Fields.FirstOrDefault(field => field.Storage == AnswerStorage.Column && field.Column == "level_of_study"));
        var gender = new Dictionary<string, int>();
        var education = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var experience = new Dictionary<string, int>();
        const string sql = """
            WITH cohort AS MATERIALIZED (
                SELECT form_version, responses, level_of_study, first_time_hacker
                  FROM applications.applications
                 WHERE event_id = @eventId AND submitted_at IS NOT NULL
            ), gender_keys AS (
                SELECT * FROM unnest(@versions, @keys) AS mapping(version, key)
            )
            SELECT 'gender', a.form_version,
                   CASE WHEN jsonb_typeof(a.responses -> g.key) = 'string' THEN a.responses ->> g.key END,
                   count(*)
              FROM cohort a LEFT JOIN gender_keys g ON g.version = a.form_version
             GROUP BY a.form_version, g.key, CASE WHEN jsonb_typeof(a.responses -> g.key) = 'string' THEN a.responses ->> g.key END
            UNION ALL
            SELECT 'education', form_version, level_of_study, count(*) FROM cohort GROUP BY form_version, level_of_study
            UNION ALL
            SELECT 'experience', 0,
                   CASE first_time_hacker WHEN true THEN 'First hackathon' WHEN false THEN 'Returning hacker' ELSE 'Not provided' END,
                   count(*) FROM cohort GROUP BY first_time_hacker
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("eventId", eventId);
        command.Parameters.AddWithValue("versions", NpgsqlDbType.Array | NpgsqlDbType.Integer, genderFields.Keys.ToArray());
        command.Parameters.AddWithValue("keys", NpgsqlDbType.Array | NpgsqlDbType.Text, genderFields.Values.Select(field => field.Key).ToArray());
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var version = reader.GetInt32(1);
            var value = reader.IsDBNull(2) ? null : reader.GetString(2);
            var count = checked((int)reader.GetInt64(3));
            switch (reader.GetString(0))
            {
                case "gender": Add(gender, AnalyticsAnswers.Gender(value, genderFields.GetValueOrDefault(version)), count); break;
                case "education": Add(education, AnalyticsAnswers.OptionLabel(value, educationFields.GetValueOrDefault(version)), count); break;
                case "experience": Add(experience, value ?? "Not provided", count); break;
            }
        }
        return new ApplicantDemographics(genderFields.Count > 0, Buckets(gender), Buckets(education), Buckets(experience));
    }

    private static void Add(Dictionary<string, int> counts, string label, int count) =>
        counts[label] = counts.GetValueOrDefault(label) + count;

    private static AnalyticsBucket[] Buckets(Dictionary<string, int> counts) => counts
        .OrderBy(pair => pair.Key == "Not provided").ThenByDescending(pair => pair.Value).ThenBy(pair => pair.Key)
        .Select(pair => new AnalyticsBucket(pair.Key, pair.Value)).ToArray();
}
