using CommunityToolkit.Mvvm.ComponentModel;
using LessonsPlayer.Core.Subtitles;
using LessonsPlayer.Presentation.Formatting;

namespace LessonsPlayer.Presentation.ViewModels;

/// <summary>Um trecho da legenda na tela. Reaproveitado entre edições enquanto o <see cref="Segment"/> não muda.</summary>
public sealed partial class SegmentViewModel : ObservableObject
{
    private (int WordIndex, bool IsInsideWord)? _appliedProgress;

    [ObservableProperty]
    private bool _isActive;

    public SegmentViewModel(Segment model, int index)
    {
        Model = model;
        Index = index;
        Words = [.. model.Words.Select((word, i) => new WordViewModel(this, i, word))];
    }

    public Segment Model { get; }

    /// <summary>Posição do trecho na legenda atual (atualizada depois de cada edição).</summary>
    public int Index { get; set; }

    public IReadOnlyList<WordViewModel> Words { get; }

    public string StartText => TimeFormat.Compact(Model.Start);

    /// <summary>
    /// Marca as palavras como faladas até <paramref name="wordIndex"/>; ela fica como "atual" se a posição
    /// ainda estiver dentro do intervalo dela.
    /// </summary>
    public void ApplyProgress(int wordIndex, bool isInsideWord)
    {
        if (_appliedProgress == (wordIndex, isInsideWord))
        {
            return;
        }

        _appliedProgress = (wordIndex, isInsideWord);
        for (var i = 0; i < Words.Count; i++)
        {
            Words[i].State = i < wordIndex ? WordState.Spoken
                : i == wordIndex ? (isInsideWord ? WordState.Current : WordState.Spoken)
                : WordState.Normal;
        }
    }

    public void ClearProgress()
    {
        _appliedProgress = null;
        foreach (var word in Words)
        {
            word.State = WordState.Normal;
        }
    }
}
