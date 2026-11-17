using System.IO.Pipes;
using System.Text.Json;
using Coconut.Signaling.Contracts;

namespace Coconut.App.Core.Services;

public sealed record AppActivation(string? Invite = null, bool Startup = false, bool InvalidInvite = false, bool ExitIfIdle = false)
{
    public static AppActivation Parse(IEnumerable<string> arguments)
    {
        string? invite = null; var invalid = false; var startup = false; var exit = false;
        foreach (var argument in arguments.Take(16))
        {
            if (argument == "--startup") { startup = true; }
            else if (argument == "--exit-if-idle") { exit = true; }
            else if (argument.StartsWith("coconut:", StringComparison.OrdinalIgnoreCase))
            {
                if (argument.Length <= 64 && argument.StartsWith(InviteCode.LinkPrefix, StringComparison.OrdinalIgnoreCase) && InviteCode.TryNormalize(argument) is { } code) { invite = code; }
                else { invalid = true; }
            }
        }
        return new AppActivation(invite, startup, invalid, exit && invite is null && !invalid);
    }
}

/// <summary>One user-local instance. IPC is bounded, same-user only and cannot execute commands or open URLs.</summary>
public sealed class SingleInstanceBroker : IAsyncDisposable
{
    private readonly Mutex _presence;
    private readonly string _name;
    private readonly CancellationTokenSource _stop = new();
    private Task? _listen;

    public SingleInstanceBroker(string name)
    {
        _name = name;
        // Keep the named object alive, without acquiring thread-affine ownership across awaits.
        _presence = new Mutex(false, @"Local\" + name, out var created); IsPrimary = created;
    }

    public bool IsPrimary { get; }
    public event Action<AppActivation>? Activated;

    public void Listen()
    {
        if (IsPrimary && _listen is null) { _listen = Task.Run(ListenAsync); }
    }

    public async Task ForwardAsync(AppActivation activation, CancellationToken cancellation = default)
    {
        using var pipe = new NamedPipeClientStream(".", _name, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(5000, cancellation);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(activation);
        await pipe.WriteAsync(BitConverter.GetBytes(bytes.Length), cancellation);
        await pipe.WriteAsync(bytes, cancellation);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync(); if (_listen is not null) { await _listen; }
        _presence.Dispose(); _stop.Dispose();
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(_name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(2));
                var header = new byte[4]; await pipe.ReadExactlyAsync(header, deadline.Token);
                var length = BitConverter.ToInt32(header);
                if (length is <= 0 or > 512) { continue; }
                var bytes = new byte[length]; await pipe.ReadExactlyAsync(bytes, deadline.Token);
                var activation = JsonSerializer.Deserialize<AppActivation>(bytes);
                if (activation is not null && (activation.Invite is null || activation.Invite == InviteCode.TryNormalize(activation.Invite))) { Activated?.Invoke(activation); }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException) { }
        }
    }
}
