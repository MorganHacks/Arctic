using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Services;
using MorganHacks.Applications.Storage;
using Testcontainers.Azurite;

namespace MorganHacks.Api.Tests;

/// <summary>
/// A throwaway Azure Blob, via Microsoft's own emulator.
/// </summary>
/// <remarks>
/// The real client against a real blob service, for the same reason the schema
/// tests run migrations against a real Postgres: what is being protected here
/// is a signature, and a hand-written double would sign whatever we told it to.
/// </remarks>
public sealed class BlobStorage : IAsyncLifetime
{
    // The same image docker-compose runs, so what a contributor develops
    // against and what CI asserts against are one thing.
    private readonly AzuriteContainer _container =
        new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:latest").Build();

    public AzureResumeStore Store { get; private set; } = null!;

    public Task InitializeAsync() => StartAsync();

    private async Task StartAsync()
    {
        await _container.StartAsync();

        Store = new AzureResumeStore(new ResumeStorageOptions
        {
            ConnectionString = _container.GetConnectionString(),
            Container = "resumes",
        });
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>
/// What a stored resume is, and what a link to one lets somebody do.
/// </summary>
/// <remarks>
/// These run the shared-key path, which is what a developer with the
/// docker-compose stack has. Deployed environments sign with a user delegation
/// key instead; that branch is covered by
/// <see cref="DelegatedResumeLinkTests"/> against the same emulator with OAuth
/// switched on, so neither path is left to staging to discover.
/// </remarks>
public class ResumeStoreTests(BlobStorage blobs) : IClassFixture<BlobStorage>
{
    private static byte[] Pdf(int size = 2048)
    {
        var bytes = new byte[size];
        "%PDF-1.7\n"u8.CopyTo(bytes);
        return bytes;
    }

    [Fact]
    public async Task A_stored_resume_comes_back_through_its_link()
    {
        var key = await blobs.Store.StoreAsync(Guid.NewGuid(), Pdf());
        var link = await blobs.Store.LinkToAsync(key, "resume-abcdef.pdf");

        using var http = new HttpClient();
        var response = await http.GetAsync(link.Url);

        response.EnsureSuccessStatusCode();
        Assert.Equal(2048, (await response.Content.ReadAsByteArrayAsync()).Length);
    }

    [Fact]
    public async Task The_file_arrives_as_a_pdf_with_a_name_we_chose()
    {
        // These two headers are inside the signature rather than requested by
        // the caller, and together they are what stops a file a stranger
        // uploaded being interpreted as anything else. A reviewer opens these
        // in a browser.
        var key = await blobs.Store.StoreAsync(Guid.NewGuid(), Pdf());
        var link = await blobs.Store.LinkToAsync(key, "resume-abcdef.pdf");

        using var http = new HttpClient();
        var response = await http.GetAsync(link.Url);

        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

        var disposition = response.Content.Headers.ContentDisposition!;
        Assert.Equal("inline", disposition.DispositionType);
        Assert.Contains("resume-abcdef.pdf", disposition.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_same_URL_without_its_signature_is_refused()
    {
        // The container is private, and this is the assertion that says so.
        // Everything else here would still pass if somebody flipped it to
        // anonymous read, which is the one mistake that turns every resume
        // into a public URL.
        var key = await blobs.Store.StoreAsync(Guid.NewGuid(), Pdf());
        var link = await blobs.Store.LinkToAsync(key, "resume-abcdef.pdf");

        using var http = new HttpClient();
        var unsigned = await http.GetAsync(link.Url.GetLeftPart(UriPartial.Path));

        Assert.False(unsigned.IsSuccessStatusCode);
    }

    [Fact]
    public async Task A_link_lasts_five_minutes()
    {
        // Long enough to open the file, short enough that a URL pasted into a
        // group chat is dead before anybody clicks it.
        var key = await blobs.Store.StoreAsync(Guid.NewGuid(), Pdf());
        var link = await blobs.Store.LinkToAsync(key, "resume-abcdef.pdf");

        Assert.InRange(
            link.ExpiresAt - DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(4),
            TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task A_key_with_nothing_behind_it_fails_rather_than_signing()
    {
        // A SAS is generated without touching the blob, so an absent object
        // would otherwise produce a working-looking link that answers with an
        // XML error document — which a reviewer reports as "the resume is
        // broken" rather than as the missing file it is.
        await Assert.ThrowsAsync<ResumeMissingException>(
            () => blobs.Store.LinkToAsync(ResumeFile.NewKey(Guid.NewGuid()), "resume.pdf"));
    }

    [Fact]
    public async Task An_upload_never_lands_on_top_of_an_existing_one()
    {
        // Keys are random, so this does not happen. If it ever did, losing an
        // applicant's resume silently is far worse than an upload they retry:
        // nobody would know to look.
        var key = await blobs.Store.StoreAsync(Guid.NewGuid(), Pdf());
        var again = await blobs.Store.StoreAsync(Guid.NewGuid(), Pdf());

        Assert.NotEqual(key, again);
    }
}
