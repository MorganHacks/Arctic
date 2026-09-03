using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Services;
using Npgsql;

namespace MorganHacks.Applications.Data;

/// <summary>
/// The organizers' read side of <c>applications.applications</c>, and notes.
/// </summary>
/// <remarks>
/// Reads the same table <see cref="PostgresApplicationStore"/> writes, and
/// deliberately cannot write a status. There is one way to change one and it
/// is <see cref="IApplicationStore.TransitionAsync"/>, where the lifecycle
/// check and the settings the history trigger reads both live.
/// </remarks>
public sealed class PostgresApplicantStore(NpgsqlDataSource dataSource) : IApplicantStore
{
    /// <summary>
    /// The columns an applicant is built from, in the order
    /// <see cref="Read"/> expects them.
    /// </summary>
    /// <remarks>
    /// <c>resume_key IS NOT NULL</c> rather than the key itself. The key never
    /// leaves this module, and a select list that does not name it is a
    /// stronger guarantee than a mapper that remembers not to copy it.
    /// </remarks>
    private const string Columns = """
        id, event_id, email, first_name, last_name, school, status, form_version,
        created_at, submitted_at, decided_at, rsvp_deadline, confirmed_at,
        declined_at, checked_in_at, resume_key IS NOT NULL
        """;

    public async Task<ApplicantPage> PageAsync(
        ApplicantSearch search,
        ApplicantCursor? after,
        int limit,
        CancellationToken ct = default)
    {
        var statuses = search.Statuses ?? [];
        var text = Fragment(search.Text);

        // Built by concatenation rather than by one statement with every
        // predicate always present. A `(@q IS NULL OR ...)` clause reads as
        // tidier and plans as worse: the planner has to pick one plan that
        // works whether or not the search is there, and the one it picks is
        // the scan.
        //
        // Every fragment below is a constant in this file. The only things
        // that vary are bound parameters.
        var sql = $"SELECT {Columns} FROM applications.applications WHERE event_id = @eventId"
                  + (text is null ? string.Empty : Matching)
                  + (statuses.Count == 0 ? string.Empty : " AND status = ANY(@statuses)")
                  + (after is null ? string.Empty : " AND (created_at, id) < (@at, @after)")
                  + " ORDER BY created_at DESC, id DESC LIMIT @limit";

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("eventId", search.EventId);

        // One more than asked for, so "is there another page" is answered by
        // what came back rather than by a second count(*) over the same
        // predicate. The extra row is discarded below and never reaches a
        // caller.
        cmd.Parameters.AddWithValue("limit", limit + 1);

        if (text is not null)
        {
            cmd.Parameters.AddWithValue("q", text);
        }

        if (statuses.Count > 0)
        {
            cmd.Parameters.AddWithValue(
                "statuses", statuses.Select(s => s.ToWire()).ToArray());
        }

        if (after is { } cursor)
        {
            // A row-value comparison rather than the usual
            // "created_at < @at OR (created_at = @at AND id < @after)". They
            // mean the same thing and only this one can use the index as a
            // single range scan.
            cmd.Parameters.AddWithValue("at", cursor.CreatedAt);
            cmd.Parameters.AddWithValue("after", cursor.Id);
        }

        var rows = new List<Applicant>(limit + 1);
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                rows.Add(Read(reader));
            }
        }

        var more = rows.Count > limit;
        var items = more ? rows[..limit] : rows;

        return new ApplicantPage(
            items,
            more ? new ApplicantCursor(items[^1].CreatedAt, items[^1].Id) : null);
    }

    public async Task<Applicant?> ByIdAsync(Guid applicationId, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            $"SELECT {Columns} FROM applications.applications WHERE id = @id");
        cmd.Parameters.AddWithValue("id", applicationId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    public async Task<IReadOnlyDictionary<ApplicationStatus, int>> CountsAsync(
        Guid eventId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT status, count(*)
              FROM applications.applications
             WHERE event_id = @eventId
             GROUP BY status
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("eventId", eventId);

        var counts = new Dictionary<ApplicationStatus, int>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            counts[ApplicationStatuses.Parse(reader.GetString(0))] = (int)reader.GetInt64(1);
        }

        return counts;
    }

    public async Task<IReadOnlyList<ApplicantNote>> NotesOfAsync(
        Guid applicationId, CancellationToken ct = default)
    {
        // Oldest first, unlike everything else here. A note thread is read as
        // a conversation rather than scanned for the newest thing, and one
        // that runs backwards has to be read backwards to make sense.
        const string sql = """
            SELECT id, author_id, body, created_at
              FROM applications.notes
             WHERE application_id = @id
             ORDER BY created_at, id
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", applicationId);

        var notes = new List<ApplicantNote>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            notes.Add(new ApplicantNote(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return notes;
    }

    /// <inheritdoc />
    public async Task<ApplicantNote?> AddNoteAsync(
        Guid applicationId, Guid authorId, string body, CancellationToken ct = default)
    {
        // SELECT rather than VALUES, so an id naming no application inserts
        // nothing and returns nothing. The alternative is the foreign key
        // raising, which is the same answer delivered as a 500.
        const string sql = """
            INSERT INTO applications.notes (application_id, author_id, body)
            SELECT @id, @author, @body
              FROM applications.applications WHERE id = @id
            RETURNING id, author_id, body, created_at
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", applicationId);
        cmd.Parameters.AddWithValue("author", authorId);
        cmd.Parameters.AddWithValue("body", body);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new ApplicantNote(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetFieldValue<DateTimeOffset>(3));
    }

    /// <summary>
    /// Where the search text is allowed to match.
    /// </summary>
    /// <remarks>
    /// The joined name is in the list because people type one. Searching
    /// "ada lovelace" against a first name and a last name separately matches
    /// neither, and the reader's conclusion is that the applicant is not
    /// there.
    /// <para>
    /// ILIKE rather than lower() on both sides: it is the same comparison and
    /// it says what it is doing. Neither can use an index with a leading
    /// wildcard, so there is nothing to lose by the readable one — see
    /// 0016_applicant_list.sql for why that is acceptable and what the fix is
    /// when it stops being.
    /// </para>
    /// </remarks>
    private const string Matching = """
         AND (email ILIKE @q ESCAPE '\'
              OR coalesce(first_name, '') ILIKE @q ESCAPE '\'
              OR coalesce(last_name, '') ILIKE @q ESCAPE '\'
              OR coalesce(first_name, '') || ' ' || coalesce(last_name, '')
                 ILIKE @q ESCAPE '\')
        """;

    /// <summary>
    /// The search text as a LIKE pattern, or null when there is nothing to
    /// search for.
    /// </summary>
    /// <remarks>
    /// The wildcards a person typed are escaped rather than honoured. Somebody
    /// searching for an address with an underscore in it means the character,
    /// and a bare <c>%</c> arriving from a search box is a request to match
    /// every applicant on the event through a predicate that cannot use an
    /// index.
    /// <para>
    /// The backslash goes first, or escaping the other two would double back
    /// over the escapes just written.
    /// </para>
    /// </remarks>
    private static string? Fragment(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var escaped = text.Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

        return $"%{escaped}%";
    }

    private static Applicant Read(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        ApplicationStatuses.Parse(reader.GetString(6)),
        reader.GetInt32(7),
        reader.GetFieldValue<DateTimeOffset>(8),
        reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
        reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
        reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
        reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
        reader.IsDBNull(13) ? null : reader.GetFieldValue<DateTimeOffset>(13),
        reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14),
        reader.GetBoolean(15));
}
