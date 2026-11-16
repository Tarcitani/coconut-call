using System.Security.Cryptography;
using System.Text;

namespace Coconut.Signaling.Server;

/// <summary>Convites e tokens de sessão gerados por CSPRNG. Nenhum deles é registrado em log.</summary>
internal static class Secrets
{
    // Base32 de Crockford: sem I, L, O, U — legível e fácil de ditar.
    private const string InviteAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const int InviteLength = 10; // 10 × 5 bits = 50 bits de entropia

    public static string NewInviteCode() => RandomNumberGenerator.GetString(InviteAlphabet, InviteLength);

    /// <summary>Normaliza o que o usuário digitou ou o que veio de um link; nulo se o formato for inválido.</summary>
    public static string? NormalizeInvite(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 32)
        {
            return null;
        }

        var builder = new StringBuilder(InviteLength);
        foreach (var raw in input)
        {
            if (raw is '-' or ' ')
            {
                continue;
            }

            var c = char.ToUpperInvariant(raw) switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                var other => other,
            };
            if (!InviteAlphabet.Contains(c, StringComparison.Ordinal))
            {
                return null;
            }

            builder.Append(c);
        }

        return builder.Length == InviteLength ? builder.ToString() : null;
    }

    /// <summary>Token de retomada: 256 bits, base64url.</summary>
    public static string NewResumeToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>O servidor guarda só o hash do token.</summary>
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Identificador interno para logs; não revela o convite.</summary>
    public static string NewLogHandle() => Convert.ToHexString(RandomNumberGenerator.GetBytes(4));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
