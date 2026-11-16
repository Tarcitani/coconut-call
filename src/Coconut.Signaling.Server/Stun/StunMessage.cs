using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Coconut.Signaling.Server.Stun;

/// <summary>
/// Subconjunto da RFC 5389 necessário a um servidor STUN de descoberta: Binding Request → Binding Success
/// Response com XOR-MAPPED-ADDRESS. Sem autenticação, sem TURN. Mensagens fora do formato são descartadas.
/// </summary>
internal static class StunMessage
{
    public const int HeaderLength = 20;
    public const int TransactionIdLength = 12;
    public const uint MagicCookie = 0x2112A442;
    public const ushort BindingRequest = 0x0001;
    public const ushort BindingSuccess = 0x0101;
    public const ushort XorMappedAddress = 0x0020;

    /// <summary>Maior resposta possível (IPv6): cabeçalho + atributo de 4 + 20 bytes.</summary>
    public const int MaxResponseLength = HeaderLength + 4 + 20;

    /// <summary>Aceita só Binding Request válido; devolve o ID de transação para ecoar na resposta.</summary>
    public static bool TryParseBindingRequest(ReadOnlySpan<byte> message, out ReadOnlySpan<byte> transactionId)
    {
        transactionId = default;
        if (message.Length < HeaderLength || (message[0] & 0xC0) != 0)
        {
            return false; // os dois primeiros bits de toda mensagem STUN são zero
        }

        var type = BinaryPrimitives.ReadUInt16BigEndian(message);
        var length = BinaryPrimitives.ReadUInt16BigEndian(message[2..]);
        if (type != BindingRequest
            || BinaryPrimitives.ReadUInt32BigEndian(message[4..]) != MagicCookie
            || length % 4 != 0
            || HeaderLength + length != message.Length)
        {
            return false;
        }

        transactionId = message.Slice(8, TransactionIdLength);
        return true;
    }

    /// <summary>Escreve a resposta com o endereço de origem observado; devolve o tamanho.</summary>
    public static int WriteBindingSuccess(Span<byte> destination, ReadOnlySpan<byte> transactionId, IPEndPoint mapped)
    {
        var address = mapped.Address.IsIPv4MappedToIPv6 ? mapped.Address.MapToIPv4() : mapped.Address;
        var ipv6 = address.AddressFamily == AddressFamily.InterNetworkV6;
        var addressLength = ipv6 ? 16 : 4;
        var attributeLength = 4 + addressLength;

        BinaryPrimitives.WriteUInt16BigEndian(destination, BindingSuccess);
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], (ushort)(4 + attributeLength));
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], MagicCookie);
        transactionId.CopyTo(destination[8..]);

        var attribute = destination[HeaderLength..];
        BinaryPrimitives.WriteUInt16BigEndian(attribute, XorMappedAddress);
        BinaryPrimitives.WriteUInt16BigEndian(attribute[2..], (ushort)attributeLength);
        attribute[4] = 0;
        attribute[5] = (byte)(ipv6 ? 0x02 : 0x01);
        BinaryPrimitives.WriteUInt16BigEndian(attribute[6..], (ushort)(mapped.Port ^ (MagicCookie >> 16)));

        // X-Address: endereço XOR cookie (IPv4) ou XOR (cookie ‖ ID de transação) (IPv6).
        Span<byte> mask = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(mask, MagicCookie);
        transactionId.CopyTo(mask[4..]);
        var bytes = address.GetAddressBytes();
        for (var i = 0; i < addressLength; i++)
        {
            attribute[8 + i] = (byte)(bytes[i] ^ mask[i]);
        }

        return HeaderLength + 4 + attributeLength;
    }

    /// <summary>Lê o XOR-MAPPED-ADDRESS de uma resposta (usado em testes e no diagnóstico).</summary>
    public static IPEndPoint? ReadMappedAddress(ReadOnlySpan<byte> response)
    {
        if (response.Length < HeaderLength || BinaryPrimitives.ReadUInt16BigEndian(response) != BindingSuccess)
        {
            return null;
        }

        var transactionId = response.Slice(8, TransactionIdLength);
        var attributes = response[HeaderLength..];
        while (attributes.Length >= 4)
        {
            var type = BinaryPrimitives.ReadUInt16BigEndian(attributes);
            var length = BinaryPrimitives.ReadUInt16BigEndian(attributes[2..]);
            if (attributes.Length < 4 + length)
            {
                return null;
            }

            if (type == XorMappedAddress && length >= 8)
            {
                var value = attributes.Slice(4, length);
                var ipv6 = value[1] == 0x02;
                var addressLength = ipv6 ? 16 : 4;
                var port = BinaryPrimitives.ReadUInt16BigEndian(value[2..]) ^ (int)(MagicCookie >> 16);
                Span<byte> mask = stackalloc byte[16];
                BinaryPrimitives.WriteUInt32BigEndian(mask, MagicCookie);
                transactionId.CopyTo(mask[4..]);
                var bytes = new byte[addressLength];
                for (var i = 0; i < addressLength; i++)
                {
                    bytes[i] = (byte)(value[4 + i] ^ mask[i]);
                }

                return new IPEndPoint(new IPAddress(bytes), port);
            }

            attributes = attributes[(4 + ((length + 3) & ~3))..];
        }

        return null;
    }

    /// <summary>Monta um Binding Request (testes e diagnóstico).</summary>
    public static byte[] CreateBindingRequest(ReadOnlySpan<byte> transactionId)
    {
        var request = new byte[HeaderLength];
        BinaryPrimitives.WriteUInt16BigEndian(request, BindingRequest);
        BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(4), MagicCookie);
        transactionId.CopyTo(request.AsSpan(8));
        return request;
    }
}
