using LessonsPlayer.Core.Subtitles;

namespace LessonsPlayer.Core.Abstractions;

/// <summary>Persistência da legenda ao lado do arquivo de áudio.</summary>
public interface ISubtitleStore
{
    string GetSubtitlePath(string audioFilePath);

    bool Exists(string audioFilePath);

    /// <summary>Carrega a legenda do áudio, ou retorna <c>null</c> se ela não existir.</summary>
    Task<ParseResult?> LoadAsync(string audioFilePath, CancellationToken cancellationToken);

    Task SaveAsync(string audioFilePath, Transcript transcript, CancellationToken cancellationToken);
}
