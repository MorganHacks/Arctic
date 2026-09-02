using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MorganHacks.Applications.Storage;

namespace MorganHacks.Api.Tests;

/// <summary>
/// Azurite with OAuth turned on, which is the only way it will issue a user
/// delegation key.
/// </summary>
/// <remarks>
/// Deployed environments never sign with a shared key — atlas holds a managed
/// identity, so <c>CanGenerateSasUri</c> is false and every link goes through
/// <c>GetUserDelegationKeyAsync</c>. That is a different call, a different
/// signature and a different failure mode from the shared-key path, and until
/// this fixture existed it was the one branch nothing ran until it reached
/// staging.
/// <para>
/// Azurite will only serve OAuth over https, so the fixture makes a throwaway
/// certificate at start-up and hands the client a transport that does not check
/// it. Neither belongs anywhere near a deployed environment, which is why this
/// builds its own client rather than going through
/// <see cref="ResumeStorageOptions"/>.
/// </para>
/// </remarks>
public sealed class DelegatedBlobStorage : IAsyncLifetime
{
    private const int Port = 10000;

    private IContainer _container = null!;

    public AzureResumeStore Store { get; private set; } = null!;

    public Task InitializeAsync() => StartAsync();

    private async Task StartAsync()
    {
        var (certificate, key) = SelfSigned();

        _container = new ContainerBuilder("mcr.microsoft.com/azure-storage/azurite:latest")
            .WithPortBinding(Port, assignRandomHostPort: true)
            .WithResourceMapping(certificate, "/certs/cert.pem")
            .WithResourceMapping(key, "/certs/key.pem")
            .WithCommand(
                "azurite-blob",
                "--blobHost", "0.0.0.0",
                "--blobPort", Port.ToString(),
                "--oauth", "basic",
                "--cert", "/certs/cert.pem",
                "--key", "/certs/key.pem")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("successfully listens"))
            .Build();

        await _container.StartAsync();

        var endpoint = new Uri(
            $"https://{_container.Hostname}:{_container.GetMappedPublicPort(Port)}/devstoreaccount1");

        var service = new BlobServiceClient(endpoint, new StubCredential(), Trusting());

        // The store cannot create it on this path, and neither can a deployed
        // environment: the container comes from Bicep there and from here in a
        // test, which is the same arrangement rather than a shortcut.
        await service.GetBlobContainerClient("resumes").CreateIfNotExistsAsync();

        Store = new AzureResumeStore(service, "resumes");
    }

    /// <summary>A client that accepts the throwaway certificate.</summary>
    public static BlobClientOptions Trusting() => new()
    {
        Transport = new HttpClientTransport(new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        })),
    };

    private static (byte[] Certificate, byte[] Key) SelfSigned()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        return (
            Encoding.ASCII.GetBytes(new string(PemEncoding.Write("CERTIFICATE", certificate.RawData))),
            Encoding.ASCII.GetBytes(new string(PemEncoding.Write("PRIVATE KEY", rsa.ExportPkcs8PrivateKey()))));
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>
/// A well-formed token that nothing signed.
/// </summary>
/// <remarks>
/// Azurite in <c>basic</c> mode reads the token's shape and claims and does not
/// verify the signature, which is the point: what is under test is our half of
/// the exchange — that the delegation key is requested over the right window and
/// that the signature built from it actually opens the blob. Whether Entra
/// issues real tokens is not ours to prove.
/// </remarks>
internal sealed class StubCredential : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext context, CancellationToken ct)
    {
        var expires = DateTimeOffset.UtcNow.AddHours(1);

        return new AccessToken(
            $"{Segment("""{"alg":"RS256","typ":"JWT","kid":"test"}""")}."
            + $"{Segment(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["aud"] = "https://storage.azure.com",
                ["iss"] = "https://sts.windows.net/00000000-0000-0000-0000-000000000000/",
                ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["nbf"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["exp"] = expires.ToUnixTimeSeconds(),
                ["oid"] = "00000000-0000-0000-0000-000000000001",
                ["tid"] = "00000000-0000-0000-0000-000000000000",
                ["appid"] = "00000000-0000-0000-0000-000000000002",
            }))}."
            + $"{Segment("not-a-signature")}",
            expires);
    }

    public override ValueTask<AccessToken> GetTokenAsync(
        TokenRequestContext context, CancellationToken ct) => new(GetToken(context, ct));

    private static string Segment(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
