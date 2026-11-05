namespace LessonsPlayer.Core.Playlist;

/// <summary>Um arquivo de áudio na playlist.</summary>
public sealed record PlaylistEntry(string FilePath)
{
    public string FileName => Path.GetFileName(FilePath);

    public string Title => Path.GetFileNameWithoutExtension(FilePath);
}
