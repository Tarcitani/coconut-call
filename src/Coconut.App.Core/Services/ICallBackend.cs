using Coconut.Audio;
using Coconut.Signaling.Contracts;

namespace Coconut.App.Core.Services;

/// <summary>Resultado de criar ou entrar numa sala.</summary>
public enum JoinOutcome
{
    Admitted,
    ServerUnavailable,
    InviteInvalid,
    RoomFull,
    InvalidName,
    RoomLimit,
    AdmissionFailed,
    Canceled,
}

/// <summary>
/// Uma participação em chamada, do ponto de vista da interface. A implementação real usa
/// <c>MeshSession</c> + <c>VoiceEngine</c>; os testes usam uma falsa. Eventos podem vir de qualquer thread.
/// </summary>
public interface ICallBackend : IAsyncDisposable
{
    /// <summary>Composição, administrador ou estado mudaram.</summary>
    event Action? Changed;

    /// <summary>Mensagem para o usuário durante a chamada (ex.: microfone desconectado).</summary>
    event Action<string>? Problem;

    /// <summary>A participação terminou; o motivo é um código estável (ex.: <c>kicked</c>, <c>room-ended</c>).</summary>
    event Action<string>? Ended;

    string? InviteCode { get; }

    ParticipantId? You { get; }

    ParticipantId? Admin { get; }

    IReadOnlyList<PeerInfo> Roster { get; }

    bool IsMuted { get; set; }

    double MicrophoneLevel { get; }

    IReadOnlyDictionary<ParticipantId, double> SpeakingLevels { get; }

    Task<JoinOutcome> CreateRoomAsync(Uri server, string displayName, CancellationToken cancellation);

    Task<JoinOutcome> JoinRoomAsync(Uri server, string inviteCode, string displayName, CancellationToken cancellation);

    Task LeaveAsync();

    Task KickAsync(ParticipantId target);

    Task EndRoomForAllAsync();

    IReadOnlyList<AudioDeviceInfo> Devices(AudioDirection direction);

    /// <summary>Dispositivo escolhido; nulo = padrão do Windows.</summary>
    string? SelectedDevice(AudioDirection direction);

    Task SelectDeviceAsync(AudioDirection direction, string? deviceId);
}

/// <summary>Executa na thread de UI.</summary>
public interface IUiDispatcher
{
    void Post(Action action);

    /// <summary>Repete <paramref name="tick"/> na thread de UI até o descarte do retorno.</summary>
    IDisposable StartTimer(TimeSpan interval, Action tick);
}

public interface IClipboard
{
    void SetText(string text);
}
