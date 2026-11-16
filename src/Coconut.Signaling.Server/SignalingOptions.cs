using Coconut.Signaling.Core;

namespace Coconut.Signaling.Server;

/// <summary>Limites do servidor. Seção de configuração "Signaling".</summary>
public sealed class SignalingOptions
{
    /// <summary>Tamanho máximo de uma mensagem do cliente (SDP com muitos candidatos cabe com folga).</summary>
    public int MaxMessageBytes { get; set; } = 64 * 1024;

    public int MaxRelayPayloadChars { get; set; } = 16 * 1024;

    public int MaxDisplayNameLength { get; set; } = 32;

    /// <summary>Prazo para a primeira mensagem (criar, entrar ou retomar).</summary>
    public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Taxa sustentada por conexão; candidatos ICE chegam em rajadas, daí o balde.</summary>
    public double MessagesPerSecond { get; set; } = 50;

    public int MessageBurst { get; set; } = 200;

    /// <summary>Salas abertas simultaneamente por endereço de origem (endereço só em memória, nunca em log).</summary>
    public int MaxRoomsPerAddress { get; set; } = 3;

    /// <summary>Fila de envio por conexão; cliente que não consome é desconectado.</summary>
    public int OutgoingQueueCapacity { get; set; } = 512;

    public TimeSpan TickInterval { get; set; } = TimeSpan.FromSeconds(1);

    public RoomOptions Room { get; set; } = new();
}
