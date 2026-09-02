using System.Text;

namespace MorganHacks.Applications.Domain;

/// <summary>Why a file was refused, in words the applicant can act on.</summary>
public enum ResumeRejection
{
    None,
    Empty,
    TooLarge,
    NotAPdf,
}

/// <summary>
/// The rules a resume has to pass before any of it is kept.
/// </summary>
/// <remarks>
/// Pure, and separate from the endpoint and the store on purpose: these are
/// the rules that decide whether arbitrary bytes from the public internet get
/// written down, and they should be readable and testable without a container
/// or a cloud account in the way.
/// <para>
/// The page checks the size and the extension before it uploads. That is a
/// courtesy — it saves somebody pushing five megabytes up campus wifi to be
/// told no — and nothing here assumes it happened.
/// </para>
/// </remarks>
public static class ResumeFile
{
    /// <summary>
    /// Five mebibytes.
    /// </summary>
    /// <remarks>
    /// Enforced here, over the bytes actually read, rather than from the
    /// request's own Content-Length. A length header is a claim by the caller;
    /// this is a measurement.
    /// </remarks>
    public const int MaxBytes = 5 * 1024 * 1024;

    /// <summary>
    /// The only type accepted, and the only type ever served back.
    /// </summary>
    /// <remarks>
    /// One type rather than a list. Every additional format is another parser
    /// running in a reviewer's browser against a file a stranger chose, and a
    /// resume that is not a PDF is a resume that renders differently for every
    /// person who opens it anyway.
    /// </remarks>
    public const string ContentType = "application/pdf";

    /// <summary>
    /// What a PDF starts with: <c>%PDF-</c>.
    /// </summary>
    /// <remarks>
    /// The header is required at offset zero. Some readers tolerate junk in
    /// front of it, and matching them would mean accepting a file that is an
    /// HTML page for the first kilobyte and a PDF afterwards.
    /// </remarks>
    private static ReadOnlySpan<byte> PdfHeader => "%PDF-"u8;

    /// <summary>
    /// Whether these bytes may be stored.
    /// </summary>
    /// <remarks>
    /// The content decides, never the name. <c>.pdf</c> on the end of a
    /// filename is a claim by whoever uploaded it, costs nothing to write, and
    /// is the single check that separates "we accept resumes" from "we accept
    /// anything and hand it to an organizer to open".
    /// </remarks>
    public static ResumeRejection Inspect(ReadOnlySpan<byte> content)
    {
        if (content.Length == 0)
        {
            return ResumeRejection.Empty;
        }

        if (content.Length > MaxBytes)
        {
            return ResumeRejection.TooLarge;
        }

        return content.Length >= PdfHeader.Length && content[..PdfHeader.Length].SequenceEqual(PdfHeader)
            ? ResumeRejection.None
            : ResumeRejection.NotAPdf;
    }

    /// <summary>What to tell the person who picked the file.</summary>
    /// <remarks>
    /// Each one says what was wrong and what to do about it. "Invalid file" is
    /// the version of this message that gets a support email.
    /// </remarks>
    public static string Explain(ResumeRejection rejection) => rejection switch
    {
        ResumeRejection.Empty =>
            "That file was empty. Pick the file again — it may not have finished saving.",
        ResumeRejection.TooLarge =>
            "That file is over 5 MB. Export it again at a smaller size, or remove any "
            + "images, and upload it once more.",
        ResumeRejection.NotAPdf =>
            "That file is not a PDF. Renaming a document to end in .pdf does not convert "
            + "it — use \"Export as PDF\" or \"Save as PDF\" and upload the result.",
        _ => "That file could not be accepted.",
    };

    /// <summary>
    /// Where a resume is kept.
    /// </summary>
    /// <remarks>
    /// Generated, with nothing of the uploaded filename in it. A filename is
    /// attacker-controlled: <c>../../something</c> and a name that differs from
    /// another only by case both do damage the moment a key is built from one,
    /// and no amount of stripping characters is as good as not using it.
    /// <para>
    /// Foldered by event so a year can be found, listed or expired as a unit,
    /// and suffixed <c>.pdf</c> only because a key that reads as a file is
    /// easier to recognise in a storage browser. Nothing derives the type from
    /// the suffix.
    /// </para>
    /// </remarks>
    public static string NewKey(Guid eventId) => $"{eventId:N}/{Guid.NewGuid():N}.pdf";

    /// <summary>
    /// The name the reviewer's browser saves it as.
    /// </summary>
    /// <remarks>
    /// Built from the application id rather than passed through from the
    /// upload. The stored name is arbitrary text from the public internet and
    /// this value ends up inside a <c>Content-Disposition</c> header — a
    /// newline in it is header injection, and quotes and semicolons are enough
    /// to change what the rest of the header means.
    /// <para>
    /// Nothing is lost by it: the name the applicant used is on the
    /// application row and the review screen shows it there, where it is text
    /// on a page rather than part of a protocol.
    /// </para>
    /// </remarks>
    public static string DownloadName(Guid applicationId) => $"resume-{applicationId:N}.pdf";

    /// <summary>
    /// Trims a filename to something the column will hold.
    /// </summary>
    /// <remarks>
    /// <c>resume_filename</c> is unbounded text reached from an endpoint that
    /// takes no authentication, so the length has to be decided here rather
    /// than by whoever is calling. Control characters go with it: this string
    /// is written to a database, read back into JSON and rendered on a review
    /// screen, and a newline in the middle of it is nobody's real filename.
    /// </remarks>
    public static string TidyFilename(string? filename)
    {
        var trimmed = (filename ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return "resume.pdf";
        }

        var tidy = new StringBuilder(Math.Min(trimmed.Length, MaxFilenameLength));
        foreach (var c in trimmed)
        {
            if (tidy.Length == MaxFilenameLength)
            {
                break;
            }

            tidy.Append(char.IsControl(c) ? ' ' : c);
        }

        return tidy.ToString();
    }

    private const int MaxFilenameLength = 255;
}
