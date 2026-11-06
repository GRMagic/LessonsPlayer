namespace LessonsPlayer.Infrastructure;

/// <summary>Locais em disco usados pela aplicação.</summary>
public sealed record InfrastructureOptions
{
    public const string AppFolderName = "LessonsPlayer";

    /// <summary>Pasta dos modelos Whisper (grandes, por máquina: fica em LocalAppData).</summary>
    public required string ModelsDirectory { get; init; }

    /// <summary>Arquivo de preferências do usuário (fica em AppData, que acompanha o perfil).</summary>
    public required string SettingsFilePath { get; init; }

    /// <summary>Pasta dos logs.</summary>
    public required string LogsDirectory { get; init; }

    /// <summary>Modelo do detector de voz (Silero VAD), que vai junto com o app, ao lado do executável.</summary>
    public string SpeechModelPath { get; init; } = DefaultSpeechModelPath;

    private static string DefaultSpeechModelPath => Path.Combine(AppContext.BaseDirectory, "vad", "ggml-silero-v6.2.0.bin");

    public static InfrastructureOptions CreateDefault()
    {
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);
        var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);

        return new InfrastructureOptions
        {
            ModelsDirectory = Path.Combine(local, "models"),
            LogsDirectory = Path.Combine(local, "logs"),
            SettingsFilePath = Path.Combine(roaming, "settings.json"),
        };
    }
}
