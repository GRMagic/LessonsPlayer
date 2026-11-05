using CommunityToolkit.Mvvm.ComponentModel;
using LessonsPlayer.Core.Subtitles;
using LessonsPlayer.Presentation.Resources;

namespace LessonsPlayer.Presentation.ViewModels;

/// <summary>Estado visual de uma palavra no karaokê.</summary>
public enum WordState
{
    /// <summary>Fora do trecho ativo, ou ainda não falada.</summary>
    Normal,

    /// <summary>Já falada no trecho ativo.</summary>
    Spoken,

    /// <summary>Sendo falada agora.</summary>
    Current,
}

public sealed partial class WordViewModel(SegmentViewModel segment, int index, Word model) : ObservableObject
{
    [ObservableProperty]
    private WordState _state;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editText = string.Empty;

    public SegmentViewModel Segment { get; } = segment;

    public int Index { get; } = index;

    public Word Model { get; } = model;

    public string Text => Model.Text;

    public string Tooltip => UiText.WordTooltip(Model);

    public WordLocation Location => new(Segment.Index, Index);
}
