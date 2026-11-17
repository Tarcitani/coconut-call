using System.IO.Compression;
using System.Text;

namespace Coconut.App.Core.Services;

public static class DiagnosticExporter
{
    /// <summary>Only bounded Coconut logs, sanitized again; no settings, server address, signing key or media.</summary>
    public static async Task ExportAsync(string logsDirectory, string destination, CancellationToken cancellation = default)
    {
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        if (!Directory.Exists(logsDirectory)) { return; }
        foreach (var path in Directory.EnumerateFiles(logsDirectory, "coconut-????????.log").OrderByDescending(Path.GetFileName).Take(8))
        {
            if (!DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(path)[8..], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)) { continue; }
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var bounded = new MemoryStream(); var buffer = new byte[8192];
            for (var read = await input.ReadAsync(buffer, cancellation); read != 0; read = await input.ReadAsync(buffer, cancellation))
            {
                if (bounded.Length + read > FileDiagnosticLog.MaxFileBytes + 8192) { break; }
                bounded.Write(buffer, 0, read);
            }
            var entry = zip.CreateEntry(Path.GetFileName(path), CompressionLevel.Optimal);
            await using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            using var reader = new StringReader(Encoding.UTF8.GetString(bounded.ToArray()));
            while (reader.ReadLine() is { } line) { await writer.WriteLineAsync(LogSanitizer.Clean(line).AsMemory(), cancellation); }
        }
    }
}
