namespace MorganHacks.Audit;

/// <summary>
/// One recorded change to what somebody may do.
/// </summary>
/// <remarks>
/// Ids and slugs only. No address, no name, no phone number — the table holds
/// none, and this type is the shape anything reading it sees, so a screen or
/// an export built on it cannot leak what was never recorded.
/// </remarks>
/// <param name="Id">
/// Also the sort key. Several entries from one admin action share an
/// <paramref name="OccurredAt"/> to the microsecond, because that is
/// transaction start time; this is what orders them the way they happened.
/// </param>
/// <param name="Action">
/// <c>noun.verb</c>, past tense: <c>grant.added</c>, <c>team.left</c>,
/// <c>person.revoked</c>. Left as a string rather than an enum because the
/// database is what writes it, and a C# enum that fell behind the triggers
/// would throw on reading a row it did not recognise — the one row somebody
/// was looking for.
/// </param>
/// <param name="ActorId">
/// Null where nobody was behind it. The seed, an import, a fix run in psql:
/// all real, all with no person, and all better recorded as null than as a
/// guess.
/// </param>
/// <param name="SubjectId">
/// Whose access changed. Null exactly when <paramref name="SubjectTeam"/> is
/// set.
/// </param>
/// <param name="SubjectTeam">
/// Set instead of <paramref name="SubjectId"/> when a team baseline changed,
/// which changes what everybody on that team may do without touching a single
/// person's row.
/// </param>
/// <param name="Target">
/// The team slug or permission string that changed. Null where the action is
/// about the person themselves.
/// </param>
/// <param name="ExpiresAt">
/// When the access this entry describes lapses. Half the answer to "why can
/// they still do this": a membership added with an expiry and one added
/// without are different decisions.
/// </param>
/// <param name="Detail">
/// Anything the action needs beyond the columns, as raw JSON — the previous
/// expiry on a retiming, the <c>granted_by</c> the grants table recorded.
/// Kept as a string rather than parsed, because this library has no opinion
/// about what a caller does with it.
/// </param>
public sealed record AuditEntry(
    long Id,
    DateTimeOffset OccurredAt,
    string Action,
    Guid? ActorId,
    Guid? SubjectId,
    string? SubjectTeam,
    string? Target,
    DateTimeOffset? ExpiresAt,
    string Detail);

/// <summary>
/// Which slice of the trail to read.
/// </summary>
/// <remarks>
/// The two filters are the two questions anybody actually asks: "what happened
/// to this person" during an access review, and "what has this person been
/// doing" during an incident. Both are supported together, because the second
/// question is usually asked about somebody who appears in the answer to the
/// first.
/// </remarks>
/// <param name="Before">
/// Reads the page of entries older than this id. A cursor rather than an
/// offset: the table only ever grows at the newest end, so an offset would
/// re-read rows as new ones arrive, and paging through an incident would show
/// the same entry twice and skip another.
/// </param>
public sealed record AuditQuery(
    Guid? Subject = null,
    Guid? Actor = null,
    long? Before = null,
    int Limit = 100);
