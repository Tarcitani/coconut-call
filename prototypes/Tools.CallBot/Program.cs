using Coconut.Client;
using Coconut.Media.Native;

// Ferramenta de teste: participante sem áudio (não envia nem reproduz nada). Entra numa sala e relata eventos.
// Uso: Tools.CallBot <ws://servidor/ws> <convite> [nome] [segundos]

if (args.Length < 2)
{
    Console.Error.WriteLine("Uso: Tools.CallBot <ws://servidor/ws> <convite> [nome] [segundos]");
    return 2;
}

var name = args.Length > 2 ? args[2] : "Bot de teste";
var seconds = args.Length > 3 ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 30;
var ended = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

await using var session = new MeshSession(new RtcPeerOptions());
session.Admitted += () => Console.WriteLine($"admitido; participantes: {session.Roster.Count}");
session.LinkVerified += (_, after) => Console.WriteLine($"enlace verificado em {after.TotalMilliseconds:0} ms");
session.Ended += reason => ended.TrySetResult(reason);
session.AdmissionRejected += failure => Console.WriteLine($"admissão recusada: {failure.Reason}");

await session.JoinRoomAsync(new Uri(args[0]), args[1], name, CancellationToken.None);
var finished = await Task.WhenAny(ended.Task, Task.Delay(TimeSpan.FromSeconds(seconds)));
if (finished == ended.Task)
{
    Console.WriteLine($"fim: {ended.Task.Result}");
}
else
{
    Console.WriteLine("tempo esgotado; saindo");
    await session.LeaveAsync();
}

return 0;
