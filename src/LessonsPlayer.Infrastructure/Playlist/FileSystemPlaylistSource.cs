using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Playlist;

namespace LessonsPlayer.Infrastructure.Playlist;

/// <summary>Monta a playlist com os áudios suportados de uma pasta (sem subpastas) ou de uma lista de caminhos.</summary>
public sealed class FileSystemPlaylistSource : IPlaylistSource
{
    public IReadOnlyList<PlaylistEntry> FromFolder(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        return PlaylistBuilder.Build(Directory.EnumerateFiles(folderPath));
    }

    public IReadOnlyList<PlaylistEntry> FromPaths(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return PlaylistBuilder.Build(paths.SelectMany(path => Directory.Exists(path) ? Directory.EnumerateFiles(path) : [path]));
    }
}
