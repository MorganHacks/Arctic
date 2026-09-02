namespace MorganHacks.Api.Tests;

/// <summary>
/// The same guarantees as <see cref="ResumeStoreTests"/>, reached the way a
/// deployed environment reaches them: a user delegation key rather than a
/// shared one.
/// </summary>
/// <remarks>
/// Worth having as its own class rather than a second case on the existing
/// tests. The shared-key path signs locally and can never fail for a reason
/// involving the service; the delegation path asks the service for a key first,
/// over a window our clock chooses, and then signs with it. A link that is born
/// expired, a window the service rejects, or a signature built from the wrong
/// account name are all failures that only exist on this side, and they are
/// exactly the failures that would otherwise wait for staging to show up.
/// </remarks>
public class DelegatedResumeLinkTests(DelegatedBlobStorage blobs)
    : IClassFixture<DelegatedBlobStorage>
{
    private static byte[] Pdf(int size = 2048)
    {
        var bytes = new byte[size];
        "%PDF-1.7\n"u8.CopyTo(bytes);
        return bytes;
    }

    private static HttpClient Trusting() => new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
    });

    [Fact]
    public async Task A_link_signed_with_a_delegation_key_opens_the_file()
    {
        var key = await blobs.Store.StoreAsync(Guid.NewGuid(), Pdf());
        var link = await blobs.Store.LinkToAsync(key, "resume-abcdef.pdf");

        using var http = Trusting();
        var response = await http.GetAsync(link.Url);

        response.EnsureSuccessStatusCode();
        Assert.Equal(2048, (await response.Content.ReadAsByteArrayAsync()).Length);
    }

    [Fact]
    public async Task It_is_a_delegation_signature_rather_than_a_shared_key_one()
    {
        // Without this the rest of the class could pass while quietly running
        // the shared-key path, which is the thing it exists not to run. The
        // signed object id only appears on a delegation signature.
        var key = await blobs.Store.StoreAsync(Guid.NewGuid(), Pdf());
        var link = await blobs.Store.LinkToAsync(key, "resume-abcdef.pdf");

        Assert.Contains("skoid=", link.Url.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_type_and_the_name_survive_the_delegation_signature()
    {
        var key = await blobs.Store.StoreAsync(Guid.NewGuid(), Pdf());
        var link = await blobs.Store.LinkToAsync(key, "resume-abcdef.pdf");

        using var http = Trusting();
        var response = await http.GetAsync(link.Url);

        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

        var disposition = response.Content.Headers.ContentDisposition!;
        Assert.Equal("inline", disposition.DispositionType);
        Assert.Contains("resume-abcdef.pdf", disposition.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_container_is_still_private_on_this_path()
    {
        var key = await blobs.Store.StoreAsync(Guid.NewGuid(), Pdf());
        var link = await blobs.Store.LinkToAsync(key, "resume-abcdef.pdf");

        using var http = Trusting();
        var unsigned = await http.GetAsync(link.Url.GetLeftPart(UriPartial.Path));

        Assert.False(unsigned.IsSuccessStatusCode);
    }

    [Fact]
    public async Task The_link_is_usable_the_moment_it_is_issued()
    {
        // The reason the signing window opens five minutes in the past. Azure's
        // clock and ours are not the same clock, and without the backdating a
        // link is intermittently born expired — a fault that reproduces on
        // somebody else's machine and never on the one that wrote it.
        var key = await blobs.Store.StoreAsync(Guid.NewGuid(), Pdf());
        var link = await blobs.Store.LinkToAsync(key, "resume-abcdef.pdf");

        using var http = Trusting();
        var response = await http.GetAsync(link.Url);

        Assert.True(
            response.IsSuccessStatusCode,
            $"a freshly signed link answered {(int)response.StatusCode}");
    }
}
