using System.Diagnostics;
using System.Text.Json;
using Coconut.Prototypes.P1;

// P1 — uso:
//   dotnet run --project prototypes/P1.WebRtcLoopback            (coordenador: inicia dois processos)
//   P1.WebRtcLoopback peer offerer|answerer <dir>                 (uso interno)

var json = new JsonSerializerOptions { WriteIndented = true };

if (args is ["peer", var role, var dir])
{
    try
    {
        object result = role == "offerer" ? Roles.RunOfferer(dir) : Roles.RunAnswerer(dir);
        File.WriteAllText(Path.Combine(dir, role + ".json"), JsonSerializer.Serialize(result, result.GetType(), json));
        return 0;
    }
#pragma warning disable CA1031 // O processo filho registra qualquer falha para o coordenador.
    catch (Exception ex)
#pragma warning restore CA1031
    {
        File.WriteAllText(Path.Combine(dir, role + ".error.txt"), ex.ToString());
        return 1;
    }
}

var outRoot = args is ["--out", var o] ? o : Path.Combine("prototypes", "P1.WebRtcLoopback", "runs");
var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
var runDir = Path.GetFullPath(Path.Combine(outRoot, stamp));
Directory.CreateDirectory(runDir);

var exe = Environment.ProcessPath!;
var total = Stopwatch.StartNew();
var answerer = Start("answerer");
var offerer = Start("offerer");
var finished = offerer.WaitForExit(TimeSpan.FromSeconds(90)) & answerer.WaitForExit(TimeSpan.FromSeconds(90));
total.Stop();

var offererJson = ReadIfExists("offerer.json");
var answererJson = ReadIfExists("answerer.json");
var off = offererJson is null ? null : JsonSerializer.Deserialize<OffererResult>(offererJson);
var ans = answererJson is null ? null : JsonSerializer.Deserialize<AnswererResult>(answererJson);

var criteria = new Dictionary<string, bool>
{
    ["C1 conectado nos dois processos"] = off?.Connected == true && ans?.Connected == true,
    ["C2 sem candidatos relay (nenhum servidor ICE configurado)"] = off is not null
        && !off.Offer.CandidateTypes.ContainsKey("relay") && !off.Answer.CandidateTypes.ContainsKey("relay")
        && off.SelectedLocalType != "relay" && off.SelectedRemoteType != "relay",
    ["C3 DTLS-SRTP: perfil UDP/TLS/RTP/SAVPF e fingerprint nos dois SDPs"] = off is not null
        && off.Offer.MediaProfileSavpf && off.Offer.DtlsFingerprint && off.Answer.MediaProfileSavpf && off.Answer.DtlsFingerprint,
    ["C4 canal de dados: 20/20 ecos"] = off?.PongsReceived == Roles.Pings,
    ["C5 Opus: >=140/150 quadros decodificados"] = ans?.DecodedFrames >= 140,
    ["C6 timestamps RTP monotônicos (passo 960 em >=95%)"] = ans?.TimestampStepOkRatio >= 0.95,
    ["C7 tom 440 Hz íntegro (>=20 dB acima de 1 kHz)"] = ans?.ToneToOffBandRatio >= 100,
};
var success = finished && criteria.Values.All(v => v);

var summary = new
{
    stamp,
    success,
    finished,
    totalSeconds = total.Elapsed.TotalSeconds,
    environment = new
    {
        os = Environment.OSVersion.VersionString,
        dotnet = Environment.Version.ToString(),
        processors = Environment.ProcessorCount,
        scenario = "local: dois processos na mesma máquina, sem servidores ICE",
    },
    criteria,
    offerer = off,
    answerer = ans,
    errors = new[] { ReadIfExists("offerer.error.txt"), ReadIfExists("answerer.error.txt") }.Where(e => e is not null),
};
File.WriteAllText(Path.Combine(runDir, "summary.json"), JsonSerializer.Serialize(summary, json));

foreach (var (name, ok) in criteria)
{
    Console.WriteLine($"{(ok ? "OK  " : "FALHA")} {name}");
}

Console.WriteLine($"Resultado: {(success ? "SUCESSO" : "FALHA")} — evidências em {runDir}");
return success ? 0 : 1;

Process Start(string role)
{
    var info = new ProcessStartInfo(exe)
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    info.ArgumentList.Add("peer");
    info.ArgumentList.Add(role);
    info.ArgumentList.Add(runDir);
    var process = Process.Start(info)!;
    var log = Path.Combine(runDir, role + ".log");
    process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (log) { File.AppendAllText(log, e.Data + Environment.NewLine); } } };
    process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (log) { File.AppendAllText(log, e.Data + Environment.NewLine); } } };
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    return process;
}

string? ReadIfExists(string name)
{
    var path = Path.Combine(runDir, name);
    return File.Exists(path) ? File.ReadAllText(path) : null;
}
