using System.Text;
using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Subtitles;

namespace LessonsPlayer.Infrastructure.Storage;

/// <summary>
/// Guarda a legenda ao lado do áudio: <c>aula01.mp3</c> → <c>aula01.legenda.txt</c>.
/// A gravação é atômica: escreve um arquivo temporário e só então substitui o definitivo.
/// </summary>
public sealed class TxtSubtitleStore : ISubtitleStore
{
    public const string FileSuffix = ".legenda.txt";

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public string GetSubtitlePath(string audioFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFilePath);
        var fullPath = Path.GetFullPath(audioFilePath);
        var directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        return Path.Combine(directory, Path.GetFileNameWithoutExtension(fullPath) + FileSuffix);
    }

    public bool Exists(string audioFilePath) => File.Exists(GetSubtitlePath(audioFilePath));

    public async Task<ParseResult?> LoadAsync(string audioFilePath, CancellationToken cancellationToken)
    {
        var path = GetSubtitlePath(audioFilePath);
        if (!File.Exists(path))
        {
            return null;
        }

        // ReadAllTextAsync detecta BOM; sem BOM assume UTF-8.
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        return SubtitleTxtFormat.Parse(content);
    }

    public async Task SaveAsync(string audioFilePath, Transcript transcript, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        var path = GetSubtitlePath(audioFilePath);
        var temporaryPath = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, SubtitleTxtFormat.Serialize(transcript), Utf8WithoutBom, cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }

    public bool Delete(string audioFilePath)
    {
        var path = GetSubtitlePath(audioFilePath);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }
}
