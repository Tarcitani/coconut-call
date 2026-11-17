using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using NSec.Cryptography;

namespace Coconut.Updates;

public sealed record UpdateManifest(int Schema, string Version, string Runtime, string Url, string Sha256, long Size, DateTimeOffset PublishedAt, DateTimeOffset ExpiresAt);
public sealed record SignedManifest(string Payload, string Signature);

public static class ManifestVerifier
{
    private static readonly JsonSerializerOptions s_json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static UpdateManifest Verify(ReadOnlySpan<byte> envelope, ReadOnlySpan<byte> publicKey, Version installed, DateTimeOffset now)
    {
        if (envelope.Length > 16 * 1024) { throw new CryptographicException("Manifesto muito grande."); }
        var signed = JsonSerializer.Deserialize<SignedManifest>(envelope, s_json) ?? throw new CryptographicException("Manifesto ausente.");
        var payload = Convert.FromBase64String(signed.Payload); var signature = Convert.FromBase64String(signed.Signature);
        var algorithm = SignatureAlgorithm.Ed25519;
        var key = PublicKey.Import(algorithm, publicKey, KeyBlobFormat.RawPublicKey);
        if (payload.Length > 4096 || signature.Length != algorithm.SignatureSize || !algorithm.Verify(key, payload, signature)) { throw new CryptographicException("Assinatura de atualização inválida."); }
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(payload, s_json) ?? throw new CryptographicException("Manifesto inválido.");
        if (manifest.Schema != 1 || manifest.Runtime != "win-x64" || !System.Version.TryParse(manifest.Version, out var version) ||
            version.Build < 0 || version.Revision >= 0 || version <= installed || manifest.Size is < 1 or > 512 * 1024 * 1024 ||
            manifest.Sha256.Length != 64 || manifest.Sha256.Any(c => !Uri.IsHexDigit(c)) ||
            manifest.PublishedAt > now.AddMinutes(10) || manifest.ExpiresAt <= now || manifest.ExpiresAt <= manifest.PublishedAt)
        { throw new CryptographicException("Versão, prazo, runtime ou hash da atualização inválidos."); }
        var expected = $"https://github.com/Tarcitani/coconut-call/releases/download/v{manifest.Version}/Coconut-{manifest.Version}-win-x64-Setup.exe";
        if (!string.Equals(manifest.Url, expected, StringComparison.Ordinal)) { throw new CryptographicException("Origem da atualização inválida."); }
        return manifest;
    }

    public static byte[] EmbeddedPublicKey()
    {
        using var stream = typeof(ManifestVerifier).Assembly.GetManifestResourceStream("Coconut.Updates.release-public-key.txt")
            ?? throw new CryptographicException("Chave pública de atualização ausente.");
        using var reader = new StreamReader(stream); return Convert.FromBase64String(reader.ReadToEnd().Trim());
    }
}
