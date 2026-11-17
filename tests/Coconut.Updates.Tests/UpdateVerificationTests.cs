using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using NSec.Cryptography;

namespace Coconut.Updates.Tests;

public sealed class UpdateVerificationTests : IDisposable
{
    private readonly Key _key = Key.Create(SignatureAlgorithm.Ed25519);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Coconut-update-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset s_now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] s_package = [1, 3, 5, 7, 9];

    [Fact]
    public void Valid_ed25519_manifest_is_accepted()
    {
        var manifest = Manifest(); Assert.Equal("0.3.0", Verify(Sign(manifest)).Version);
    }

    [Fact]
    public void Corrupted_manifest_or_signature_or_wrong_public_key_is_rejected()
    {
        var envelope = JsonSerializer.Deserialize<SignedManifest>(Sign(Manifest()))!;
        var payload = Convert.FromBase64String(envelope.Payload); payload[0] ^= 1;
        Assert.Throws<CryptographicException>(() => Verify(JsonSerializer.SerializeToUtf8Bytes(envelope with { Payload = Convert.ToBase64String(payload) })));
        var signature = Convert.FromBase64String(envelope.Signature); signature[0] ^= 1;
        Assert.Throws<CryptographicException>(() => Verify(JsonSerializer.SerializeToUtf8Bytes(envelope with { Signature = Convert.ToBase64String(signature) })));
        using var other = Key.Create(SignatureAlgorithm.Ed25519);
        Assert.Throws<CryptographicException>(() => ManifestVerifier.Verify(Sign(Manifest()), other.PublicKey.Export(KeyBlobFormat.RawPublicKey), new Version(0, 2, 0), s_now));
    }

    [Theory]
    [InlineData("version")] [InlineData("runtime")] [InlineData("origin")] [InlineData("expired")] [InlineData("future")] [InlineData("size")] [InlineData("hash")]
    public void Even_correctly_signed_invalid_metadata_is_rejected(string field)
    {
        var manifest = Manifest(); manifest = field switch
        {
            "version" => manifest with { Version = "0.1.0" },
            "runtime" => manifest with { Runtime = "win-arm64" },
            "origin" => manifest with { Url = "https://example.com/untrusted.exe" },
            "expired" => manifest with { ExpiresAt = s_now.AddMinutes(-1) },
            "future" => manifest with { PublishedAt = s_now.AddDays(1) },
            "size" => manifest with { Size = long.MaxValue },
            _ => manifest with { Sha256 = "invalid" },
        };
        Assert.Throws<CryptographicException>(() => Verify(Sign(manifest)));
    }

    [Fact]
    public async Task Downloaded_package_is_checked_again_at_apply_and_call_is_never_interrupted()
    {
        using var client = Client(s_package); var verified = await client.DownloadAsync(Verify(Sign(Manifest())));
        var executed = false;
        Assert.False(await VerifiedUpdateClient.ApplyAsync(verified, () => true, _ => executed = true)); Assert.False(executed);
        var calls = 0;
        Assert.False(await VerifiedUpdateClient.ApplyAsync(verified, () => ++calls == 2, _ => executed = true)); Assert.False(executed);
        Assert.True(await VerifiedUpdateClient.ApplyAsync(verified, () => false, _ => executed = true)); Assert.True(executed);
        File.WriteAllBytes(verified.Path, [7, 7, 7, 7, 7]);
        await Assert.ThrowsAsync<CryptographicException>(() => VerifiedUpdateClient.ApplyAsync(verified, () => false, _ => throw new InvalidOperationException("Cannot execute tampered package")));
    }

    [Fact]
    public async Task Tampered_or_truncated_or_oversized_download_is_deleted_and_never_returned()
    {
        foreach (var body in new byte[][] { [1, 3, 5, 7, 8], [1, 3], [1, 3, 5, 7, 9, 11] })
        {
            using var client = Client(body);
            await Assert.ThrowsAsync<CryptographicException>(() => client.DownloadAsync(Manifest()));
            Assert.Empty(Directory.EnumerateFiles(_directory));
        }
    }

    [Fact]
    public async Task Downgrade_to_http_or_unknown_redirect_host_is_rejected()
    {
        foreach (var url in new[] { "http://github.com/Tarcitani/coconut-call/file", "https://example.com/file" })
        {
            using var client = new VerifiedUpdateClient(_key.PublicKey.Export(KeyBlobFormat.RawPublicKey), new Version(0, 2, 0), _directory, new ReplyHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri(url); return response;
            }));
            await Assert.ThrowsAsync<CryptographicException>(() => client.DownloadAsync(Manifest()));
        }
    }

    private static UpdateManifest Manifest() => new(1, "0.3.0", "win-x64", "https://github.com/Tarcitani/coconut-call/releases/download/v0.3.0/Coconut-0.3.0-win-x64-Setup.exe",
        Convert.ToHexStringLower(SHA256.HashData(s_package)), s_package.Length, s_now.AddHours(-1), s_now.AddDays(365));
    private byte[] Sign(UpdateManifest manifest)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(manifest);
        return JsonSerializer.SerializeToUtf8Bytes(new SignedManifest(Convert.ToBase64String(payload), Convert.ToBase64String(SignatureAlgorithm.Ed25519.Sign(_key, payload))));
    }
    private UpdateManifest Verify(byte[] bytes) => ManifestVerifier.Verify(bytes, _key.PublicKey.Export(KeyBlobFormat.RawPublicKey), new Version(0, 2, 0), s_now);
    private VerifiedUpdateClient Client(byte[] body) => new(_key.PublicKey.Export(KeyBlobFormat.RawPublicKey), new Version(0, 2, 0), _directory,
        new ReplyHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }));
    public void Dispose() { _key.Dispose(); if (Directory.Exists(_directory)) { Directory.Delete(_directory, recursive: true); } }
    private sealed class ReplyHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(reply(request));
    }
}
