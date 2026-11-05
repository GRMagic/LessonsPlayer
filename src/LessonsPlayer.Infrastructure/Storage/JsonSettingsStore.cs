using System.Text.Json;
using System.Text.Json.Serialization;
using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Settings;
using Microsoft.Extensions.Logging;

namespace LessonsPlayer.Infrastructure.Storage;

/// <summary>Preferências em JSON. Arquivo ausente ou corrompido resulta nos valores padrão.</summary>
public sealed partial class JsonSettingsStore(InfrastructureOptions options, ILogger<JsonSettingsStore> logger) : ISettingsStore
{
    public AppSettings Load()
    {
        var path = options.SettingsFilePath;
        if (!File.Exists(path))
        {
            return AppSettings.Default;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? AppSettings.Default;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LogLoadFailed(logger, ex, path);
            return AppSettings.Default;
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var path = options.SettingsFilePath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferências não são críticas: registra e segue.
            LogSaveFailed(logger, ex, path);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Não foi possível ler as preferências em {Path}; usando padrões")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Não foi possível salvar as preferências em {Path}")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception, string path);
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
