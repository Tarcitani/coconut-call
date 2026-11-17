using System.Globalization;
using System.Text.RegularExpressions;

namespace Coconut.App.Core.Services;

/// <summary>Registro técnico local (requisito SEC-2). Nunca deve lançar exceção para quem chama.</summary>
public interface IDiagnosticLog
{
    void Write(string message);
}

public sealed class NullDiagnosticLog : IDiagnosticLog
{
    public static readonly NullDiagnosticLog Instance = new();

    public void Write(string message)
    {
    }
}

/// <summary>
/// Remove do texto o que não pode ir para log: endereços IP (inclusive no formato do sslip.io), convites,
/// tokens e trechos de SDP/candidatos ICE. Aplicado a toda linha, inclusive mensagens de bibliotecas.
/// </summary>
public static partial class LogSanitizer
{
    public static string Clean(string message)
    {
        if (SdpOrCandidate().IsMatch(message))
        {
            message = SdpOrCandidate().Replace(message, "<sdp removido>");
        }

        message = InviteLink().Replace(message, "<convite>");
        message = InviteCode().Replace(message, "<convite>");
        message = Ipv4().Replace(message, "<ip>");
        message = HyphenatedIpv4().Replace(message, "<ip>");
        message = Ipv6().Replace(message, "<ip>");
        message = Token().Replace(message, "<segredo>");
        return message.ReplaceLineEndings(" ");
    }

    [GeneratedRegex(@"(?i)(\ba=\S.*|candidate:.*|\bv=0\b.*)")]
    private static partial Regex SdpOrCandidate();

    [GeneratedRegex(@"(?i)coconut://\S+")]
    private static partial Regex InviteLink();

    [GeneratedRegex(@"\b[0-9A-Z]{5}-[0-9A-Z]{5}\b")]
    private static partial Regex InviteCode();

    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b")]
    private static partial Regex Ipv4();

    [GeneratedRegex(@"\b\d{1,3}(?:-\d{1,3}){3}\b")]
    private static partial Regex HyphenatedIpv4();

    // Pelo menos dois ":" entre grupos hexadecimais (também cobre "::1" e endereços com zona/porta).
    [GeneratedRegex(@"(?i)(?<![\w:])\[?(?:[0-9a-f]{0,4}:){2,7}[0-9a-f]{0,4}(?:%\w+)?\]?(?![\w:])")]
    private static partial Regex Ipv6();

    [GeneratedRegex(@"[A-Za-z0-9_\-+/=]{32,}")]
    private static partial Regex Token();
}

/// <summary>
/// Um arquivo por dia em %LOCALAPPDATA%\Coconut\logs (coconut-AAAAMMDD.log), retenção de 7 dias e
/// teto por arquivo. Fica só nesta máquina; a pessoa decide se envia.
/// </summary>
public sealed class FileDiagnosticLog : IDiagnosticLog
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    public const long MaxFileBytes = 5 * 1024 * 1024;

    private readonly Lock _gate = new();
    private readonly TimeProvider _clock;

    public FileDiagnosticLog(string? directory = null, TimeProvider? clock = null)
    {
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Coconut", "logs");
        _clock = clock ?? TimeProvider.System;
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            PruneOldFiles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // sem log é melhor que sem app
        }
    }

    public string Directory { get; }

    public void Write(string message)
    {
        var now = _clock.GetLocalNow();
        var line = $"{now:yyyy-MM-dd HH:mm:ss.fff} {LogSanitizer.Clean(message)}{Environment.NewLine}";
        var path = Path.Combine(Directory, $"coconut-{now:yyyyMMdd}.log");
        lock (_gate)
        {
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length >= MaxFileBytes)
                {
                    return;
                }

                File.AppendAllText(path, line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // idem: o registro nunca interrompe a chamada
            }
        }
    }

    private void PruneOldFiles()
    {
        var oldest = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime - Retention);
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "coconut-*.log"))
        {
            var stamp = Path.GetFileNameWithoutExtension(file)["coconut-".Length..];
            if (DateOnly.TryParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day < oldest)
            {
                File.Delete(file);
            }
        }
    }
}
