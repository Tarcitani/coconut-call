using Coconut.App.Core.ViewModels;
using Coconut.Audio;
using Coconut.Signaling.Contracts;

namespace Coconut.App.Core.Tests;

public sealed class CallViewModelTests : IAsyncDisposable
{
    public ValueTask DisposeAsync() => _backend.DisposeAsync();

    private readonly FakeBackend _backend = new();
    private readonly ImmediateDispatcher _ui = new();
    private readonly FakeClipboard _clipboard = new();
    private readonly List<string> _ended = [];
    private readonly ParticipantId _other = ParticipantId.New();

    private CallViewModel Create()
    {
        _backend.Roster =
        [
            new PeerInfo(_backend.You!.Value, "Eu", ParticipantState.Active),
            new PeerInfo(_other, "Bia", ParticipantState.Active),
        ];
        return new CallViewModel(_backend, _ui, _clipboard, (_, message) => _ended.Add(message));
    }

    [Fact]
    public void Roster_marks_self_and_admin()
    {
        _backend.Admin = _other;
        var call = Create();

        Assert.Equal(2, call.Participants.Count);
        Assert.True(call.Participants.Single(p => p.Id == _backend.You).IsYou);
        Assert.Equal("Administrador", call.Participants.Single(p => p.Id == _other).StatusText);
        Assert.False(call.IsAdmin);
        Assert.All(call.Participants, p => Assert.False(p.CanBeKicked));
        Assert.Equal("2 pessoas", call.ParticipantCountText);
    }

    [Fact]
    public void Admin_can_kick_others_but_not_self_and_end_for_all()
    {
        _backend.Admin = _backend.You;
        var call = Create();

        Assert.True(call.IsAdmin);
        Assert.True(call.Participants.Single(p => p.Id == _other).CanBeKicked);
        Assert.False(call.Participants.Single(p => p.IsYou).CanBeKicked);
        Assert.True(call.EndForAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task Kick_sends_only_for_kickable_participants()
    {
        _backend.Admin = _backend.You;
        var call = Create();

        await call.KickCommand.ExecuteAsync(call.Participants.Single(p => p.IsYou));
        await call.KickCommand.ExecuteAsync(call.Participants.Single(p => p.Id == _other));

        Assert.Equal([$"kick:{_other}"], _backend.Calls);
    }

    [Fact]
    public void Roster_updates_in_place_when_people_leave_and_join()
    {
        var call = Create();
        var bia = call.Participants.Single(p => p.Id == _other);
        var caio = ParticipantId.New();

        _backend.Roster = [new PeerInfo(_backend.You!.Value, "Eu", ParticipantState.Active), new PeerInfo(caio, "Caio", ParticipantState.Reconnecting)];
        _backend.RaiseChanged();

        Assert.DoesNotContain(bia, call.Participants);
        Assert.Equal("Reconectando…", call.Participants.Single(p => p.Id == caio).StatusText);
    }

    [Fact]
    public void Mute_is_applied_to_the_backend()
    {
        var call = Create();

        call.ToggleMuteCommand.Execute(null);
        Assert.True(_backend.IsMuted);

        call.ToggleMuteCommand.Execute(null);
        Assert.False(_backend.IsMuted);
    }

    [Fact]
    public void Copy_invite_puts_the_link_on_the_clipboard()
    {
        var call = Create();

        call.CopyInviteCommand.Execute(null);

        Assert.Equal("coconut://join/ABCDE12345", _clipboard.Text);
        Assert.Equal("ABCDE-12345", call.InviteDisplay);
        Assert.NotNull(call.Notice);
    }

    [Fact]
    public void Levels_update_speaking_state_and_mute_zeroes_own_meter()
    {
        var call = Create();
        _backend.MicrophoneLevel = 900;
        _backend.SpeakingLevels = new Dictionary<ParticipantId, double> { [_other] = 500 };

        _ui.Tick();
        Assert.True(call.Participants.Single(p => p.IsYou).IsSpeaking);
        Assert.True(call.Participants.Single(p => p.Id == _other).IsSpeaking);

        call.IsMuted = true;
        _ui.Tick();
        Assert.False(call.Participants.Single(p => p.IsYou).IsSpeaking);
        Assert.Equal(0, call.MicrophoneLevel);
    }

    [Theory]
    [InlineData("kicked")]
    [InlineData("room-ended")]
    [InlineData("signaling-closed")]
    public void Ending_reports_a_message_once(string reason)
    {
        Create();

        _backend.RaiseEnded(reason);
        _backend.RaiseEnded(reason);

        Assert.Equal(UserMessages.ForEnd(reason), Assert.Single(_ended));
        Assert.False(string.IsNullOrEmpty(_ended[0]));
    }

    [Fact]
    public async Task Leaving_is_silent_and_releases_the_backend()
    {
        var call = Create();

        await call.LeaveCommand.ExecuteAsync(null);
        await call.DisposeAsync();

        Assert.Contains("leave", _backend.Calls);
        Assert.Equal(string.Empty, Assert.Single(_ended));
        Assert.True(_backend.Disposed);
        Assert.Equal(0, _ui.ActiveTimers);
    }

    [Fact]
    public void Problems_show_as_notice()
    {
        var call = Create();

        _backend.RaiseProblem("O microfone foi desconectado.");

        Assert.Equal("O microfone foi desconectado.", call.Notice);
    }

    [Fact]
    public void Default_device_is_preselected_and_choice_reaches_backend()
    {
        var call = Create();
        Assert.Equal("mic-1", call.SelectedCaptureDevice?.Id);
        Assert.Empty(_backend.Calls);

        call.SelectedCaptureDevice = call.CaptureDevices.Single(d => d.Id == "mic-2");

        Assert.Equal([$"device:{AudioDirection.Capture}:mic-2"], _backend.Calls);
    }
}
