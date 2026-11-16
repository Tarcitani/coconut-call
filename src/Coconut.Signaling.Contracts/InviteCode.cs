using System.Text;

namespace Coconut.Signaling.Contracts;

/// <summary>
/// Formato do convite: 10 caracteres em base32 de Crockford (50 bits). Fonte única para servidor e cliente.
/// Aceita minúsculas, hífens e espaços, e corrige O→0 e I/L→1. Também aceita o link <c>coconut://join/CODIGO</c>.
/// </summary>
public static class InviteCode
{
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const int Length = 10;
    public const string LinkPrefix = "coconut://join/";

    public static string? TryNormalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var text = input.Trim();
        if (text.StartsWith(LinkPrefix, StringComparison.OrdinalIgnoreCase))
        {
            text = text[LinkPrefix.Length..].TrimEnd('/');
        }

        if (text.Length > 32)
        {
            return null;
        }

        var builder = new StringBuilder(Length);
        foreach (var raw in text)
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
            if (!Alphabet.Contains(c, StringComparison.Ordinal))
            {
                return null;
            }

            builder.Append(c);
        }

        return builder.Length == Length ? builder.ToString() : null;
    }

    /// <summary>Exibição legível: "ABCDE-12345".</summary>
    public static string Format(string code) => code.Length == Length ? $"{code[..5]}-{code[5..]}" : code;

    public static string ToLink(string code) => LinkPrefix + code;
}
