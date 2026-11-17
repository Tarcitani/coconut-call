using System.IO.Compression;
using Coconut.App.Core.Services;
using Coconut.App.Core.ViewModels;
using Coconut.Client.Screen;
using Coconut.Screen;
using Coconut.Signaling.Contracts;

namespace Coconut.App.Core.Tests;

public sealed class DesktopMvpTests
{
    [Theory]
    [InlineData("coconut://join/ABCDE12345", "ABCDE12345")]
    [InlineData("coconut://join/abcde-12345", "ABCDE12345")]
    [InlineData("coconut://join/ABCDE12345?run=cmd", null)]
    [InlineData("coconut://join/../../file.exe", null)]
    [InlineData("coconut://other/ABCDE12345", null)]
    public void Links_are_strictly_validated_and_never_become_commands(string argument, string? expected)
    {
        var activation = AppActivation.Parse([argument]); Assert.Equal(expected, activation.Invite); Assert.Equal(expected is null, activation.InvalidInvite);
    }

    [Fact]
    public async Task Second_instance_forwards_to_first_user_local_pipe()
    {
        var name = "Coconut-test-" + Guid.NewGuid().ToString("N");
        await using var first = new SingleInstanceBroker(name); Assert.True(first.IsPrimary);
        var received = new TaskCompletionSource<AppActivation>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Activated += activation => received.TrySetResult(activation); first.Listen();
        await using var second = new SingleInstanceBroker(name); Assert.False(second.IsPrimary);
        await second.ForwardAsync(new AppActivation("ABCDE12345"));
        Assert.Equal("ABCDE12345", (await received.Task.WaitAsync(TimeSpan.FromSeconds(10))).Invite);
    }

    [Fact]
    public async Task Link_during_call_only_fills_home_and_never_leaves_or_joins()
    {
        var backend = new FakeBackend(); var shell = new ShellViewModel(_ => backend, new MemorySettings(), new ImmediateDispatcher(), new FakeClipboard());
        shell.Home.DisplayName = "Ana"; await shell.Home.CreateRoomCommand.ExecuteAsync(null);
        var call = shell.Call; shell.UseInvite("coconut://join/ABCDE12345");
        Assert.Same(call, shell.Call); Assert.Equal("ABCDE-12345", shell.Home.InviteInput);
        Assert.DoesNotContain("leave", backend.Calls); Assert.Single(backend.Calls);
        Assert.NotNull(call!.Notice); await shell.CloseAsync();
    }

    [Fact]
    public async Task Export_contains_sanitized_logs_only_not_settings_or_signing_material()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Coconut-export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "coconut-20261116.log"), "origem 192.168.1.2 convite coconut://join/ABCDE12345\nnormal\n");
            File.WriteAllText(Path.Combine(directory, "settings.json"), "PRIVATE_SERVER"); File.WriteAllText(Path.Combine(directory, "release-key.dpapi"), "PRIVATE_KEY");
            var output = Path.Combine(directory, "diagnostico.zip"); await DiagnosticExporter.ExportAsync(directory, output);
            using var zip = ZipFile.OpenRead(output); var entry = Assert.Single(zip.Entries);
            using var reader = new StreamReader(entry.Open()); var text = await reader.ReadToEndAsync();
            Assert.DoesNotContain("192.168", text); Assert.DoesNotContain("ABCDE12345", text); Assert.Contains("normal", text);
            Assert.DoesNotContain("PRIVATE", text);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Share_ui_waits_for_server_grant_and_handover_response_does_not_start_capture()
    {
        var backend = new FakeScreen(); var you = ParticipantId.New();
        using var vm = new ScreenShareViewModel(backend, new ImmediateDispatcher(), () => you, _ => "Ana");
        Assert.NotNull(vm.SelectedMonitor); await vm.RequestCommand.ExecuteAsync(null);
        Assert.Equal(1, backend.Requests); Assert.False(vm.IsSharing); Assert.True(vm.IsRequesting);
        backend.Sharer = you; backend.Session = ShareSessionId.New(); backend.IsRequesting = false; backend.Raise(); Assert.True(vm.IsSharing);
        backend.IncomingRequest = new ShareRequested(ShareRequestId.New(), ParticipantId.New(), DateTimeOffset.UtcNow.AddSeconds(30)); backend.Raise();
        Assert.True(vm.HasIncomingRequest); await vm.AcceptCommand.ExecuteAsync(null); Assert.True(backend.Accepted);
        Assert.Equal(1, backend.Requests); Assert.Equal(0, backend.Stops);
        await vm.StopCommand.ExecuteAsync(null); Assert.Equal(1, backend.Stops);
    }

    private sealed class FakeScreen : IScreenSharing
    {
        public event Action? Changed;
        public event Action<string>? Problem { add { } remove { } }
        public event Action<AuthorizedScreenFrame>? FrameReceived { add { } remove { } }
        public ParticipantId? Sharer { get; set; }
        public ShareSessionId? Session { get; set; }
        public ShareRequested? IncomingRequest { get; set; }
        public ShareRequestPending? PendingRequest => null;
        public bool IsRequesting { get; set; }
        public bool IsCapturing => false;
        public string Status => "Teste";
        public int Requests { get; private set; }
        public int Stops { get; private set; }
        public bool Accepted { get; private set; }
        public IReadOnlyList<ScreenMonitor> Monitors() => [new(1, "Monitor", 1920, 1080, true)];
        public Task RequestAsync(ScreenMonitor monitor, ScreenOptions options) { Requests++; IsRequesting = true; Raise(); return Task.CompletedTask; }
        public Task StopAsync() { Stops++; return Task.CompletedTask; }
        public Task RespondAsync(bool accept) { Accepted = accept; return Task.CompletedTask; }
        public Task CancelAsync() => Task.CompletedTask;
        public void Raise() => Changed?.Invoke();
    }
}
