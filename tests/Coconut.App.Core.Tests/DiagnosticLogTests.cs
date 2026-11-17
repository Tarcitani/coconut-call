using Coconut.App.Core.Services;

namespace Coconut.App.Core.Tests;

public sealed class DiagnosticLogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coconut-log-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("conectado a 203.0.113.7:3478", "203.0.113.7")]
    [InlineData("servidor wss://203-0-113-7.sslip.io/ws", "203-0-113-7")]
    [InlineData("par 2001:db8::1 respondeu", "2001:db8::1")]
    [InlineData("loopback [::1]:5080", "::1")]
    [InlineData("link fe80::1%12 ativo", "fe80::1")]
    [InlineData("convite ABCDE-12345 colado", "ABCDE-12345")]
    [InlineData("abriu coconut://join/ABCDE12345", "ABCDE12345")]
    [InlineData("token Zm9vYmFyYmF6cXV4cXV1eGNvcmdlZ3JhdWx0Z2FycGx5", "Zm9vYmFyYmF6cXV4cXV1eGNvcmdlZ3JhdWx0Z2FycGx5")]
    [InlineData("rejeitado: a=candidate:1 1 UDP 2122 198.51.100.4 50000 typ host", "198.51.100.4")]
    [InlineData("falhou candidate:842163049 1 udp 1677729535 10.0.0.2 4000 typ srflx", "10.0.0.2")]
    public void Sanitizer_removes_network_identifiers_and_secrets(string message, string secret)
    {
        var clean = LogSanitizer.Clean(message);

        Assert.DoesNotContain(secret, clean, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitizer_keeps_useful_diagnostics()
    {
        const string message = "microfone: falha ao abrir: COMException 0x88890004; conexão srflx/host; pacotes 491";

        Assert.Equal(message, LogSanitizer.Clean(message));
    }

    [Fact]
    public void Writes_sanitized_line_to_daily_file()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 16, 21, 5, 0, TimeSpan.Zero));
        var log = new FileDiagnosticLog(_dir, clock);

        log.Write("par 192.0.2.10 conectado");

        var text = File.ReadAllText(Path.Combine(_dir, "coconut-20261116.log"));
        Assert.Contains("2026-11-16 21:05:00.000 par <ip> conectado", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Deletes_files_older_than_retention()
    {
        File.WriteAllText(Path.Combine(_dir, "coconut-20261101.log"), "antigo");
        File.WriteAllText(Path.Combine(_dir, "coconut-20261112.log"), "recente");
        File.WriteAllText(Path.Combine(_dir, "outro.txt"), "não é log");
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));

        _ = new FileDiagnosticLog(_dir, clock);

        Assert.False(File.Exists(Path.Combine(_dir, "coconut-20261101.log")));
        Assert.True(File.Exists(Path.Combine(_dir, "coconut-20261112.log")));
        Assert.True(File.Exists(Path.Combine(_dir, "outro.txt")));
    }

    [Fact]
    public void Stops_writing_when_daily_file_reaches_cap()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        var path = Path.Combine(_dir, "coconut-20261116.log");
        File.WriteAllBytes(path, new byte[FileDiagnosticLog.MaxFileBytes]);
        var log = new FileDiagnosticLog(_dir, clock);

        log.Write("mais uma linha");

        Assert.Equal(FileDiagnosticLog.MaxFileBytes, new FileInfo(path).Length);
    }

    [Fact]
    public void Unwritable_directory_never_throws()
    {
        var file = Path.Combine(_dir, "arquivo");
        File.WriteAllText(file, "x");
        var log = new FileDiagnosticLog(Path.Combine(file, "sub")); // diretório impossível

        log.Write("teste");
    }
}

/// <summary>Relógio parado em UTC (o nome do arquivo e o carimbo usam a hora local do relógio).</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
