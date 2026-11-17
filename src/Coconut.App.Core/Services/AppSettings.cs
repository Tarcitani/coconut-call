using System.Text.Json;

namespace Coconut.App.Core.Services;

/// <summary>Preferências locais do usuário. Sem contas: fica só nesta máquina.</summary>
public sealed record AppSettings
{
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Servidor de sinalização. Padrão: local, para desenvolvimento; o pacote distribuído traz o seu.</summary>
    public string ServerAddress { get; init; } = "ws://127.0.0.1:5080/ws";

    /// <summary>O aviso de exposição de IP entre participantes já foi exibido.</summary>
    public bool IpNoticeAcknowledged { get; init; }
}

public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}

/// <summary>
/// JSON em %LOCALAPPDATA%\Coconut\settings.json. Enquanto o usuário não tem configurações próprias, vale
/// o arquivo de padrões que acompanha o pacote (<see cref="DefaultsFileName"/> ao lado do executável),
/// que só pré-configura o servidor. Falhas de leitura voltam ao padrão.
/// </summary>
public sealed class JsonSettingsStore(string? directory = null, string? defaultsDirectory = null) : ISettingsStore
{
    public const string DefaultsFileName = "coconut.defaults.json";

    private readonly string _path = Path.Combine(
        directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Coconut"),
        "settings.json");

    private readonly string _defaultsPath = Path.Combine(defaultsDirectory ?? AppContext.BaseDirectory, DefaultsFileName);

    public AppSettings Load() => Read(_path) ?? Defaults();

    private AppSettings Defaults()
    {
        var packaged = Read(_defaultsPath);
        return packaged is null ? new() : new() { ServerAddress = packaged.ServerAddress };
    }

    private static AppSettings? Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
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
