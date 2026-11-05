using System.Collections.Frozen;
using System.Collections.Immutable;

namespace LessonsPlayer.Core.Playlist;

/// <summary>Filtra arquivos de áudio suportados e os ordena de forma natural (aula2 antes de aula10).</summary>
public static class PlaylistBuilder
{
    /// <summary>Formatos decodificados pelo Media Foundation do Windows.</summary>
    public static FrozenSet<string> SupportedExtensions { get; } =
        new[] { ".mp3", ".wav", ".m4a", ".aac", ".wma", ".flac" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string filePath) =>
        !string.IsNullOrWhiteSpace(filePath) && SupportedExtensions.Contains(Path.GetExtension(filePath));

    public static ImmutableArray<PlaylistEntry> Build(IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        return
        [
            .. filePaths
                .Where(IsSupported)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(Path.GetFileName, NaturalStringComparer.Instance)
                .Select(path => new PlaylistEntry(path)),
        ];
    }
}
