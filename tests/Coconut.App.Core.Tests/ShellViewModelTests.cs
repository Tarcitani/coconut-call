using Coconut.App.Core.ViewModels;

namespace Coconut.App.Core.Tests;

public class ShellViewModelTests
{
    [Fact]
    public async Task Joining_navigates_to_call_and_ending_returns_home_with_reason()
    {
        var backend = new FakeBackend();
        var shell = new ShellViewModel(_ => backend, new MemorySettings(), new ImmediateDispatcher(), new FakeClipboard());
        shell.Home.DisplayName = "Ana";

        await shell.Home.CreateRoomCommand.ExecuteAsync(null);
        Assert.IsType<CallViewModel>(shell.CurrentPage);

        backend.RaiseEnded("kicked");

        Assert.Same(shell.Home, shell.CurrentPage);
        Assert.Equal(UserMessages.ForEnd("kicked"), shell.Home.Error);
        Assert.True(backend.Disposed);
    }
}
