using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Coconut.Prototypes.P2;

// P2 — uso (após dotnet build Coconut.slnx):
//   dotnet run --project prototypes/P2.MeshLocal -- 5        (N participantes, padrão 3)
//   P2.MeshLocal node <i> <dir> <url>                        (uso interno)

var json = new JsonSerializerOptions { WriteIndented = true };

if (args is ["node", var indexText, var nodeDir, var url])
{
    var index = int.Parse(indexText, CultureInfo.InvariantCulture);
    try
    {
        var result = await Node.RunAsync(index, nodeDir, new Uri(url));
        await File.WriteAllTextAsync(Path.Combine(nodeDir, $"node-{index}.json"), JsonSerializer.Serialize(result, json));
        return 0;
    }
#pragma warning disable CA1031 // O nó registra qualquer falha para o coordenador.
    catch (Exception ex)
#pragma warning restore CA1031
    {
        await File.WriteAllTextAsync(Path.Combine(nodeDir, $"node-{index}.error.txt"), ex.ToString());
        return 1;
    }
}

var nodes = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 3;
var repo = FindRepositoryRoot();
var serverDll = Path.Combine(repo, "src", "Coconut.Signaling.Server", "bin", "Debug", "net10.0", "Coconut.Signaling.Server.dll");
if (!File.Exists(serverDll))
{
    Console.Error.WriteLine($"Servidor não compilado: {serverDll}. Execute dotnet build Coconut.slnx.");
    return 2;
}

var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
var runDir = Path.Combine(repo, "prototypes", "P2.MeshLocal", "runs", $"{stamp}-n{nodes}");
Directory.CreateDirectory(runDir);

var port = FreeLoopbackPort();
var serverUrl = $"http://127.0.0.1:{port}";
using var server = StartProcess("dotnet", [serverDll, "--urls", serverUrl], Path.Combine(runDir, "server.log"));
await WaitHealthyAsync(serverUrl);

var total = Stopwatch.StartNew();
var exe = Environment.ProcessPath!;
var processes = Enumerable.Range(0, nodes)
    .Select(i => StartProcess(exe, ["node", i.ToString(CultureInfo.InvariantCulture), runDir, "ws://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/ws"], Path.Combine(runDir, $"node-{i}.log")))
    .ToList();

// Fases coordenadas por arquivos: todos admitidos → áudio → todos terminaram → parar.
var allAdmitted = await WaitFilesAsync(runDir, "admitted-", nodes, TimeSpan.FromSeconds(60));
var meshSeconds = total.Elapsed.TotalSeconds;
File.WriteAllText(Path.Combine(runDir, "audio.go"), "1");
var allAudio = allAdmitted && await WaitFilesAsync(runDir, "audio-done-", nodes, TimeSpan.FromSeconds(30));
await Task.Delay(500); // pacotes em trânsito
File.WriteAllText(Path.Combine(runDir, "stop"), "1");
await WaitFilesAsync(runDir, "reported-", nodes, TimeSpan.FromSeconds(20));
File.WriteAllText(Path.Combine(runDir, "leave"), "1");
foreach (var p in processes)
{
    if (!p.WaitForExit(TimeSpan.FromSeconds(20)))
    {
        p.Kill(entireProcessTree: true);
    }
}

server.Kill(entireProcessTree: true);

var results = Enumerable.Range(0, nodes)
    .Select(i => Path.Combine(runDir, $"node-{i}.json"))
    .Where(File.Exists)
    .Select(f => JsonSerializer.Deserialize<NodeResult>(File.ReadAllText(f))!)
    .ToList();
var errors = Directory.GetFiles(runDir, "node-*.error.txt").Select(File.ReadAllText).ToList();

var minFrames = (int)(Node.AudioFrames * 0.9);
var criteria = new Dictionary<string, bool>
{
    [$"C1 {nodes}/{nodes} nós admitidos"] = results.Count == nodes && results.All(r => r.Admitted),
    ["C2 mesma época e lista completa em todos"] = results.Count == nodes && results.Select(r => r.Epoch).Distinct().Count() == 1 && results.All(r => r.RosterSize == nodes),
    [$"C3 {nodes - 1} enlaces verificados em cada nó"] = results.Count == nodes && results.All(r => r.LinksVerified == nodes - 1),
    [$"C4 >= {minFrames}/{Node.AudioFrames} quadros decodificados de cada par"] = results.Count == nodes
        && results.All(r => r.AudioByPeer.Count == nodes - 1 && r.AudioByPeer.Values.All(a => a.DecodedFrames >= minFrames)),
    ["C5 nenhum candidato relay selecionado"] = results.All(r => !r.SelectedCandidateTypes.ContainsKey("relay")),
};
var success = allAudio && errors.Count == 0 && criteria.Values.All(v => v);

static double Median(IEnumerable<double> values)
{
    var list = values.Order().ToList();
    return list.Count == 0 ? double.NaN : list[list.Count / 2];
}

var summary = new
{
    stamp,
    nodes,
    success,
    meshFormedSeconds = allAdmitted ? meshSeconds : (double?)null,
    environment = new
    {
        os = Environment.OSVersion.VersionString,
        dotnet = Environment.Version.ToString(),
        processors = Environment.ProcessorCount,
        scenario = "local: servidor e N processos na mesma máquina, entradas simultâneas, sem servidores ICE",
    },
    criteria,
    admittedAfterMs = new { median = Median(results.Select(r => r.AdmittedAfterMs ?? double.NaN)), max = results.Select(r => r.AdmittedAfterMs).DefaultIfEmpty().Max() },
    linkVerifyMs = new { median = Median(results.Select(r => r.LinkVerifyMedianMs ?? double.NaN)), max = results.Select(r => r.LinkVerifyMaxMs).DefaultIfEmpty().Max() },
    cpuSecondsPerNode = new { median = Median(results.Select(r => r.CpuSeconds)), max = results.Select(r => r.CpuSeconds).DefaultIfEmpty().Max() },
    peakWorkingSetMbPerNode = new { median = Median(results.Select(r => r.PeakWorkingSetMb)), max = results.Select(r => r.PeakWorkingSetMb).DefaultIfEmpty().Max() },
    minDecodedFromAnyPeer = results.SelectMany(r => r.AudioByPeer.Values).Select(a => a.DecodedFrames).DefaultIfEmpty(0).Min(),
    diagnostics = results.SelectMany(r => r.Diagnostics).Distinct().ToList(),
    errors,
};
File.WriteAllText(Path.Combine(runDir, "summary.json"), JsonSerializer.Serialize(summary, json));

foreach (var (name, ok) in criteria)
{
    Console.WriteLine($"{(ok ? "OK  " : "FALHA")} {name}");
}

Console.WriteLine($"Malha formada em {meshSeconds:0.00} s · admissão máx {summary.admittedAfterMs.max:0} ms · CPU/nó mediana {summary.cpuSecondsPerNode.median:0.00} s · RAM/nó máx {summary.peakWorkingSetMbPerNode.max:0} MB");
Console.WriteLine($"Resultado: {(success ? "SUCESSO" : "FALHA")} — {runDir}");
return success ? 0 : 1;

static Process StartProcess(string fileName, string[] arguments, string logPath)
{
    var info = new ProcessStartInfo(fileName) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var a in arguments)
    {
        info.ArgumentList.Add(a);
    }

    var process = Process.Start(info)!;
    var gate = new object();
    void Append(string? line)
    {
        if (line is not null)
        {
            lock (gate)
            {
                File.AppendAllText(logPath, line + Environment.NewLine);
            }
        }
    }

    process.OutputDataReceived += (_, e) => Append(e.Data);
    process.ErrorDataReceived += (_, e) => Append(e.Data);
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    return process;
}

static async Task<bool> WaitFilesAsync(string dir, string prefix, int count, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        if (Directory.GetFiles(dir, prefix + "*").Count(f => !f.EndsWith(".tmp", StringComparison.Ordinal)) >= count)
        {
            return true;
        }

        await Task.Delay(50);
    }

    return false;
}

static async Task WaitHealthyAsync(string url)
{
    using var http = new HttpClient();
    for (var i = 0; i < 100; i++)
    {
        try
        {
            if ((await http.GetAsync(new Uri(url + "/healthz"))).StatusCode == HttpStatusCode.OK)
            {
                return;
            }
        }
        catch (HttpRequestException)
        {
        }

        await Task.Delay(100);
    }

    throw new TimeoutException("Servidor de sinalização não respondeu.");
}

static int FreeLoopbackPort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

static string FindRepositoryRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Coconut.slnx")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName ?? throw new InvalidOperationException("Coconut.slnx não encontrado.");
}
