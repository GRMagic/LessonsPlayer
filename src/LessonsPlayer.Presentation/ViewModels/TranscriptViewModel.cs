using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Subtitles;
using LessonsPlayer.Presentation.Resources;
using LessonsPlayer.Presentation.Services;
using Microsoft.Extensions.Logging;

namespace LessonsPlayer.Presentation.ViewModels;

/// <summary>
/// Legenda do áudio atual: destaque estilo karaokê conforme a posição, clique para posicionar o áudio
/// e todas as operações de edição (delegadas ao <see cref="TranscriptEditor"/>, com desfazer/refazer).
/// </summary>
public sealed partial class TranscriptViewModel(
    ISubtitleStore subtitleStore,
    IDialogService dialogs,
    ILogger<TranscriptViewModel> logger) : ObservableObject
{
    private static readonly TimeSpan NudgeStep = TimeSpan.FromMilliseconds(50);

    private TranscriptEditor? _editor;
    private WordIndex? _index;
    private TimeSpan _position;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAudio), nameof(EmptyMessage))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string? _audioFilePath;

    [ObservableProperty]
    private WordViewModel? _selectedWord;

    /// <summary>Trecho sendo falado agora (a view rola até ele quando <see cref="FollowAudio"/> está ligado).</summary>
    [ObservableProperty]
    private SegmentViewModel? _activeSegment;

    [ObservableProperty]
    private bool _followAudio = true;

    /// <summary>Pedido para posicionar o áudio (clique numa palavra).</summary>
    public event EventHandler<TimeSpan>? SeekRequested;

    /// <summary>A legenda mudou (edição, desfazer, salvar). Útil para atualizar o título da janela.</summary>
    public event EventHandler? Changed;

    /// <summary>A legenda foi gravada; o argumento é o caminho do arquivo de legenda.</summary>
    public event EventHandler<string>? Saved;

    public ObservableCollection<SegmentViewModel> Segments { get; } = [];

    public bool HasTranscript => Segments.Count > 0;

    public bool HasAudio => AudioFilePath is not null;

    /// <summary>Mensagem para a área vazia, ou <c>null</c> quando há legenda para mostrar.</summary>
    public string? EmptyMessage => HasTranscript ? null : HasAudio ? UiText.TranscriptEmpty : UiText.NoTrack;

    public bool IsDirty => _editor?.IsDirty ?? false;

    public bool CanUndo => _editor?.CanUndo ?? false;

    public bool CanRedo => _editor?.CanRedo ?? false;

    public Transcript? Current => _editor?.Current;

    /// <summary>Mostra a legenda do áudio (ou nenhuma, se <paramref name="transcript"/> for <c>null</c>).</summary>
    /// <param name="audioFilePath">Áudio atual, ou <c>null</c>.</param>
    /// <param name="transcript">Legenda a mostrar, ou <c>null</c>.</param>
    /// <param name="isSaved"><c>false</c> se a legenda ainda não está gravada (aparece com alterações pendentes).</param>
    public void Load(string? audioFilePath, Transcript? transcript, bool isSaved = true)
    {
        if (_editor is not null)
        {
            _editor.Changed -= OnEditorChanged;
        }

        AudioFilePath = audioFilePath;
        _editor = transcript is null ? null : new TranscriptEditor(transcript, isSaved);
        if (_editor is not null)
        {
            _editor.Changed += OnEditorChanged;
        }

        SelectedWord = null;
        ActiveSegment = null;
        Segments.Clear();
        SyncSegments();
    }

    /// <summary>Atualiza o destaque do karaokê para a posição atual do áudio (chamado a cada quadro).</summary>
    public void UpdatePosition(TimeSpan position)
    {
        _position = position;
        if (_index?.FindLastStartedAt(position) is not { } location || location.SegmentIndex >= Segments.Count)
        {
            SetActiveSegment(null);
            return;
        }

        var segment = Segments[location.SegmentIndex];
        SetActiveSegment(segment);
        var word = segment.Model.Words[location.WordIndex];
        segment.ApplyProgress(location.WordIndex, position < word.End);
    }

    /// <summary>Grava a legenda ao lado do áudio. Retorna <c>false</c> se falhar (o erro é mostrado ao usuário).</summary>
    public async Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        if (_editor is null || AudioFilePath is null)
        {
            return true;
        }

        try
        {
            await subtitleStore.SaveAsync(AudioFilePath, _editor.Current, cancellationToken);
            _editor.MarkSaved();
            LogSaved(logger, AudioFilePath);
            Saved?.Invoke(this, subtitleStore.GetSubtitlePath(AudioFilePath));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSaveFailed(logger, ex, AudioFilePath);
            dialogs.ShowError(UiText.ErrorTitle, UiText.CannotSave(UiText.ErrorReason(ex)));
            return false;
        }
    }

    // ----- Clique e seleção -----

    [RelayCommand]
    private void ClickWord(WordViewModel? word)
    {
        word = CommitPendingEdit(word);
        if (word is null)
        {
            return;
        }

        Select(word);
        SeekRequested?.Invoke(this, word.Model.Start);
    }

    [RelayCommand]
    private void SelectWord(WordViewModel? word) => Select(CommitPendingEdit(word));

    /// <summary>
    /// Clique no horário à esquerda do trecho: vai para a primeira palavra dele, como o clique nela. Confirma
    /// antes a edição inline em andamento, mesmo que seja dessa primeira palavra. Por isso a palavra é
    /// reencontrada depois: a confirmação recria o trecho editado, e a palavra pode ter mudado ou sumido.
    /// Corrigir uma palavra mantém o início dela, então a busca é pelo tempo.
    /// </summary>
    [RelayCommand]
    private void SeekToSegment(SegmentViewModel? segment)
    {
        if (segment is null)
        {
            return;
        }

        var start = segment.Model.Start;
        if (SelectedWord is { IsEditing: true } editing)
        {
            CommitEditWord(editing);
        }

        var target = Segments.SelectMany(s => s.Words).FirstOrDefault(w => w.Model.Start >= start);
        if (target is null)
        {
            return;
        }

        Select(target);
        SeekRequested?.Invoke(this, target.Model.Start);
    }

    // ----- Corrigir palavra (edição inline) -----

    [RelayCommand]
    private void BeginEditWord(WordViewModel? word)
    {
        word = CommitPendingEdit(word ?? SelectedWord);
        if (word is null)
        {
            return;
        }

        Select(word);
        word.EditText = word.Text;
        word.IsEditing = true;
    }

    [RelayCommand]
    private void CommitEditWord(WordViewModel? word)
    {
        if (word is not { IsEditing: true } || _editor is null)
        {
            return;
        }

        word.IsEditing = false;
        var location = word.Location;
        if (_editor.ReplaceWordText(location, word.EditText))
        {
            SelectAt(location);
        }
    }

    [RelayCommand]
    private void CancelEditWord(WordViewModel? word)
    {
        word ??= SelectedWord;
        word?.IsEditing = false;
    }

    // ----- Editar frase -----

    [RelayCommand]
    private void EditSegment(WordViewModel? word)
    {
        word ??= SelectedWord;
        if (word is null || _editor is null)
        {
            return;
        }

        var segmentIndex = word.Segment.Index;
        var text = dialogs.EditText(UiText.EditSegmentTitle, word.Segment.Model.Text);
        if (text is not null && _editor.ReplaceSegmentText(segmentIndex, text))
        {
            SelectAt(new WordLocation(segmentIndex, 0));
        }
    }

    // ----- Ajustar tempo -----

    [RelayCommand]
    private void SetStartHere(WordViewModel? word) => EditSelected(word, (e, l) => e.SetWordStart(l, _position));

    [RelayCommand]
    private void SetEndHere(WordViewModel? word) => EditSelected(word, (e, l) => e.SetWordEnd(l, _position));

    [RelayCommand]
    private void NudgeStartEarlier(WordViewModel? word) => EditSelected(word, (e, l) => e.ShiftWordStart(l, -NudgeStep));

    [RelayCommand]
    private void NudgeStartLater(WordViewModel? word) => EditSelected(word, (e, l) => e.ShiftWordStart(l, NudgeStep));

    [RelayCommand]
    private void NudgeEndEarlier(WordViewModel? word) => EditSelected(word, (e, l) => e.ShiftWordEnd(l, -NudgeStep));

    [RelayCommand]
    private void NudgeEndLater(WordViewModel? word) => EditSelected(word, (e, l) => e.ShiftWordEnd(l, NudgeStep));

    // ----- Excluir / dividir / juntar -----

    [RelayCommand]
    private void DeleteWord(WordViewModel? word) => EditSelected(word, (e, l) => e.DeleteWord(l));

    [RelayCommand]
    private void SplitHere(WordViewModel? word) =>
        EditSelected(word, (e, l) => e.SplitSegmentBefore(l), l => new WordLocation(l.SegmentIndex + 1, 0));

    [RelayCommand]
    private void MergeWithNext(WordViewModel? word) =>
        EditSelected(word, (e, l) => e.MergeWithNext(l.SegmentIndex));

    [RelayCommand]
    private void MergeWithPrevious(WordViewModel? word)
    {
        word ??= SelectedWord;
        if (word is null || word.Segment.Index == 0)
        {
            return;
        }

        // A palavra passa a ficar depois das palavras do trecho anterior.
        var previousLength = Segments[word.Segment.Index - 1].Words.Count;
        EditSelected(word, (e, l) => e.MergeWithPrevious(l.SegmentIndex),
            l => new WordLocation(l.SegmentIndex - 1, previousLength + l.WordIndex));
    }

    // ----- Histórico e salvar -----

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => _editor?.Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => _editor?.Redo();

    /// <summary>Só há o que salvar com alterações pendentes (inclui a legenda gerada que não pôde ser gravada).</summary>
    private bool CanSave => HasAudio && IsDirty;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save() => await SaveAsync();

    // ----- Infraestrutura interna -----

    /// <summary>
    /// Confirma a edição inline em andamento (se houver e for de outra palavra), como faria ao perder o foco.
    /// Clicar numa palavra não tira o foco do TextBox, porque as palavras não são focáveis.
    /// A confirmação recria o trecho editado, então a palavra alvo é reencontrada pelo modelo, que as
    /// palavras não alteradas preservam (mesma instância de <see cref="Word"/>).
    /// </summary>
    private WordViewModel? CommitPendingEdit(WordViewModel? target)
    {
        if (SelectedWord is not { IsEditing: true } editing || ReferenceEquals(editing, target))
        {
            return target;
        }

        var targetModel = target?.Model;
        CommitEditWord(editing);
        return targetModel is null ? null : FindByModel(targetModel) ?? target;
    }

    private WordViewModel? FindByModel(Word model) =>
        Segments.SelectMany(s => s.Words).FirstOrDefault(w => ReferenceEquals(w.Model, model));

    /// <summary>Aplica uma operação à palavra indicada (ou à selecionada) e mantém a seleção numa posição coerente.</summary>
    private void EditSelected(
        WordViewModel? word,
        Func<TranscriptEditor, WordLocation, bool> operation,
        Func<WordLocation, WordLocation>? selectionAfter = null)
    {
        word ??= SelectedWord;
        if (word is null || _editor is null)
        {
            return;
        }

        var location = word.Location;
        if (operation(_editor, location))
        {
            SelectAt(selectionAfter?.Invoke(location) ?? location);
        }
    }

    private void OnEditorChanged(object? sender, EventArgs e)
    {
        SyncSegments();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Sincroniza a lista visível com a legenda atual reaproveitando os trechos que não mudaram
    /// (mesma referência): só o miolo alterado é removido/inserido, preservando rolagem e estado.
    /// </summary>
    private void SyncSegments()
    {
        var target = _editor?.Current.Segments ?? [];

        var prefix = 0;
        while (prefix < Segments.Count && prefix < target.Length && ReferenceEquals(Segments[prefix].Model, target[prefix]))
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < Segments.Count - prefix && suffix < target.Length - prefix
               && ReferenceEquals(Segments[Segments.Count - 1 - suffix].Model, target[target.Length - 1 - suffix]))
        {
            suffix++;
        }

        for (var i = Segments.Count - suffix - 1; i >= prefix; i--)
        {
            if (ReferenceEquals(Segments[i], ActiveSegment))
            {
                ActiveSegment = null;
            }

            Segments.RemoveAt(i);
        }

        for (var i = prefix; i < target.Length - suffix; i++)
        {
            Segments.Insert(i, new SegmentViewModel(target[i], i));
        }

        for (var i = 0; i < Segments.Count; i++)
        {
            Segments[i].Index = i;
        }

        _index = _editor is null ? null : new WordIndex(_editor.Current);
        if (SelectedWord is not null && !Segments.Contains(SelectedWord.Segment))
        {
            SelectedWord = null;
        }

        OnPropertyChanged(nameof(HasTranscript));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        UpdatePosition(_position);
    }

    private void SetActiveSegment(SegmentViewModel? segment)
    {
        if (ReferenceEquals(segment, ActiveSegment))
        {
            return;
        }

        if (ActiveSegment is not null)
        {
            ActiveSegment.IsActive = false;
            ActiveSegment.ClearProgress();
        }

        ActiveSegment = segment;
        segment?.IsActive = true;
    }

    private void Select(WordViewModel? word)
    {
        SelectedWord?.IsSelected = false;
        SelectedWord = word;
        word?.IsSelected = true;
    }

    private void SelectAt(WordLocation location)
    {
        if (Segments.Count == 0)
        {
            Select(null);
            return;
        }

        var segment = Segments[Math.Clamp(location.SegmentIndex, 0, Segments.Count - 1)];
        Select(segment.Words[Math.Clamp(location.WordIndex, 0, segment.Words.Count - 1)]);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Legenda salva para {AudioFilePath}")]
    private static partial void LogSaved(ILogger logger, string audioFilePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao salvar a legenda de {AudioFilePath}")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception, string audioFilePath);
}
