using Coconut.App.Core.Services;

namespace Coconut.App.Core.Tests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("coconut-settings-").FullName;
    private string UserDir => Path.Combine(_root, "user");
    private string PackageDir => Path.Combine(_root, "package");

    public JsonSettingsStoreTests() => Directory.CreateDirectory(PackageDir);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private JsonSettingsStore Create() => new(UserDir, PackageDir);

    private void WriteDefaults(string json) =>
        File.WriteAllText(Path.Combine(PackageDir, JsonSettingsStore.DefaultsFileName), json);

    [Fact]
    public void Without_any_file_uses_built_in_defaults()
    {
        Assert.Equal(new AppSettings(), Create().Load());
    }

    [Fact]
    public void Packaged_defaults_preconfigure_only_the_server()
    {
        WriteDefaults("""{"ServerAddress":"wss://exemplo.test/ws","DisplayName":"Intruso","IpNoticeAcknowledged":true}""");

        var settings = Create().Load();

        Assert.Equal("wss://exemplo.test/ws", settings.ServerAddress);
        Assert.Equal(string.Empty, settings.DisplayName);
        Assert.False(settings.IpNoticeAcknowledged);
    }

    [Fact]
    public void User_settings_win_over_packaged_defaults()
    {
        WriteDefaults("""{"ServerAddress":"wss://pacote.test/ws"}""");
        var store = Create();
        store.Save(new AppSettings { DisplayName = "Ana", ServerAddress = "wss://meu.test/ws" });

        var settings = store.Load();

        Assert.Equal("Ana", settings.DisplayName);
        Assert.Equal("wss://meu.test/ws", settings.ServerAddress);
    }

    [Fact]
    public void Corrupt_defaults_fall_back_to_built_in()
    {
        WriteDefaults("{ não é json");

        Assert.Equal(new AppSettings(), Create().Load());
    }

    [Fact]
    public void Corrupt_user_settings_fall_back_to_packaged_defaults()
    {
        WriteDefaults("""{"ServerAddress":"wss://pacote.test/ws"}""");
        Directory.CreateDirectory(UserDir);
        File.WriteAllText(Path.Combine(UserDir, "settings.json"), "{ quebrado");

        Assert.Equal("wss://pacote.test/ws", Create().Load().ServerAddress);
    }
}
