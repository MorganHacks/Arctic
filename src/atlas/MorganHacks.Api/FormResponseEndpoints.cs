using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Forms;
using MorganHacks.Applications.Services;
using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;
using MorganHacks.Observability;

namespace MorganHacks.Api;

/// <summary>
/// Reading back what applicants answered.
/// </summary>
/// <remarks>
/// The other half of <see cref="PublicFormEndpoints"/>. That file takes a form
/// in and has no way to read one out on purpose — the code in a URL is the
/// whole permission there, and a public endpoint that could read answers would
/// hand the applicant pool to anybody who saw a flyer. This is where reading
/// happens, behind a session and a permission.
/// <para>
/// Three permissions, and the split is the point.
/// <c>applications.view_responses</c> reads the answers, and is deliberately
/// not <c>applications.view</c> — that one gates the form builder next door,
/// where seeing the questions is not seeing anybody's answers to them, and it
/// is held by comms and logistics for headcount rather than for reading what
/// several hundred people wrote about themselves.
/// <c>applications.export</c> gates the CSV, because a file on a laptop is PII
/// that has left the system and that permission already means exactly that.
/// <c>applications.view_resume</c> gates the signed link, checked inside the
/// handler rather than on the route, so this cannot become a second way to
/// reach a resume without the permission that exists to guard one.
/// </para>
/// <para>
/// Nothing here logs an answer, a filename or a cursor. Form ids, response
/// ids, actors and counts, which is enough to find a row and tells a log
/// reader nothing about who anybody is. See <see cref="Redaction"/> for the
/// net underneath that rule.
/// </para>
/// </remarks>
public static class FormResponseEndpoints
{
    /// <summary>How many responses a page holds when the caller says nothing.</summary>
    private const int DefaultLimit = 50;

    /// <summary>
    /// And the most it will hold however loudly they ask.
    /// </summary>
    /// <remarks>
    /// A ceiling rather than trust, because the alternative to a page is one
    /// request that reads every application and serialises the lot. Somebody
    /// who wants all of them wants the CSV, which is behind
    /// <c>applications.export</c> for a reason.
    /// </remarks>
    private const int MaxLimit = 200;

    public static IEndpointRouteBuilder MapFormResponses(this IEndpointRouteBuilder app)
    {
        var responses = app.MapGroup("/admin/forms/{id:guid}");

        responses.MapGet("/responses", List)
                 .RequirePermission(Permission.ApplicationsViewResponses);
        responses.MapGet("/responses/{responseId:guid}", One)
                 .RequirePermission(Permission.ApplicationsViewResponses);

        // A separate route rather than a format parameter on the one above,
        // because it is a different permission and a route is where this
        // codebase says so. `responses.csv` is its own literal segment, so it
        // cannot be confused with a response whose id happens to be "csv".
        responses.MapGet("/responses.csv", Export)
                 .RequirePermission(Permission.ApplicationsExport);

        return app;
    }

    // ------------------------------------------------------------- reading ---

    /// <summary>
    /// A page of responses, newest first. Requires
    /// <c>applications.view_responses</c>.
    /// </summary>
    /// <remarks>
    /// No resume links. Signing one costs a round trip to the object store,
    /// they expire in five minutes, and a page is fifty of them — so signing a
    /// page's worth would spend fifty calls on links that are dead before
    /// anybody has scrolled to the bottom. The list says whether there is a
    /// resume and what it is called; asking for the one response is what
    /// mints a URL.
    /// </remarks>
    private static async Task<IResult> List(
        Guid id,
        IFormStore forms,
        IResponseStore responses,
        CancellationToken ct,
        string? cursor = null,
        int limit = DefaultLimit)
    {
        var form = await forms.ByIdAsync(id, ct);
        if (form is null)
        {
            return Results.NotFound(new { error = "No such form." });
        }

        if (!TryCursor(cursor, out var after))
        {
            // Refused rather than ignored. A cursor we cannot read means the
            // caller is somewhere in the middle of a list, and starting them
            // silently at the top would read as the newest page arriving
            // twice.
            return Results.BadRequest(new { error = "That page marker is not one of ours." });
        }

        var questions = FormQuestions.From(await forms.HistoryAsync(id, ct));
        var page = await ResponsesOf(form, questions, responses, after, Clamp(limit), ct);

        return Results.Ok(new
        {
            items = page.Items.Select(r => Describe(r, link: null)),
            nextCursor = Encode(page.Next),
        });
    }

    /// <summary>
    /// One response, with a link to its resume. Requires
    /// <c>applications.view_responses</c>, and <c>applications.view_resume</c>
    /// for the link.
    /// </summary>
    /// <remarks>
    /// The resume permission is checked here rather than on the route because
    /// the rest of the answer set is readable without it. Somebody holding
    /// responses and not resumes sees the response with
    /// <c>resume.url</c> absent — the filename and size stay, because those
    /// are already in the list this screen came from and hiding them here
    /// would look like the file had gone.
    /// <para>
    /// Reading one leaves the same mark it leaves through
    /// <see cref="ResumeEndpoints"/>. The permission model calls a resume more
    /// sensitive than the rest of a record, which is only true if every path
    /// to one is recorded, and a second path that logs nothing would quietly
    /// undo that.
    /// </para>
    /// </remarks>
    private static async Task<IResult> One(
        Guid id,
        Guid responseId,
        HttpContext http,
        IFormStore forms,
        IResponseStore responses,
        IResumeStore resumes,
        PermissionService permissions,
        ILogger<FormResponse> log,
        CancellationToken ct)
    {
        var form = await forms.ByIdAsync(id, ct);
        if (form is null)
        {
            return Results.NotFound(new { error = "No such form." });
        }

        var questions = FormQuestions.From(await forms.HistoryAsync(id, ct));

        var response = form.IsApplication
            ? await responses.ByIdAsync(form.EventId, responseId, questions, ct)
            : null;

        if (response is null)
        {
            return Results.NotFound(new { error = "No such response on that form." });
        }

        // A second permission lookup, after the gate on the route already did
        // one. Worth a query: the alternative is the filter stashing the whole
        // permission set on the request for handlers to rummage through, and a
        // handler that can read any permission is one that will eventually
        // check the wrong one.
        var effective = await permissions.ForAsync(http.PersonId(), ct);

        SignedResume? link = null;
        if (response.Resume is { } resume
            && effective.Can(Permission.ApplicationsViewResume)
            && resumes.Available)
        {
            try
            {
                link = await resumes.LinkToAsync(
                    resume.StorageKey, ResumeFile.DownloadName(response.Id), ct);

                log.LogInformation(
                    "A resume was read. {actor} {applicationId} {event}",
                    http.PersonId(), response.Id, Events.ResumeRead);
            }
            catch (ResumeMissingException)
            {
                // The row says there are bytes and the store disagrees. Loud,
                // because an object went missing — and the rest of the
                // response still answers, since one absent file is no reason
                // to refuse somebody the answers beside it.
                log.LogError(
                    "An application points at a resume the store does not have. {applicationId}",
                    response.Id);
            }
        }

        return Results.Ok(Describe(response, link));
    }

    // ----------------------------------------------------------- exporting ---

    /// <summary>
    /// Every response as a spreadsheet. Requires <c>applications.export</c>.
    /// </summary>
    /// <remarks>
    /// One column per published question, in the order the form asks them, and
    /// never one column per key that happens to appear in the data. A question
    /// nobody answered has to still be a column — an export whose shape
    /// depends on its contents is one where two runs cannot be compared, and a
    /// missing column reads as a question that was never asked.
    /// <para>
    /// The other direction is the trailing <c>other_answers</c> column. A form
    /// edited mid-cycle leaves answers under keys the current questions no
    /// longer mention, and those are somebody's words: dropping them would be
    /// silent data loss in the one artefact people treat as the record. They
    /// go in one JSON cell at the end rather than in columns of their own, so
    /// the shape stays the published form's.
    /// </para>
    /// <para>
    /// Read a row at a time and written into a buffer, rather than streamed
    /// straight at the caller. Streaming would send a header before knowing
    /// the read succeeds, and a failure halfway through then arrives as a
    /// truncated CSV with a 200 on it — a file that opens, looks complete, and
    /// is missing the last two hundred applicants. Holding it costs a few
    /// megabytes for an applicant pool this size and buys a failure that fails.
    /// </para>
    /// </remarks>
    private static async Task<IResult> Export(
        Guid id,
        HttpContext http,
        IFormStore forms,
        IResponseStore responses,
        ILogger<FormResponse> log,
        CancellationToken ct)
    {
        var form = await forms.ByIdAsync(id, ct);
        if (form is null)
        {
            return Results.NotFound(new { error = "No such form." });
        }

        var questions = FormQuestions.From(await forms.HistoryAsync(id, ct));

        // The file question is left out. Its answer is an upload id that was
        // spent at submit and never lands in the answer set, so a column for
        // it would be empty in every row forever — the two resume columns
        // below are what it actually became.
        //
        // A page break is left out for a stronger reason: it is not a question
        // and nobody answered it, so a column for it would be an empty column
        // in every export with a heading nobody was ever asked. Its key stays
        // out of `known` as a consequence, which is what makes the one case
        // that matters work — a question turned into a page break after people
        // had answered it keeps its old answers, and they come back in
        // other_answers rather than silently disappearing.
        var columns = questions.Published
            .Where(f => f.Type is not (FieldType.File or FieldType.Section))
            .ToList();

        var known = columns.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);

        var rows = 0;
        var body = new MemoryStream();

        // A UTF-8 BOM, which is the one thing that makes Excel read the file
        // as UTF-8 rather than as the machine's code page. Without it every
        // name with an accent in it arrives mangled, and this file exists to
        // be opened in a spreadsheet.
        await using (var writer = new StreamWriter(
            body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true))
        {
            // CRLF, whatever the host runs. The file is written on Linux and
            // opened on Windows, and RFC 4180 settles it either way — leaving
            // it to Environment.NewLine means an export whose shape depends on
            // which machine produced it.
            writer.NewLine = "\r\n";

            await writer.WriteLineAsync(Row(
                ["id", "submitted_at", "form_version",
                 .. columns.Select(f => f.Key),
                 "resume_filename", "resume_size", "other_answers"]));

            if (form.IsApplication)
            {
                await foreach (var response in responses.AllAsync(form.EventId, questions, ct))
                {
                    await writer.WriteLineAsync(Row([
                        response.Id.ToString(),
                        response.SubmittedAt.ToString("O", CultureInfo.InvariantCulture),
                        response.FormVersion.ToString(CultureInfo.InvariantCulture),
                        .. columns.Select(f => response.Answers.TryGetValue(f.Key, out var a)
                            ? Text(a)
                            : string.Empty),
                        response.Resume?.Filename ?? string.Empty,
                        response.Resume?.Size?.ToString(CultureInfo.InvariantCulture)
                            ?? string.Empty,
                        Leftovers(response, known),
                    ]));

                    rows++;
                }
            }
        }

        body.Position = 0;

        // Who took a copy of the applicant pool, from which form, and how big
        // it was. Not what was in it. This is the log line an access review
        // reads, and applications.export is on the sensitive list precisely
        // because taking one has to leave a mark.
        log.LogInformation(
            "Form responses were exported. {actor} {form} {rows} {event}",
            http.PersonId(), id, rows, Events.ResponsesExported);

        return Results.File(
            body, "text/csv; charset=utf-8", $"responses-{form.Code}.csv");
    }

    /// <summary>
    /// The answers whose questions are no longer on the form, as JSON.
    /// </summary>
    /// <remarks>
    /// Empty for almost every row, and the reason the column exists is the
    /// rows where it is not: somebody rebuilt a question between two
    /// submissions and the earlier applicant's answer now has a key nothing
    /// asks for. It is still what they wrote.
    /// </remarks>
    private static string Leftovers(FormResponse response, IReadOnlySet<string> known)
    {
        var extra = response.Answers
            .Where(a => !known.Contains(a.Key))
            .OrderBy(a => a.Key, StringComparer.Ordinal)
            .ToDictionary(a => a.Key, a => a.Value);

        return extra.Count == 0 ? string.Empty : JsonSerializer.Serialize(extra);
    }

    // -------------------------------------------------------------- shaping ---

    /// <summary>
    /// One response as the console reads it.
    /// </summary>
    /// <remarks>
    /// The storage key is never in here, on any path. What the console gets is
    /// a name to show and a size to show beside it, and a URL only when one
    /// has been signed for this request — a key would be a permanent way to
    /// name somebody's CV, which is the whole reason the column holds a key
    /// and not a URL in the first place.
    /// </remarks>
    private static object Describe(FormResponse response, SignedResume? link) => new
    {
        id = response.Id,
        submittedAt = response.SubmittedAt,
        formVersion = response.FormVersion,
        answers = response.Answers,
        resume = response.Resume is null ? null : new
        {
            filename = response.Resume.Filename,
            sizeBytes = response.Resume.Size,
            url = link?.Url,

            // Handed over so the screen embedding the file knows when to ask
            // for a fresh one, rather than discovering the problem as a broken
            // frame. Same reasoning as ResumeEndpoints, which is the other
            // place a link is minted.
            expiresAt = link?.ExpiresAt,
        },
    };

    /// <summary>
    /// The responses on a form, or none at all.
    /// </summary>
    /// <remarks>
    /// Only an application form has any. A survey's answers are refused at
    /// submit — <see cref="PublicFormEndpoints"/> answers 501 rather than
    /// accepting them and dropping them — so there is genuinely nothing
    /// stored, and an empty page is the honest answer.
    /// <para>
    /// It also has to be an explicit check rather than a query that finds
    /// nothing. Responses are found by event, because an application carries
    /// no form id, so a survey sitting on an event beside the application form
    /// would otherwise answer with the application's responses under the
    /// survey's id.
    /// </para>
    /// </remarks>
    private static Task<ResponsePage> ResponsesOf(
        Form form,
        FormQuestions questions,
        IResponseStore responses,
        ResponseCursor? after,
        int limit,
        CancellationToken ct) =>
        form.IsApplication
            ? responses.PageAsync(form.EventId, questions, after, limit, ct)
            : Task.FromResult(new ResponsePage([], null));

    private static int Clamp(int limit) =>
        limit <= 0 ? DefaultLimit : Math.Min(limit, MaxLimit);

    // --------------------------------------------------------------- cursor ---

    /// <summary>
    /// Where the next page starts, as one opaque string.
    /// </summary>
    /// <remarks>
    /// Opaque so that the ordering stays ours. A caller that can read
    /// "timestamp, id" out of a cursor is a caller who will eventually
    /// construct one, and then the ordering columns are a public API that
    /// cannot be changed without breaking whoever did. It is not a secret —
    /// everything it names is already in the page it came with — so it is
    /// encoded rather than signed.
    /// </remarks>
    private static string? Encode(ResponseCursor? cursor) => cursor is not { } at
        ? null
        : Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            $"{at.SubmittedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}:{at.Id:N}"));

    /// <summary>
    /// Reads one back, or refuses.
    /// </summary>
    /// <remarks>
    /// True with a null cursor means "start at the top", which is what no
    /// cursor at all means. False means one arrived and was not ours, and the
    /// caller is told so rather than quietly restarted.
    /// </remarks>
    private static bool TryCursor(string? value, out ResponseCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(value)).Split(':');

            if (parts.Length != 2
                || !long.TryParse(parts[0], CultureInfo.InvariantCulture, out var ticks)
                || ticks < 0 || ticks > DateTimeOffset.MaxValue.UtcTicks
                || !Guid.TryParseExact(parts[1], "N", out var id))
            {
                return false;
            }

            cursor = new ResponseCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ csv ---

    /// <summary>One line of CSV, RFC 4180 shaped.</summary>
    private static string Row(IEnumerable<string> cells) => string.Join(",", cells.Select(Cell));

    /// <summary>
    /// One cell, quoted and defused.
    /// </summary>
    /// <remarks>
    /// The quoting is ordinary CSV: every cell is quoted whether it needs to
    /// be or not, and a quote inside one is doubled. Unconditional because the
    /// answers here are paragraphs somebody typed, and deciding per cell is
    /// one forgotten case away from a newline in an essay shifting every
    /// column after it.
    /// <para>
    /// The leading apostrophe is the part that matters. Excel and Sheets treat
    /// a cell starting <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> as a formula
    /// and evaluate it on open, which makes an answer field a way to run
    /// something on an organizer's machine — <c>=HYPERLINK</c> pointing at an
    /// attacker's host with the row beside it in the query string is the cheap
    /// version, and applicants control every one of these cells. Tab and
    /// carriage return are in the list because both survive into the cell and
    /// leave the next character leading.
    /// </para>
    /// <para>
    /// It costs an apostrophe in front of a genuine negative number, which is
    /// the trade: a spreadsheet nobody can weaponise, at the price of a
    /// character in front of the few cells that begin with a minus sign.
    /// Phone numbers are the common case and they are text anyway.
    /// </para>
    /// </remarks>
    private static string Cell(string? value)
    {
        var text = value ?? string.Empty;

        if (text.Length > 0 && Dangerous.Contains(text[0]))
        {
            text = "'" + text;
        }

        return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static readonly SearchValues<char> Dangerous =
        SearchValues.Create("=+-@\t\r");

    /// <summary>
    /// One answer as a spreadsheet cell.
    /// </summary>
    /// <remarks>
    /// A checkboxes answer is several values and becomes one comma-separated
    /// cell, matching what the write path already does when the same answer is
    /// routed at a text column. Anything else keeps its JSON spelling, so a
    /// number reads as a number and a consent reads as the moment it was
    /// ticked.
    /// </remarks>
    private static string Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(Text)),
        _ => value.GetRawText(),
    };
}
