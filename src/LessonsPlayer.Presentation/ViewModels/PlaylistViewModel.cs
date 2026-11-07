using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Playlist;
using LessonsPlayer.Core.Search;

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

    /// <summary>Filtro pelo nome da aula (vazio = todas).</summary>
    private SearchQuery _filter = SearchQuery.Empty;

    /// <summary>A playlist inteira, que a navegação (anterior/próxima, autoplay) percorre.</summary>
    public ObservableCollection<PlaylistItemViewModel> Items { get; } = [];

    /// <summary>As aulas mostradas na lista: as de <see cref="Items"/> cujo nome casa com a busca.</summary>
    public ObservableCollection<PlaylistItemViewModel> VisibleItems { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    /// <summary>Há aulas, mas nenhum nome casa com a busca.</summary>
    public bool HasNoNameMatches => !IsEmpty && VisibleItems.Count == 0;

    /// <summary>A playlist foi trocada (outra pasta ou outros arquivos).</summary>
    public event EventHandler? Reloaded;

    public void Filter(SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        _filter = query;
        RebuildVisibleItems();
    }

    public void LoadFolder(string folderPath) =>
        Replace(source.FromFolder(folderPath), Path.GetFileName(Path.TrimEndingDirectorySeparator(folderPath)));

    public void LoadPaths(IReadOnlyCollection<string> paths)
    {
        // Uma pasta: o nome dela. Um arquivo avulso: o nome da pasta onde ele está (o cabeçalho ficava vazio).
        var single = paths.Count == 1 ? Path.TrimEndingDirectorySeparator(paths.First()) : null;
        var name = single is null ? null
            : Directory.Exists(single) ? Path.GetFileName(single)
            : Path.GetFileName(Path.GetDirectoryName(single));
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
        RebuildVisibleItems();
        Reloaded?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildVisibleItems()
    {
        VisibleItems.Clear();
        foreach (var item in Items.Where(i => _filter.Matches(i.Title)))
        {
            VisibleItems.Add(item);
        }

        OnPropertyChanged(nameof(HasNoNameMatches));
    }
}
