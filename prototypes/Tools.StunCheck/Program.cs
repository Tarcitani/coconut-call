using System.Collections.Concurrent;
using Coconut.Media.Native;

// Diagnóstico: a pilha WebRTC do Coconut (libdatachannel/libjuice) obtém candidato reflexivo (srflx)
// pelo servidor STUN indicado? Mostra só tipos de candidato, nunca endereços.
// Uso: Tools.StunCheck stun:host:3478 [segundos]

if (args.Length < 1)
{
    Console.Error.WriteLine("Uso: Tools.StunCheck stun:host:3478 [segundos]");
    return 2;
}

var seconds = args.Length > 1 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 5;
var types = new ConcurrentDictionary<string, int>();
using var peer = new RtcPeer(new RtcPeerOptions { StunServers = [args[0]] });
peer.LocalCandidateReady += (candidate, _) =>
{
    var parts = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var index = Array.IndexOf(parts, "typ");
    var type = index >= 0 && index + 1 < parts.Length ? parts[index + 1] : "?";
    var family = parts.Length > 4 && parts[4].Contains(':', StringComparison.Ordinal) ? "IPv6" : "IPv4";
    types.AddOrUpdate($"{type}/{family}", 1, (_, n) => n + 1);
};
peer.CreateDataChannel("check");
peer.SetLocalDescription("offer");
await Task.Delay(TimeSpan.FromSeconds(seconds));

foreach (var (type, count) in types.OrderBy(t => t.Key))
{
    Console.WriteLine($"{type}: {count}");
}

var ok = types.Keys.Any(k => k.StartsWith("srflx", StringComparison.Ordinal));
Console.WriteLine(ok ? "SRFLX OBTIDO pelo STUN informado" : "NENHUM candidato srflx");
return ok ? 0 : 1;
