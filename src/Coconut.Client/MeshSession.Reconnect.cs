using System.Net.WebSockets;
using Coconut.Signaling.Contracts;

namespace Coconut.Client;

public sealed partial class MeshSession
{
    private readonly CancellationTokenSource _lifetime = new();
    private Task _reconnectTask = Task.CompletedTask;
    private TaskCompletionSource? _resumeHandshake;
    private Uri? _server;
    private string? _resumeToken;
    private volatile bool _disposing, _terminal;
    private bool _reconnecting;
    private int _signalConnected;

    public bool IsSignalingConnected => Volatile.Read(ref _signalConnected) == 1;
    public event Action<bool>? SignalingConnectivityChanged;
    public bool IsPeerVerified(ParticipantId peer) => _audioTargets.Any(p => p.Remote == peer);

    private Task SendSignalingAsync(ClientMessage message)
    {
        if (_disposing) { return Task.CompletedTask; }
        if (!IsSignalingConnected) { throw new InvalidOperationException("Sinalização temporariamente indisponível."); }
        return _signaling.SendAsync(message);
    }

    private void AttachSignaling(SignalingConnection connection)
    {
        connection.MessageReceived += message => Post(() => ReferenceEquals(connection, _signaling) && !_disposing ? HandleAsync(message) : Task.CompletedTask);
        connection.Closed += status =>
        {
            if (!ReferenceEquals(connection, _signaling) || _disposing) { return; }
            Volatile.Write(ref _signalConnected, 0); SignalingConnectivityChanged?.Invoke(false);
            UpdateScreenAuthorization(null, null);
            _ = Post(() =>
            {
                if (_terminal || _disposing || _reconnecting) { return Task.CompletedTask; }
                if (IsAdmitted && _server is not null && _resumeToken is not null)
                {
                    _reconnecting = true; _reconnectTask = Task.Run(ReconnectSignalingAsync);
                }
                else { Ended?.Invoke("signaling-closed"); }
                return Task.CompletedTask;
            });
        };
    }

    private async Task ReconnectSignalingAsync()
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(25);
        try
        {
            await _signaling.DisposeAsync();
            while (!_disposing && !_terminal && DateTimeOffset.UtcNow < deadline)
            {
                var candidate = new SignalingConnection();
                var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await Post(() => { _signaling = candidate; _resumeHandshake = resumed; AttachSignaling(candidate); return Task.CompletedTask; });
                try
                {
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); attempt.CancelAfter(TimeSpan.FromSeconds(5));
                    await candidate.ConnectAsync(_server!, attempt.Token);
                    await candidate.SendAsync(new ResumeSession(_resumeToken!));
                    await resumed.Task.WaitAsync(TimeSpan.FromSeconds(5), _lifetime.Token);
                    var healthy = false;
                    await Post(async () =>
                    {
                        healthy = !_disposing && IsSignalingConnected && ReferenceEquals(candidate, _signaling);
                        if (!healthy) { return; }
                        _reconnecting = false; _resumeHandshake = null;
                        foreach (var link in _links.Values.ToArray())
                        {
                            if (link.Verified) { await candidate.SendAsync(new ReportLink(link.Remote, LinkStatus.Verified)); }
                            else if (link.Role == NegotiationRole.Offerer) { RemoveLink(link.Remote); CreateLink(link.Remote, link.Role, 1); }
                        }
                        SignalingConnectivityChanged?.Invoke(true);
                    });
                    if (healthy) { return; }
                }
                catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or TimeoutException or InvalidOperationException or IOException) { }
                await candidate.DisposeAsync();
                await Task.Delay(500, _lifetime.Token);
            }
            if (!_disposing && !_terminal)
            {
                await Post(() => { _terminal = true; UpdateScreenAuthorization(null, null); Ended?.Invoke("signaling-closed"); return Task.CompletedTask; });
            }
        }
        catch (OperationCanceledException) when (_disposing) { }
    }
}
