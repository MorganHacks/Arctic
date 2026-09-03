using MorganHacks.Applications.Domain;
using Npgsql;
using NpgsqlTypes;

namespace MorganHacks.Applications.Segments;

/// <summary>
/// Resolves segments against <c>applications.*</c>.
/// </summary>
/// <remarks>
/// Every query here reads one more row than it is allowed to return, so the
/// caller can tell "exactly the maximum" from "more than the maximum" without
/// counting the whole table first. See <see cref="Segment.MaxRecipients"/> for
/// why there is a maximum at all.
/// </remarks>
public sealed class PostgresSegmentResolver(NpgsqlDataSource dataSource) : ISegmentResolver
{
    /// <summary>
    /// The select list every applicant query shares.
    /// </summary>
    /// <remarks>
    /// Built from <see cref="ApplicantColumns.Mergeable"/> rather than typed
    /// out, so a column becomes readable here by being declared there and by
    /// nothing else. The two queries below would otherwise be a second and a
    /// third place to remember, and the failure of forgetting one is an editor
    /// offering a placeholder that comes out blank for a whole segment.
    /// <para>
    /// Interpolated into SQL, which is worth being explicit about: every part
    /// of this string is a column name from a list declared in code, and a
    /// test asserts that every one of them is a real column of
    /// <c>applications.applications</c>. Nothing a caller sends reaches it.
    /// </para>
    /// <para>
    /// Withheld columns are not selected at all. A resume key that is never
    /// read out of the table cannot end up in a rendered body by accident.
    /// </para>
    /// </remarks>
    private static readonly string Selected = string.Join(
        ", ",
        new[] { "a.person_id" }.Concat(
            ApplicantColumns.Mergeable.Select(column => $"a.{column.Column}")));

    public Task<ResolvedSegment> ResolveAsync(
        Segment segment, CancellationToken ct = default) => segment switch
        {
            Segment.InStatus s => InStatusAsync(s, ct),
            Segment.FormRespondents s => RespondentsAsync(s, ct),
            Segment.Addresses s => Task.FromResult(Addresses(s)),
            _ => throw new ArgumentOutOfRangeException(nameof(segment), segment, null),
        };

    /// <summary>
    /// Everyone on one event whose application is in one of these states.
    /// </summary>
    /// <remarks>
    /// No <c>DISTINCT</c> and no dedupe pass, because the unique index on
    /// (event_id, lower(email)) already guarantees one row per address per
    /// event. That index is the reason one person cannot appear twice in a
    /// decision email, and it holds no matter what wrote the rows.
    /// </remarks>
    private async Task<ResolvedSegment> InStatusAsync(
        Segment.InStatus segment, CancellationToken ct)
    {
        var sql = $"""
            SELECT {Selected}
              FROM applications.applications a
             WHERE a.event_id = @eventId AND a.status = ANY(@statuses)
             ORDER BY a.email
             LIMIT @limit
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("eventId", segment.EventId);
        cmd.Parameters.Add(new NpgsqlParameter("statuses", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = segment.Statuses.Select(s => s.ToWire()).ToArray(),
        });
        cmd.Parameters.AddWithValue("limit", Segment.MaxRecipients + 1);

        return await ReadAsync(cmd, ct);
    }

    /// <summary>
    /// Everyone who submitted a given form.
    /// </summary>
    /// <remarks>
    /// Reached through the form's event, because an application carries a form
    /// version and not a form id. The <c>kind = 'application'</c> check is what
    /// keeps that honest: a survey sitting on the same event would otherwise
    /// resolve to the application form's respondents, and somebody would mail
    /// four hundred applicants a note meant for eleven mentors.
    /// <para>
    /// <c>submitted_at IS NOT NULL</c> rather than a status list. Somebody who
    /// opened the form and typed their name has a row — the form autosaves —
    /// and they have not answered anything.
    /// </para>
    /// </remarks>
    private async Task<ResolvedSegment> RespondentsAsync(
        Segment.FormRespondents segment, CancellationToken ct)
    {
        var sql = $"""
            SELECT {Selected}
              FROM applications.applications a
              JOIN applications.forms f ON f.event_id = a.event_id
             WHERE f.id = @formId
               AND f.kind = 'application'
               AND a.submitted_at IS NOT NULL
             ORDER BY a.email
             LIMIT @limit
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("formId", segment.FormId);
        cmd.Parameters.AddWithValue("limit", Segment.MaxRecipients + 1);

        return await ReadAsync(cmd, ct);
    }

    /// <summary>
    /// The addresses somebody typed, and nothing looked up about them.
    /// </summary>
    /// <remarks>
    /// No database round trip on purpose. Matching these against
    /// <c>identity.people</c> to fill in a person id would mean reading
    /// another module's table, and matching them against applicants would
    /// defeat the point — this segment exists for the people who are not in
    /// the applicant pool.
    /// <para>
    /// The consequence is that these messages carry no person id, so they do
    /// not appear in the "what have we sent you" history that is keyed on one.
    /// That is the correct answer for a sponsor contact and a small loss for
    /// an applicant somebody happened to paste in.
    /// </para>
    /// <para>
    /// An address fills in the one column
    /// <see cref="ApplicantColumn.OnAddressLists"/> marks and no other, so a
    /// template greeting a sponsor by name is refused before the send rather
    /// than mailed with the greeting still standing in it.
    /// </para>
    /// </remarks>
    private static ResolvedSegment Addresses(Segment.Addresses segment) =>
        new(segment.Emails.Select(email => new SegmentMember(
                null,
                email,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [ApplicantColumns.Address.Column] = email,
                })).ToList(),
            Overflowed: false);

    /// <summary>
    /// Reads the rows into members, one column of
    /// <see cref="ApplicantColumns.Mergeable"/> at a time.
    /// </summary>
    /// <remarks>
    /// By ordinal rather than by name, because <see cref="Selected"/> put them
    /// in this order and reading them back by name would be a second agreement
    /// to keep. The value is taken as the column holds it — string, int or
    /// bool — and how each of those reads in a sentence is decided in the mail
    /// rather than here.
    /// </remarks>
    private static async Task<ResolvedSegment> ReadAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var members = new List<SegmentMember>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < ApplicantColumns.Mergeable.Count; i++)
            {
                // Offset by one: person_id is the first thing Selected asks for.
                var ordinal = i + 1;
                fields[ApplicantColumns.Mergeable[i].Column] =
                    await reader.IsDBNullAsync(ordinal, ct) ? null : reader.GetValue(ordinal);
            }

            members.Add(new SegmentMember(
                await reader.IsDBNullAsync(0, ct) ? null : reader.GetGuid(0),

                // NOT NULL on the column, so this cast cannot be the thing that
                // fails; the divergence test is what keeps the column there.
                (string)fields[ApplicantColumns.Address.Column]!,
                fields));
        }

        if (members.Count > Segment.MaxRecipients)
        {
            return new ResolvedSegment([], Overflowed: true);
        }

        return new ResolvedSegment(members, Overflowed: false);
    }
}
