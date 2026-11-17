using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coconut.Updates;
using NSec.Cryptography;
using System.Diagnostics;

var keyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Coconut", "signing", "release-key.dpapi");
var entropy = Encoding.UTF8.GetBytes("Coconut release signing v1");
if (args.Length == 2 && args[0] == "init")
{
    if (!File.Exists(keyPath))
    {
        using var generated = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var raw = generated.Export(KeyBlobFormat.RawPrivateKey);
        try
        {
            var encrypted = ProtectedData.Protect(raw, entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
            using var file = new FileStream(keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None); file.Write(encrypted);
        }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }
    using var key = LoadKey(); var publicKey = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
    if (File.Exists(args[1]) && File.ReadAllText(args[1]).Trim() != publicKey) { throw new CryptographicException("A chave pública existente pertence a outra identidade. Não será substituída."); }
    File.WriteAllText(args[1], publicKey + "\n", new UTF8Encoding(false));
    Console.WriteLine("Chave de assinatura protegida por DPAPI do usuário; somente a chave pública foi exportada."); return 0;
}
if (args.Length == 5 && args[0] == "sign")
{
    var version = args[1]; var setup = args[2]; var output = args[3]; var publicFile = args[4];
    if (!Version.TryParse(version, out var parsed) || parsed.Build < 0 || parsed.Revision >= 0 || Path.GetFileName(setup) != $"Coconut-{version}-win-x64-Setup.exe") { throw new ArgumentException("Somente o instalador público versionado pode ser assinado."); }
    using var key = LoadKey();
    if (Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey)) != File.ReadAllText(publicFile).Trim()) { throw new CryptographicException("Identidade de assinatura não corresponde à chave do aplicativo."); }
    using var file = File.OpenRead(setup); var hash = Convert.ToHexStringLower(SHA256.HashData(file)); var now = DateTimeOffset.UtcNow;
    var manifest = new UpdateManifest(1, version, "win-x64", $"https://github.com/Tarcitani/coconut-call/releases/download/v{version}/Coconut-{version}-win-x64-Setup.exe", hash, file.Length, now, now.AddDays(365));
    var payload = JsonSerializer.SerializeToUtf8Bytes(manifest);
    var signed = new SignedManifest(Convert.ToBase64String(payload), Convert.ToBase64String(SignatureAlgorithm.Ed25519.Sign(key, payload)));
    var bytes = JsonSerializer.SerializeToUtf8Bytes(signed); File.WriteAllBytes(output, bytes);
    ManifestVerifier.Verify(bytes, key.PublicKey.Export(KeyBlobFormat.RawPublicKey), new Version(0, 0, 0), now);
    Console.WriteLine($"Manifesto Ed25519 criado e verificado: {output}"); return 0;
}
if (args.Length == 5 && args[0] == "verify-apply")
{
    var manifest = ManifestVerifier.Verify(File.ReadAllBytes(args[1]), ManifestVerifier.EmbeddedPublicKey(), Version.Parse(args[3]), DateTimeOffset.UtcNow);
    var useInstalled = args[4] == "installed";
    var target = useInstalled ? null : Path.GetFullPath(args[4]); var allowed = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "artifacts")) + Path.DirectorySeparatorChar;
    if (!useInstalled && !target!.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) { throw new ArgumentException("O destino de validação precisa ficar em artifacts."); }
    if (useInstalled)
    {
        var registered = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\{F5D7B2A9-BF31-4A1F-9229-92A2B27D0E66}_is1", "DisplayVersion", null) as string;
        if (!Version.TryParse(registered, out var actual) || actual != Version.Parse(args[3])) { throw new InvalidOperationException("A versão registrada não corresponde à baseline informada."); }
    }
    Process? installer = null;
    var applied = await VerifiedUpdateClient.ApplyAsync(new VerifiedUpdate(manifest, Path.GetFullPath(args[2])), () => Process.GetProcessesByName("Coconut").Length != 0, path =>
    {
        var start = new ProcessStartInfo(path) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("/VERYSILENT"); start.ArgumentList.Add("/SUPPRESSMSGBOXES");
        start.ArgumentList.Add("/SP-"); start.ArgumentList.Add("/NORESTART");
        if (target is not null) { start.ArgumentList.Add("/DIR=" + target); }
        installer = Process.Start(start);
    });
    if (!applied || installer is null) { return 1; }
    using (installer) { await installer.WaitForExitAsync(); Console.WriteLine($"Manifesto e pacote verificados antes de iniciar o instalador; saída: {installer.ExitCode}."); return installer.ExitCode; }
}
Console.Error.WriteLine("Uso: init <chave-publica.txt> | sign <versao> <setup.exe> <manifesto.json> <chave-publica.txt>");
return 2;

Key LoadKey()
{
    var raw = ProtectedData.Unprotect(File.ReadAllBytes(keyPath), entropy, DataProtectionScope.CurrentUser);
    try { return Key.Import(SignatureAlgorithm.Ed25519, raw, KeyBlobFormat.RawPrivateKey); }
    finally { CryptographicOperations.ZeroMemory(raw); }
}
