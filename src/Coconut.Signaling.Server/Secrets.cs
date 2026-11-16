using System.Security.Cryptography;
using System.Text;
using Coconut.Signaling.Contracts;

namespace Coconut.Signaling.Server;

/// <summary>Convites e tokens de sessão gerados por CSPRNG. Nenhum deles é registrado em log.</summary>
internal static class Secrets
{
    public static string NewInviteCode() => RandomNumberGenerator.GetString(InviteCode.Alphabet, InviteCode.Length);

    /// <summary>Normaliza o que o usuário digitou ou o que veio de um link; nulo se o formato for inválido.</summary>
    public static string? NormalizeInvite(string? input) => InviteCode.TryNormalize(input);

    /// <summary>Token de retomada: 256 bits, base64url.</summary>
    public static string NewResumeToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>O servidor guarda só o hash do token.</summary>
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Identificador interno para logs; não revela o convite.</summary>
    public static string NewLogHandle() => Convert.ToHexString(RandomNumberGenerator.GetBytes(4));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
