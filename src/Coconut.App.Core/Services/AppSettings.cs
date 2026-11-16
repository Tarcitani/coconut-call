using System.Text.Json;

namespace Coconut.App.Core.Services;

/// <summary>Preferências locais do usuário. Sem contas: fica só nesta máquina.</summary>
public sealed record AppSettings
{
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Servidor de sinalização. Padrão: local, para desenvolvimento (não há servidor público ainda).</summary>
    public string ServerAddress { get; init; } = "ws://127.0.0.1:5080/ws";

    /// <summary>O aviso de exposição de IP entre participantes já foi exibido.</summary>
    public bool IpNoticeAcknowledged { get; init; }
}

public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}

/// <summary>JSON em %LOCALAPPDATA%\Coconut\settings.json. Falhas de leitura voltam ao padrão.</summary>
public sealed class JsonSettingsStore(string? directory = null) : ISettingsStore
{
    private readonly string _path = Path.Combine(
        directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Coconut"),
        "settings.json");

    public AppSettings Load()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings));
        File.Move(temp, _path, overwrite: true);
    }
}
