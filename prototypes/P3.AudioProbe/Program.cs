using System.Diagnostics;
using Coconut.Audio;


// P3 — sonda de áudio. Mede, não grava: o microfone é lido por 2 s só para contar quadros e calcular o nível.
// A saída recebe silêncio digital (nenhum som audível).

using var catalog = new AudioDeviceCatalog();
var changes = 0;
catalog.DevicesChanged += () => Interlocked.Increment(ref changes);

Console.WriteLine("== Dispositivos (padrão de comunicação marcado com *)");
foreach (var direction in new[] { AudioDirection.Capture, AudioDirection.Render })
{
    foreach (var d in catalog.List(direction))
    {
        Console.WriteLine($"  {(direction == AudioDirection.Capture ? "entrada" : "saída  ")} {(d.IsDefault ? "*" : " ")} {d.Name}");
    }
}

var renderDefault = catalog.DefaultDeviceId(AudioDirection.Render);

foreach (var communications in new[] { true, false })
{
    Console.WriteLine($"\n== Captura padrão, categoria {(communications ? "Communications" : "padrão (Other)")}");
    var frames = 0;
    double sumSquares = 0;
    long samples = 0;
    var clock = Stopwatch.StartNew();
    var options = new AudioStreamOptions
    {
        CommunicationsCategory = communications,
        EchoReferenceRenderDeviceId = communications ? renderDefault : null,
    };
    try
    {
        var (capture, info) = await AudioCapture.StartAsync(catalog, options, frame =>
        {
            frames++;
            foreach (var s in frame)
            {
                sumSquares += (double)s * s;
            }

            samples += frame.Length;
        });
        await Task.Delay(TimeSpan.FromSeconds(1));
        var during = capture.RefreshEffects();
        await Task.Delay(TimeSpan.FromSeconds(1));
        capture.Dispose();
        var elapsed = clock.Elapsed.TotalSeconds;
        Console.WriteLine("  efeitos após 1 s em execução: " + (during is null ? "API indisponível" : string.Join("; ", during.Select(e => $"{e.Name} [{(e.IsOn ? "ligado" : "desligado")}]"))));
        Console.WriteLine($"  buffer {info.BufferMs:0.0} ms · latência informada {info.LatencyMs:0.0} ms");
        Console.WriteLine($"  quadros de 20 ms: {frames} em {elapsed:0.00} s (esperado ~{elapsed / 0.02:0}) · RMS {Math.Sqrt(sumSquares / Math.Max(1, samples)):0.0}");
        Console.WriteLine($"  referência de AEC controlável: {info.EchoReferenceControllable?.ToString() ?? "desconhecido"}");
        Console.WriteLine(info.Effects is null
            ? "  efeitos: API indisponível"
            : "  efeitos: " + (info.Effects.Count == 0 ? "nenhum" : string.Join("; ", info.Effects.Select(e => $"{e.Name} [{(e.IsOn ? "ligado" : "desligado")}{(e.CanSetState ? ", ajustável" : string.Empty)}]"))));
    }
    catch (Exception ex) when (ex is AudioDeviceUnavailableException or System.Runtime.InteropServices.COMException)
    {
        Console.WriteLine($"  FALHA: {ex.GetType().Name}: {ex.Message}");
    }
}

Console.WriteLine("\n== Reprodução padrão (silêncio digital, categoria Communications)");
var produced = 0;
var playClock = Stopwatch.StartNew();
try
{
    var (playback, playInfo) = await AudioPlayback.StartAsync(catalog, new AudioStreamOptions(), frame =>
    {
        frame.Clear();
        produced++;
    });
    await Task.Delay(TimeSpan.FromSeconds(2));
    playback.Dispose();
    var elapsed = playClock.Elapsed.TotalSeconds;
    Console.WriteLine($"  buffer {playInfo.BufferMs:0.0} ms · latência informada {playInfo.LatencyMs:0.0} ms");
    Console.WriteLine($"  quadros consumidos: {produced} em {elapsed:0.00} s (esperado ~{elapsed / 0.02:0})");
    Console.WriteLine(playInfo.Effects is null
        ? "  efeitos: API indisponível"
        : "  efeitos: " + (playInfo.Effects.Count == 0 ? "nenhum" : string.Join("; ", playInfo.Effects.Select(e => $"{e.Name} [{(e.IsOn ? "ligado" : "desligado")}]"))));
}
catch (Exception ex) when (ex is AudioDeviceUnavailableException or System.Runtime.InteropServices.COMException)
{
    Console.WriteLine($"  FALHA: {ex.GetType().Name}: {ex.Message}");
}

Console.WriteLine($"\nNotificações de dispositivo recebidas durante a sonda: {changes}");

Console.WriteLine("\n== Motor de voz nos dispositivos reais (mudo ligado, sem pares: nada é transmitido nem ouvido)");
{
    await using var session = new Coconut.Client.MeshSession(new Coconut.Media.Native.RtcPeerOptions());
    await using var engine = new Coconut.Client.Voice.VoiceEngine(session, catalog) { IsMuted = true };
    var problems = new List<string>();
    engine.Problem += problems.Add;
    var engineClock = Stopwatch.StartNew();
    await engine.StartAsync();
    await Task.Delay(TimeSpan.FromSeconds(2));
    var stats = engine.TransmitStats;
    Console.WriteLine($"  entrada: {(engine.CaptureInfo is null ? "não aberta" : "aberta")} · saída: {(engine.PlaybackInfo is null ? "não aberta" : "aberta")}");
    Console.WriteLine($"  em {engineClock.Elapsed.TotalSeconds:0.00} s: capturados {stats.FramesCaptured}, codificados {stats.FramesEncoded}, suprimidos pelo mudo {stats.FramesSuppressedByMute}, entregas {stats.Deliveries}");
    Console.WriteLine($"  efeitos ativos na entrada: {string.Join("; ", engine.CaptureInfo?.Effects?.Where(e => e.IsOn).Select(e => e.Name) ?? [])}");
    Console.WriteLine($"  problemas reportados: {(problems.Count == 0 ? "nenhum" : string.Join(" | ", problems))}");
}
