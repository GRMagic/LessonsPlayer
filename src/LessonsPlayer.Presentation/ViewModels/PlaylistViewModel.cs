using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Playlist;

namespace LessonsPlayer.Presentation.ViewModels;

/// <summary>Um item da playlist.</summary>
public sealed partial class PlaylistItemViewModel(PlaylistEntry entry, bool hasSubtitle) : ObservableObject
{
    [ObservableProperty]
    private bool _hasSubtitle = hasSubtitle;

    [ObservableProperty]
    private bool _isCurrent;

    public PlaylistEntry Entry { get; } = entry;

    public string FilePath => Entry.FilePath;

    public string Title => Entry.Title;

    public string FileName => Entry.FileName;
}

/// <summary>Lista de reprodução: itens, seleção e qual está tocando.</summary>
public sealed partial class PlaylistViewModel(IPlaylistSource source, ISubtitleStore subtitles) : ObservableObject
{
    [ObservableProperty]
    private PlaylistItemViewModel? _selectedItem;

    [ObservableProperty]
    private PlaylistItemViewModel? _currentItem;

    [ObservableProperty]
    private string? _folderName;

    public ObservableCollection<PlaylistItemViewModel> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    public void LoadFolder(string folderPath) =>
        Replace(source.FromFolder(folderPath), Path.GetFileName(Path.TrimEndingDirectorySeparator(folderPath)));

    public void LoadPaths(IReadOnlyCollection<string> paths)
    {
        var name = paths.Count == 1 && Directory.Exists(paths.First())
            ? Path.GetFileName(Path.TrimEndingDirectorySeparator(paths.First()))
            : null;
        Replace(source.FromPaths(paths), name);
    }

    public PlaylistItemViewModel? Next() => Neighbor(+1);

    public PlaylistItemViewModel? Previous() => Neighbor(-1);

    public void SetCurrent(PlaylistItemViewModel? item)
    {
        CurrentItem?.IsCurrent = false;
        CurrentItem = item;
        if (item is not null)
        {
            item.IsCurrent = true;
            SelectedItem = item;
        }
    }

    public void RefreshSubtitleState(string audioFilePath)
    {
        foreach (var item in Items.Where(i => string.Equals(i.FilePath, audioFilePath, StringComparison.OrdinalIgnoreCase)))
        {
            item.HasSubtitle = subtitles.Exists(item.FilePath);
        }
    }

    private PlaylistItemViewModel? Neighbor(int direction)
    {
        if (CurrentItem is null)
        {
            return Items.FirstOrDefault();
        }

        var index = Items.IndexOf(CurrentItem) + direction;
        return index >= 0 && index < Items.Count ? Items[index] : null;
    }

    private void Replace(IReadOnlyList<PlaylistEntry> entries, string? folderName)
    {
        SetCurrent(null);
        SelectedItem = null;
        Items.Clear();
        foreach (var entry in entries)
        {
            Items.Add(new PlaylistItemViewModel(entry, subtitles.Exists(entry.FilePath)));
        }

        FolderName = folderName;
        OnPropertyChanged(nameof(IsEmpty));
    }
}
