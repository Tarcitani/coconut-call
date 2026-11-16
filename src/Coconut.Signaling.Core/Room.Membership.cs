using Coconut.Signaling.Contracts;

namespace Coconut.Signaling.Core;

/// <summary>Admissão, remoção e sucessão administrativa.</summary>
public sealed partial class Room
{
    /// <summary>
    /// Reavalia pendentes em ordem de chegada contra o conjunto ativo de decisão atual
    /// (Active ∪ Reconnecting). Uma admissão por vez: cada uma altera o conjunto e reinicia a avaliação.
    /// </summary>
    private void EvaluateAdmissions(Outbox outbox)
    {
        var changed = true;
        while (changed && !IsClosed)
        {
            changed = false;
            var pendings = _members.Values
                .Where(m => m.State == ParticipantState.Pending)
                .OrderBy(m => m.JoinSequence)
                .ToList();

            foreach (var pending in pendings)
            {
                var mesh = Mesh().ToList();
                var failed = mesh.Where(r => _links.HasFailed(pending.Id, r.Id)).Select(r => r.ToInfo()).ToList();
                if (failed.Count > 0)
                {
                    FailAdmission(pending, AdmissionFailure.PeerLinkFailed, failed, outbox);
                    changed = true;
                    break;
                }

                // A malha nunca está vazia aqui: a sala é encerrada quando o último membro admitido sai.
                if (mesh.All(r => _links.IsVerified(pending.Id, r.Id)))
                {
                    Admit(pending, outbox);
                    changed = true;
                    break;
                }
            }
        }
    }

    private void Admit(Member member, Outbox outbox)
    {
        member.State = ParticipantState.Active;
        member.AdmissionSequence = ++_admissionSequence;
        Epoch++;

        // Pendentes também recebem: o novo membro passa a ser exigido na admissão deles.
        var roster = Mesh().Select(m => m.ToInfo()).ToList();
        foreach (var id in _members.Keys)
        {
            outbox.Send(id, new PeerAdmitted(member.Id, Epoch, roster));
        }

        if (Admin is null)
        {
            // Só ocorre na criação da sala.
            Admin = member.Id;
            BroadcastToMesh(new AdminChanged(member.Id), outbox);
        }

        outbox.Send(member.Id, Snapshot());
    }

    private void FailAdmission(Member pending, AdmissionFailure reason, IReadOnlyList<PeerInfo> failedPeers, Outbox outbox)
    {
        outbox.Send(pending.Id, new AdmissionFailed(reason, failedPeers));
        var leaveReason = reason == AdmissionFailure.RoomEmptied ? LeaveReason.AdmissionCanceled : LeaveReason.AdmissionFailed;
        Remove(pending.Id, leaveReason, outbox);
    }

    private void Remove(ParticipantId id, LeaveReason reason, Outbox outbox)
    {
        var member = _members[id];
        _members.Remove(id);
        _links.Forget(id);

        if (member.IsInMesh)
        {
            Epoch++;
            foreach (var other in _members.Keys)
            {
                outbox.Send(other, new PeerLeft(id, reason, Epoch));
            }
        }
        else
        {
            // Rollback de admissão: só os enlaces com o pendente são desfeitos.
            foreach (var other in _members.Keys)
            {
                outbox.Send(other, new DropPeer(id, reason));
            }
        }

        Share.OnMemberRemoved(id, outbox);

        if (!Mesh().Any())
        {
            // Ninguém admitido permanece: pendentes não herdam a sala (convite vazado não a assume).
            CloseWithoutMesh(outbox);
            return;
        }

        if (Admin == id)
        {
            ElectAdmin(outbox);
        }
    }

    private void CloseWithoutMesh(Outbox outbox)
    {
        foreach (var pending in _members.Values.ToList())
        {
            outbox.Send(pending.Id, new AdmissionFailed(AdmissionFailure.RoomEmptied, []));
        }

        _members.Clear();
        Share.Reset();
        Admin = null;
        IsClosed = true;
    }

    /// <summary>Sucessão: o ativo presente há mais tempo; na falta, o mais antigo em reconexão.</summary>
    private void ElectAdmin(Outbox outbox)
    {
        var next = Mesh()
            .OrderBy(m => m.State == ParticipantState.Active ? 0 : 1)
            .ThenBy(m => m.AdmissionSequence)
            .First();

        Admin = next.Id;
        BroadcastToMesh(new AdminChanged(next.Id), outbox);
    }
}
