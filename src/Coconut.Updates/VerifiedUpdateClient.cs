using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace Coconut.Updates;

public sealed record VerifiedUpdate(UpdateManifest Manifest, string Path);

/// <summary>Only signed releases, bounded HTTPS downloads and re-verification immediately before execution.</summary>
public sealed class VerifiedUpdateClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly byte[] _publicKey;
    private readonly Version _installed;
    private readonly string _directory;

    public VerifiedUpdateClient(byte[] publicKey, Version installed, string directory, HttpMessageHandler? handler = null)
    {
        _publicKey = publicKey; _installed = installed; _directory = directory;
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Coconut-Updater/1.0");
    }

    public async Task<UpdateManifest?> CheckAsync(CancellationToken cancellation = default)
    {
        var bytes = await GetBytesAsync(new Uri("https://api.github.com/repos/Tarcitani/coconut-call/releases?per_page=10"), 512 * 1024, cancellation);
        using var document = JsonDocument.Parse(bytes);
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) { continue; }
            var tag = release.GetProperty("tag_name").GetString();
            if (tag is null || !tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version) || version <= _installed) { continue; }
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != "coconut-update.json") { continue; }
                var expectedUrl = $"https://github.com/Tarcitani/coconut-call/releases/download/{tag}/coconut-update.json";
                if (asset.GetProperty("browser_download_url").GetString() != expectedUrl) { continue; }
                var envelope = await GetBytesAsync(new Uri(expectedUrl), 16 * 1024, cancellation);
                var manifest = ManifestVerifier.Verify(envelope, _publicKey, _installed, DateTimeOffset.UtcNow);
                if (manifest.Version != tag[1..]) { throw new CryptographicException("Versão da release não corresponde ao manifesto."); }
                return manifest;
            }
        }
        return null;
    }

    public async Task<VerifiedUpdate> DownloadAsync(UpdateManifest manifest, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(_directory);
        var path = System.IO.Path.Combine(_directory, $"Coconut-{manifest.Version}-{Guid.NewGuid():N}-Setup.exe");
        try
        {
            using var response = await GetAsync(new Uri(manifest.Url), cancellation);
            await using var input = await response.Content.ReadAsStreamAsync(cancellation);
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                var buffer = new byte[64 * 1024]; long count = 0; using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                for (var size = await input.ReadAsync(buffer, cancellation); size != 0; size = await input.ReadAsync(buffer, cancellation))
                {
                    count += size; if (count > manifest.Size) { throw new CryptographicException("Pacote excedeu o tamanho assinado."); }
                    hash.AppendData(buffer.AsSpan(0, size)); await output.WriteAsync(buffer.AsMemory(0, size), cancellation);
                }
                if (count != manifest.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(manifest.Sha256)))
                { throw new CryptographicException("Pacote de atualização adulterado ou incompleto."); }
            }
            return new VerifiedUpdate(manifest, path);
        }
        catch { File.Delete(path); throw; }
    }

    public static async Task<bool> ApplyAsync(VerifiedUpdate update, Func<bool> isCallActive, Action<string> execute, CancellationToken cancellation = default)
    {
        if (isCallActive()) { return false; }
        // Holding this handle denies replacement/write between verification and CreateProcess.
        await using var file = new FileStream(update.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        var hash = await SHA256.HashDataAsync(file, cancellation);
        if (file.Length != update.Manifest.Size || !CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(update.Manifest.Sha256)))
        { throw new CryptographicException("O pacote foi alterado após o download."); }
        if (isCallActive()) { return false; }
        execute(update.Path); return true;
    }

    public void Dispose() => _http.Dispose();

    private async Task<byte[]> GetBytesAsync(Uri url, int maximum, CancellationToken cancellation)
    {
        using var response = await GetAsync(url, cancellation);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation); using var output = new MemoryStream();
        var buffer = new byte[8192];
        for (var read = await stream.ReadAsync(buffer, cancellation); read != 0; read = await stream.ReadAsync(buffer, cancellation))
        { if (output.Length + read > maximum) { throw new IOException("Resposta excedeu o limite."); } output.Write(buffer, 0, read); }
        return output.ToArray();
    }

    private async Task<HttpResponseMessage> GetAsync(Uri url, CancellationToken cancellation)
    {
        for (var redirects = 0; redirects < 6; redirects++)
        {
            if (url.Scheme != Uri.UriSchemeHttps || !AllowedHost(url.Host)) { throw new CryptographicException("Redirecionamento de atualização recusado."); }
            var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location; response.Dispose();
                if (location is null) { throw new HttpRequestException("Redirecionamento sem destino."); }
                url = location.IsAbsoluteUri ? location : new Uri(url, location); continue;
            }
            try { response.EnsureSuccessStatusCode(); return response; }
            catch { response.Dispose(); throw; }
        }
        throw new HttpRequestException("Redirecionamentos demais.");
    }

    private static bool AllowedHost(string host) => host is "github.com" or "api.github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com" or "github-releases.githubusercontent.com";
}
