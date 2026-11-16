using Coconut.App.Core.Services;

namespace Coconut.App.Core.ViewModels;

/// <summary>Textos para o usuário em um só lugar (pt-BR). Sem detalhes técnicos nem dados de rede.</summary>
public static class UserMessages
{
    public const string IpNotice =
        "As chamadas são ponto a ponto: cada participante vê o endereço IP dos demais. Entre só em salas de pessoas em quem você confia.";

    public static string For(JoinOutcome outcome) => outcome switch
    {
        JoinOutcome.ServerUnavailable => "Não foi possível falar com o servidor do Coconut. Verifique o endereço e sua conexão.",
        JoinOutcome.InviteInvalid => "Convite inválido ou sala já encerrada.",
        JoinOutcome.RoomFull => "A sala está cheia (máximo de 15 pessoas).",
        JoinOutcome.InvalidName => "Use um nome de 1 a 32 caracteres.",
        JoinOutcome.RoomLimit => "Você já tem salas demais abertas. Encerre uma antes de criar outra.",
        JoinOutcome.AdmissionFailed => "Não foi possível conectar diretamente a todos os participantes. Algumas redes bloqueiam conexões ponto a ponto.",
        JoinOutcome.Canceled => "Entrada cancelada.",
        _ => string.Empty,
    };

    public static string ForEnd(string reason) => reason switch
    {
        "kicked" => "Você foi removido da sala pelo administrador.",
        "room-ended" => "O administrador encerrou a chamada.",
        "admission-failed" => For(JoinOutcome.AdmissionFailed),
        "left" => string.Empty,
        _ => "A conexão com o servidor caiu e a chamada terminou.",
    };
}
