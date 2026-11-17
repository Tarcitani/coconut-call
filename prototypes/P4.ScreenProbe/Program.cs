using System.Diagnostics;
using Coconut.Client.Screen;
using Coconut.Screen;
using Coconut.Signaling.Contracts;
using Coconut.Client;
using Coconut.Media.Native;

if (args.Contains("--network", StringComparer.Ordinal)) { return await NetworkAsync(args); }

// Local only: capture one explicitly selected monitor; encode, RTP fragment/reassemble and decode.
// No network and no video file. Exit success requires decoded GPU frames, not just encoded bytes.
var monitors = MonitorCatalog.List();
Console.WriteLine($"monitores: {monitors.Count}");
var index = args.Length > 0 && int.TryParse(args[0], out var selected) ? selected : 0;
if (index < 0 || index >= monitors.Count) { return 2; }
var options = new ScreenOptions(Option("--height", 720), Option("--fps", 30), 2_000_000, args.Contains("--software", StringComparer.Ordinal));
var dimensions = options.Dimensions(monitors[index]);
using var renderer = new ScreenRenderer(dimensions.Width, dimensions.Height);
var receiver = new ScreenRtpReceiver(); var peer = ParticipantId.New(); var session = ShareSessionId.New(); receiver.Authorize(peer, session);
ushort sequence = 65000; long encoded = 0, bytes = 0, rebuilt = 0; int error = 0; var clock = Stopwatch.StartNew();
var parameters = new H264ParameterSets();
using var capture = ScreenCapture.Start(monitors[index], options, (data, time, key, header) =>
{
    Interlocked.Increment(ref encoded); Interlocked.Add(ref bytes, data.Length);
    var normalized = parameters.Normalize(data, header);
    var frame = new ScreenFrame(normalized.AnnexB, dimensions.Width, dimensions.Height, (uint)(time * 9 / 1000), normalized.IsKeyframe);
    foreach (var packet in ScreenRtp.Packetize(session, frame, 123456, ref sequence))
    {
        if (receiver.Push(peer, packet) is { } complete)
        {
            Interlocked.Increment(ref rebuilt); renderer.Push(complete.AnnexB, time);
        }
    }
}, code => { Interlocked.Exchange(ref error, code); });
Console.WriteLine($"hardware: {capture.IsHardware}; {capture.Width}x{capture.Height}");
await Task.Delay(TimeSpan.FromSeconds(8));
capture.Dispose();
Console.WriteLine($"tempo: {clock.Elapsed.TotalSeconds:0.0}s; codificados: {encoded}; remontados: {rebuilt}; decodificados: {renderer.Frames}; bytes: {bytes}; erro: 0x{error:X8}");
return error == 0 && renderer.Frames >= 30 ? 0 : 1;

int Option(string name, int fallback)
{
    var position = Array.IndexOf(args, name); return position >= 0 && position + 1 < args.Length && int.TryParse(args[position + 1], out var value) ? value : fallback;
}

static async Task<int> NetworkAsync(string[] arguments)
{
    var count = arguments.Length > 1 && int.TryParse(arguments[1], out var n) ? n : 2;
    if (count is < 2 or > 15) { return 2; }
    var sessions = new List<MeshSession>(); var screens = new List<ScreenShareController>();
    var renderers = new List<ScreenRenderer>(); var decoded = new long[count]; var audio = new long[count]; var reports = 0; var reconnects = 0;
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    try
    {
        for (var i = 0; i < count; i++)
        {
            if (i == count - 1 && arguments.Contains("--latejoin", StringComparer.Ordinal))
            {
                await screens[0].RequestAsync(MonitorCatalog.List().First(m => m.IsPrimary), new ScreenOptions());
                await Task.Delay(1500, stop.Token);
            }
            var session = new MeshSession(new RtcPeerOptions()); sessions.Add(session);
            if (i == 0) { session.ScreenLossReport += (peer, loss) => Interlocked.Increment(ref reports); }
            if (i == 0) { session.SignalingConnectivityChanged += connected => { if (connected) { Interlocked.Increment(ref reconnects); } }; }
            var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Admitted += () => admitted.TrySetResult();
            session.Diagnostic += message => Console.WriteLine($"cliente: {message}");
            if (i == 0) { await session.CreateRoomAsync(new Uri("ws://127.0.0.1:5080/ws"), "Tela", stop.Token); }
            else { await session.JoinRoomAsync(new Uri("ws://127.0.0.1:5080/ws"), sessions[0].InviteCode!, $"Receptor{i}", stop.Token); }
            await admitted.Task.WaitAsync(stop.Token);
            var controller = new ScreenShareController(session); screens.Add(controller);
            controller.Problem += message => Console.WriteLine($"aviso: {message}");
            var capturedIndex = i;
            session.AudioReceived += (peer, packet) => { Interlocked.Increment(ref audio[capturedIndex]); };
            if (i > 0)
            {
                var renderer = new ScreenRenderer(1280, 720); renderers.Add(renderer);
                controller.FrameReceived += envelope =>
                {
                    try { renderer.Push(envelope.Frame.AnnexB, (long)envelope.Frame.Timestamp * 1000 / 9); Interlocked.Increment(ref decoded[capturedIndex]); }
                    catch (Exception ex) { Console.WriteLine($"decode: 0x{ex.HResult:X8}"); }
                };
            }
        }
        await Task.Delay(300, stop.Token);
        var monitor = MonitorCatalog.List().First(m => m.IsPrimary);
        await screens[0].RequestAsync(monitor, new ScreenOptions());
        using var opus = new OpusEncoder(); var pcm = new short[OpusCodec.FrameSamples];
        uint timestamp = 0; var encodedAudio = new byte[4000];
        var timer = Stopwatch.StartNew(); var interrupted = false; var firstIdentity = sessions[0].You;
        while (timer.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (!interrupted && arguments.Contains("--reconnect", StringComparer.Ordinal) && timer.Elapsed > TimeSpan.FromSeconds(3))
            {
                interrupted = true;
                // Fault injection into the real WebSocket, preserving the server's room/session state.
                var transport = (SignalingConnection)typeof(MeshSession).GetField("_signaling", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(sessions[0])!;
                transport.Abort();
            }
            var length = opus.Encode(pcm, encodedAudio);
            foreach (var session in sessions) { session.SendAudio(encodedAudio.AsSpan(0, length), timestamp); }
            timestamp += OpusCodec.FrameSamples;
            await Task.Delay(20, stop.Token);
        }
        await screens[0].StopAsync();
        Console.WriteLine($"pares: {count}; codificacoes: {screens[0].EncodedFrames}; bytes entregues: {screens[0].DeliveredBytes}; RTCP RR: {reports}; retomadas: {reconnects}; identidade preservada: {sessions[0].You == firstIdentity}");
        for (var i = 1; i < count; i++) { Console.WriteLine($"receptor {i}: quadros {decoded[i]}, GPU {renderers[i - 1].Frames}, audio {audio[i]}"); }
        return Enumerable.Range(1, count - 1).All(i => decoded[i] >= 30 && renderers[i - 1].Frames >= 25 && audio[i] >= 100) &&
            (!interrupted || reconnects > 0 && sessions[0].You == firstIdentity) && reports >= count - 1 ? 0 : 1;
    }
    finally
    {
        foreach (var screen in screens) { await screen.DisposeAsync(); }
        foreach (var session in sessions) { await session.DisposeAsync(); }
        foreach (var renderer in renderers) { renderer.Dispose(); }
    }
}
