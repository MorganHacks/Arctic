using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Services;

namespace MorganHacks.Applications.Storage;

/// <summary>How to reach the container resumes are kept in.</summary>
/// <remarks>
/// Two ways, and which one is in use is the difference between a laptop and a
/// deployment. <see cref="ConnectionString"/> is for Azurite, where there is no
/// tenant to authenticate against; <see cref="AccountName"/> is for Azure,
/// where the service already holds an identity and a key in configuration
/// would be a credential to leak for no benefit.
/// </remarks>
public sealed record ResumeStorageOptions
{
    public string? AccountName { get; init; }

    public string? ConnectionString { get; init; }

    public string Container { get; init; } = "resumes";

    /// <summary>
    /// Which identity to use, when the service holds more than one.
    /// </summary>
    /// <remarks>
    /// A container app with a user-assigned identity attached still has to be
    /// told to use it: with several available, the credential chain picks the
    /// system-assigned one, which holds no role here. Left empty this falls
    /// back to whatever the environment offers, which is what a developer
    /// signed in with the Azure CLI wants.
    /// </remarks>
    public string? ManagedIdentityClientId { get; init; }
}

/// <summary>
/// Resumes in Azure Blob Storage, in a private container, reachable only
/// through links that expire.
/// </summary>
/// <remarks>
/// Azure rather than the R2 the plan recommends, and the reasoning is worth
/// keeping next to the code. We own the subscription this runs in, so the
/// container is declared in the same Bicep deployment as everything else with
/// no second account to open and no second set of credentials to store or
/// rotate — atlas reaches it with the managed identity it already has for the
/// registry. The portability the plan is protecting is that the database holds
/// a key rather than a URL, and that survives the choice of vendor: it lives in
/// <see cref="IResumeStore"/>, not here.
/// <para>
/// The container is private and is never made otherwise. Every read is a
/// user-delegation SAS signed for five minutes, carrying its own content type
/// and disposition, so a link that escapes is a link that has already stopped
/// working — and an anonymous request straight at the blob URL is a 404 whether
/// or not somebody guessed the key.
/// </para>
/// </remarks>
public sealed class AzureResumeStore : IResumeStore
{
    private readonly BlobServiceClient? _service;
    private readonly BlobContainerClient? _container;
    private readonly bool _canCreateContainer;

    public AzureResumeStore(ResumeStorageOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            _service = new BlobServiceClient(options.ConnectionString);

            // Only on the connection-string path, which is Azurite. A deployed
            // environment gets its container from Bicep, and a service that
            // creates its own storage on demand is one that silently keeps
            // working while pointed at the wrong account.
            _canCreateContainer = true;
        }
        else if (!string.IsNullOrWhiteSpace(options.AccountName))
        {
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = options.ManagedIdentityClientId,
            });

            _service = new BlobServiceClient(
                new Uri($"https://{options.AccountName}.blob.core.windows.net"), credential);
        }

        _container = _service?.GetBlobContainerClient(options.Container);
    }

    public bool Available => _container is not null;

    public async Task<string> StoreAsync(
        Guid eventId, ReadOnlyMemory<byte> content, CancellationToken ct = default)
    {
        var container = Configured();

        if (_canCreateContainer)
        {
            // None, said out loud. The default for this argument is also None,
            // but "these are private" is the single most important fact about
            // this container and it should not be inherited from a default
            // somebody has to go and look up.
            await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        }

        var key = ResumeFile.NewKey(eventId);
        var blob = container.GetBlobClient(key);

        await blob.UploadAsync(
            BinaryData.FromBytes(content),
            new BlobUploadOptions
            {
                // Stored as a PDF as well as served as one. The link overrides
                // this anyway; setting it here means anybody who ever reaches
                // these objects another way — a storage browser, a lifecycle
                // rule, a future export — sees the same answer.
                HttpHeaders = new BlobHttpHeaders { ContentType = ResumeFile.ContentType },

                // Refuses to overwrite. Keys are random so a collision is not a
                // thing that happens; if one ever does, losing an application's
                // resume silently is much worse than a failed upload somebody
                // retries.
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
            },
            ct);

        return key;
    }

    public async Task<SignedResume> LinkToAsync(
        string storageKey, string downloadName, CancellationToken ct = default)
    {
        var container = Configured();
        var blob = container.GetBlobClient(storageKey);

        // Asked before signing. A SAS is generated without touching the blob,
        // so a key with nothing behind it would produce a working-looking link
        // that answers with an XML error document — which is how a reviewer
        // ends up reporting "the resume is broken" for a row that never had
        // one.
        if (!await blob.ExistsAsync(ct))
        {
            throw new ResumeMissingException("No resume is stored under that key.");
        }

        var expiresAt = DateTimeOffset.UtcNow + IResumeStore.LinkLifetime;

        var sas = new BlobSasBuilder
        {
            BlobContainerName = container.Name,
            BlobName = storageKey,
            Resource = "b",
            ExpiresOn = expiresAt,

            // The two headers the file arrives with, pinned into the signature
            // so they cannot be argued with by the request. A reviewer opens
            // these in a browser, and this is what stops a file a stranger
            // uploaded being interpreted as anything other than a PDF.
            ContentType = ResumeFile.ContentType,
            ContentDisposition = $"inline; filename=\"{downloadName}\"",
        };

        sas.SetPermissions(BlobSasPermissions.Read);

        // Azurite speaks plain HTTP, so this cannot be unconditional — a
        // https-only SAS against it signs a link that refuses every request.
        if (blob.Uri.Scheme == Uri.UriSchemeHttps)
        {
            sas.Protocol = SasProtocol.Https;
        }

        return new SignedResume(await SignAsync(blob, sas, expiresAt, ct), expiresAt);
    }

    /// <summary>
    /// Signs the link with whichever key this store actually has.
    /// </summary>
    /// <remarks>
    /// A managed identity holds no account key, so a SAS it issues is signed
    /// with a user delegation key fetched from Azure for the occasion. That is
    /// the better half of the trade rather than a workaround: the account key
    /// is never in configuration, never in a container image, and the
    /// delegation key expires on its own, so revoking the identity's role ends
    /// the ability to hand out links.
    /// </remarks>
    private async Task<Uri> SignAsync(
        BlobClient blob, BlobSasBuilder sas, DateTimeOffset expiresAt, CancellationToken ct)
    {
        if (blob.CanGenerateSasUri)
        {
            return blob.GenerateSasUri(sas);
        }

        // A few minutes of slack at each end, because the signing clock and
        // Azure's are not the same clock. Without it a link is intermittently
        // born expired, which is the kind of fault that only shows up under
        // somebody else's account.
        var skew = TimeSpan.FromMinutes(5);
        var delegation = await _service!.GetUserDelegationKeyAsync(
            DateTimeOffset.UtcNow - skew, expiresAt + skew, ct);

        var query = sas.ToSasQueryParameters(delegation.Value, blob.AccountName);
        return new BlobUriBuilder(blob.Uri) { Sas = query }.ToUri();
    }

    private BlobContainerClient Configured() =>
        _container ?? throw new InvalidOperationException(
            "Resume storage is not configured. Set Resumes:AccountName or "
            + "Resumes:ConnectionString.");
}
