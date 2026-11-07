using LessonsPlayer.Infrastructure;
using LessonsPlayer.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace LessonsPlayer.Integration.Tests;

/// <summary>Preferências gravadas em disco (pasta temporária).</summary>
[Trait("Category", "Integration")]
public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _workDirectory = Directory.CreateTempSubdirectory("lessonsplayer-tests-").FullName;

    public void Dispose() => Directory.Delete(_workDirectory, recursive: true);

    /// <summary>
    /// A opção "Use GPU" saiu em 2026-11-07. Um arquivo antigo com <c>useGpu</c> precisa continuar sendo lido:
    /// se a leitura falhasse, o programa voltaria aos padrões e perderia o modelo, o volume e a última pasta.
    /// </summary>
    [Fact]
    public void Reads_settings_saved_by_a_version_that_still_had_the_gpu_option()
    {
        var path = Path.Combine(_workDirectory, "settings.json");
        File.WriteAllText(path, """
            {
              "modelId": "large-v3-turbo-q5_0",
              "language": "en",
              "useGpu": false,
              "volume": 0.25,
              "lastFolder": "C:\\Users\\teacher\\Lessons"
            }
            """);
        var options = InfrastructureOptions.CreateDefault() with { SettingsFilePath = path };

        var settings = new JsonSettingsStore(options, NullLogger<JsonSettingsStore>.Instance).Load();

        settings.ModelId.ShouldBe("large-v3-turbo-q5_0");
        settings.Language.ShouldBe("en");
        settings.Volume.ShouldBe(0.25f);
        settings.LastFolder.ShouldBe(@"C:\Users\teacher\Lessons");
    }
}
