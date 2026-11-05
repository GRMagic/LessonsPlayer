using LessonsPlayer.Core.Playlist;

namespace LessonsPlayer.Core.Abstractions;

/// <summary>Monta playlists a partir do sistema de arquivos.</summary>
public interface IPlaylistSource
{
    IReadOnlyList<PlaylistEntry> FromFolder(string folderPath);

    /// <summary>Monta a playlist a partir de arquivos e/ou pastas (ex.: arrastar e soltar).</summary>
    IReadOnlyList<PlaylistEntry> FromPaths(IEnumerable<string> paths);
}
