using System.Net;

namespace Coconut.App.Core.Services;

/// <summary>
/// O STUN do Coconut roda no mesmo host da sinalização (porta UDP 3478): nenhum terceiro vê os endereços.
/// Servidor local (loopback) dispensa STUN — os pares estão na mesma máquina.
/// </summary>
public static class ServerEndpoints
{
    public const int StunPort = 3478;

    public static IReadOnlyList<string> StunServersFor(Uri signaling)
    {
        ArgumentNullException.ThrowIfNull(signaling);
        if (signaling.IsLoopback || (IPAddress.TryParse(signaling.DnsSafeHost, out var ip) && IPAddress.IsLoopback(ip)))
        {
            return [];
        }

        return [$"stun:{signaling.Host}:{StunPort}"];
    }
}
