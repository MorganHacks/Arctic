using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace MorganHacks.Api;

/// <summary>
/// Pulling one file out of a multipart request, without letting it land
/// anywhere first.
/// </summary>
/// <remarks>
/// Its own file because two endpoints now take a resume — the public
/// application form and the portal — and the way the bytes are read is a
/// security decision rather than plumbing. A second copy of it would be a
/// second place for the cap to be subtly different, and the copy that drifts
/// is always the one nobody is looking at.
/// <para>
/// Read as a stream rather than through <c>ReadFormAsync</c>, and the
/// difference is what the cap actually means. Form binding buffers anything
/// over 64 KB to a temporary file and then hands it over, so every check would
/// be running against bytes already written to disk; this way a file over the
/// limit is abandoned mid-flight and never exists anywhere.
/// </para>
/// </remarks>
internal static class UploadedFile
{
    /// <summary>
    /// The name the caller's machine gave the file, and its bytes.
    /// </summary>
    /// <remarks>
    /// The name is arbitrary text from the public internet. It is carried out
    /// of here only so it can be shown back to somebody as content; nothing
    /// may build a path, a header or a log line from it.
    /// </remarks>
    /// <param name="Content">
    /// Null when the part existed and ran past the cap. That is a different
    /// answer from the method returning null, which means there was no file
    /// part at all, and the two produce different sentences on the screen.
    /// </param>
    internal readonly record struct Picked(string? Name, byte[]? Content);

    /// <summary>
    /// The first file part of a multipart body, up to <paramref name="limit"/>
    /// bytes.
    /// </summary>
    /// <remarks>
    /// Null means there was no file part at all — including a request that was
    /// not multipart, or one whose boundary is missing. All of those are the
    /// same thing to the person who pressed the button: nothing arrived.
    /// <para>
    /// The first file part wins and the rest of the body is not read. Both
    /// callers accept exactly one file, so a second attachment is either a
    /// mistake or somebody seeing what happens.
    /// </para>
    /// </remarks>
    internal static async Task<Picked?> ReadOneAsync(
        HttpRequest request, int limit, CancellationToken ct)
    {
        if (!request.HasFormContentType
            || !MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType))
        {
            return null;
        }

        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary);
        if (StringSegment.IsNullOrEmpty(boundary))
        {
            return null;
        }

        var reader = new MultipartReader(boundary.Value!, request.Body);

        while (await reader.ReadNextSectionAsync(ct) is { } section)
        {
            if (!ContentDispositionHeaderValue.TryParse(
                    section.ContentDisposition, out var disposition)
                || !disposition.IsFileDisposition())
            {
                continue;
            }

            var name = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue
                ? disposition.FileNameStar
                : disposition.FileName);

            return new Picked(name.Value, await ReadAtMostAsync(section.Body, limit, ct));
        }

        return null;
    }

    /// <summary>
    /// Reads a stream, and gives up rather than growing past a limit.
    /// </summary>
    /// <remarks>
    /// One byte past the cap is enough to know: it is read so that a file of
    /// exactly the limit is kept and the one after it is refused, without
    /// having to trust a length anybody sent.
    /// <para>
    /// The part's own <c>Content-Type</c> is never consulted, here or by
    /// either caller. It is a claim the uploader typed, and the only thing
    /// that decides what a file is are the bytes at the front of it — see
    /// <c>ResumeFile.Inspect</c>.
    /// </para>
    /// </remarks>
    private static async Task<byte[]?> ReadAtMostAsync(
        Stream source, int limit, CancellationToken ct)
    {
        var buffer = new byte[limit + 1];
        var read = 0;

        while (read < buffer.Length)
        {
            var got = await source.ReadAsync(buffer.AsMemory(read), ct);
            if (got == 0)
            {
                break;
            }

            read += got;
        }

        return read > limit ? null : buffer[..read];
    }
}
