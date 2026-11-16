using System.Net;
using Coconut.Signaling.Server;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection("Signaling").Get<SignalingOptions>() ?? new SignalingOptions();
builder.Services.AddSingleton(options);
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RoomRegistry>();

var app = builder.Build();

// Defesa por requisição: cobre a janela antes do TransportGuard encerrar e configurações inesperadas.
app.Use((context, next) =>
{
    if (!context.Request.IsHttps && !TransportGuard.IsLocal(context.Connection.RemoteIpAddress))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    return next(context);
});

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
app.Map("/ws", (HttpContext context, RoomRegistry registry, SignalingOptions o, TimeProvider clock) =>
    SignalingEndpoint.HandleAsync(context, registry, o, clock));
app.MapGet("/healthz", () => Results.Text("ok"));

app.Lifetime.ApplicationStarted.Register(() => TransportGuard.EnsureSecure(app));

app.Run();

/// <summary>Ponto de entrada (exposto para testes de integração).</summary>
public partial class Program;

/// <summary>
/// Sinalização em texto claro só é aceita em loopback (desenvolvimento local). Fora dele, exige HTTPS
/// (WSS): sem certificado configurado, o servidor recusa iniciar.
/// </summary>
internal static partial class TransportGuard
{
    public static void EnsureSecure(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        var insecure = addresses.Where(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !IsLoopback(a)).ToList();
        if (insecure.Count == 0)
        {
            return;
        }

        LogInsecureBinding(app.Logger);
        app.Lifetime.StopApplication();
    }

    /// <summary>Origem local: loopback, ou nula quando o servidor roda em processo (testes).</summary>
    public static bool IsLocal(IPAddress? remote) => remote is null || IPAddress.IsLoopback(remote);

    private static bool IsLoopback(string address)
    {
        var host = new Uri(address.Replace("*", "wildcard", StringComparison.Ordinal).Replace("+", "wildcard", StringComparison.Ordinal)).Host;
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip));
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Endereço HTTP sem TLS fora do loopback. Configure HTTPS (WSS) ou escute apenas em localhost. Encerrando.")]
    private static partial void LogInsecureBinding(ILogger logger);
}
