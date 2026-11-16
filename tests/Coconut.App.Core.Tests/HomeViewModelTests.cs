using Coconut.App.Core.Services;
using Coconut.App.Core.ViewModels;

namespace Coconut.App.Core.Tests;

public class HomeViewModelTests
{
    private readonly MemorySettings _settings = new();
    private readonly List<FakeBackend> _created = [];
    private readonly List<ICallBackend> _joined = [];

    private HomeViewModel Create(JoinOutcome outcome = JoinOutcome.Admitted) =>
        new(() =>
        {
            var backend = new FakeBackend { Outcome = outcome };
            _created.Add(backend);
            return backend;
        }, _settings, _joined.Add);

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("Ana", true)]
    [InlineData("a\u0007b", false)]
    [InlineData("0123456789012345678901234567890123", false)]
    public void Create_requires_a_valid_name(string name, bool enabled)
    {
        var home = Create();
        home.DisplayName = name;

        Assert.Equal(enabled, home.CreateRoomCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("abcde-12345", true)]
    [InlineData("coconut://join/ABCDE12345", true)]
    [InlineData("ABCDE1234", false)]
    [InlineData("ABCDE1234U", false)]
    public void Join_requires_a_valid_invite_or_link(string invite, bool enabled)
    {
        var home = Create();
        home.DisplayName = "Ana";
        home.InviteInput = invite;

        Assert.Equal(enabled, home.JoinRoomCommand.CanExecute(null));
    }

    [Fact]
    public async Task Join_sends_normalized_code_and_navigates_when_admitted()
    {
        var home = Create();
        home.DisplayName = "  Ana  ";
        home.UseInvite("coconut://join/abcde12345");

        await home.JoinRoomCommand.ExecuteAsync(null);

        Assert.Equal("join:ABCDE12345:Ana", Assert.Single(_created).Calls.Single());
        Assert.Same(_created[0], Assert.Single(_joined));
        Assert.Equal(string.Empty, home.InviteInput);
    }

    [Fact]
    public async Task Failure_shows_message_disposes_backend_and_stays_home()
    {
        var home = Create(JoinOutcome.InviteInvalid);
        home.DisplayName = "Ana";
        home.InviteInput = "ABCDE12345";

        await home.JoinRoomCommand.ExecuteAsync(null);

        Assert.Equal(UserMessages.For(JoinOutcome.InviteInvalid), home.Error);
        Assert.True(_created[0].Disposed);
        Assert.Empty(_joined);
        Assert.False(home.IsBusy);
    }

    [Fact]
    public async Task Invalid_server_address_never_creates_a_backend()
    {
        var home = Create();
        home.DisplayName = "Ana";
        home.ServerAddress = "http://exemplo";

        await home.CreateRoomCommand.ExecuteAsync(null);

        Assert.Empty(_created);
        Assert.NotNull(home.Error);
    }

    [Fact]
    public async Task Name_and_server_are_remembered()
    {
        var home = Create();
        home.DisplayName = "Ana";
        home.ServerAddress = "wss://sinal.exemplo/ws";

        await home.CreateRoomCommand.ExecuteAsync(null);

        Assert.Equal("Ana", _settings.Current.DisplayName);
        Assert.Equal("wss://sinal.exemplo/ws", _settings.Current.ServerAddress);
        Assert.Equal("Ana", Create().DisplayName);
    }

    [Fact]
    public void Ip_notice_is_shown_until_acknowledged()
    {
        var home = Create();
        Assert.True(home.ShowIpNotice);

        home.AcknowledgeIpNoticeCommand.Execute(null);

        Assert.False(home.ShowIpNotice);
        Assert.False(Create().ShowIpNotice);
    }

    [Fact]
    public void Every_failure_outcome_has_a_message()
    {
        foreach (var outcome in Enum.GetValues<JoinOutcome>().Where(o => o != JoinOutcome.Admitted))
        {
            Assert.False(string.IsNullOrWhiteSpace(UserMessages.For(outcome)), outcome.ToString());
        }
    }
}
