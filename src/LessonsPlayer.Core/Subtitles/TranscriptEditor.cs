using System.Collections.Immutable;

namespace LessonsPlayer.Core.Subtitles;

/// <summary>
/// Operações de edição sobre uma legenda, com desfazer/refazer.
/// Cada operação produz um novo <see cref="Transcript"/>; segmentos não afetados mantêm a mesma referência.
/// Todas as operações retornam <c>false</c> (sem alterar nada) quando a posição é inválida ou nada mudaria.
/// Não é thread-safe: use a partir de uma única thread (a da UI).
/// </summary>
public sealed class TranscriptEditor
{
    private const int MaxHistory = 200;

    private readonly List<Transcript> _undo = [];
    private readonly List<Transcript> _redo = [];
    private Transcript? _saved;

    /// <param name="transcript">Legenda inicial.</param>
    /// <param name="isSaved">
    /// <c>false</c> quando a legenda inicial ainda não está gravada (ex.: gerada numa pasta sem permissão de escrita).
    /// </param>
    public TranscriptEditor(Transcript transcript, bool isSaved = true)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        Current = transcript;
        _saved = isSaved ? transcript : null;
    }

    /// <summary>Disparado depois de qualquer alteração em <see cref="Current"/> ou no estado de salvo.</summary>
    public event EventHandler? Changed;

    public Transcript Current { get; private set; }

    /// <summary>Há alterações desde a última vez que a legenda foi salva (ou carregada).</summary>
    public bool IsDirty => !ReferenceEquals(Current, _saved);

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void MarkSaved()
    {
        _saved = Current;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo() => Restore(_undo, _redo);

    public bool Redo() => Restore(_redo, _undo);

    /// <summary>Corrige o texto de uma palavra, mantendo o tempo. Várias palavras dividem o intervalo; texto vazio exclui.</summary>
    public bool ReplaceWordText(WordLocation location, string newText)
    {
        if (Current.FindWord(location) is not { } word)
        {
            return false;
        }

        var tokens = TextTokens.Split(newText);
        if (tokens.Length == 0)
        {
            return DeleteWord(location);
        }

        var segment = Current.Segments[location.SegmentIndex];
        var replacement = TimeDistribution.Distribute(word.Start, word.End, tokens);
        var words = segment.Words.RemoveAt(location.WordIndex).InsertRange(location.WordIndex, replacement);
        return Apply(ReplaceSegment(Current, location.SegmentIndex, words));
    }

    /// <summary>Reescreve o texto de um trecho; palavras mantidas preservam o tempo. Texto vazio exclui o trecho.</summary>
    public bool ReplaceSegmentText(int segmentIndex, string newText)
    {
        if (!IsValidSegment(segmentIndex))
        {
            return false;
        }

        var segment = Current.Segments[segmentIndex];
        var words = TextRealigner.Realign(segment.Words, TextTokens.Split(newText));
        return Apply(ReplaceSegment(Current, segmentIndex, words));
    }

    public bool DeleteWord(WordLocation location)
    {
        if (Current.FindWord(location) is null)
        {
            return false;
        }

        var words = Current.Segments[location.SegmentIndex].Words.RemoveAt(location.WordIndex);
        return Apply(ReplaceSegment(Current, location.SegmentIndex, words));
    }

    /// <summary>
    /// Define o início da palavra. O valor é limitado entre o início da palavra anterior e o da próxima,
    /// para manter a ordem. Se a anterior terminasse depois do novo início, o fim dela é recuado.
    /// Um início depois do fim desloca a palavra inteira, mantendo a duração (até o início da próxima).
    /// </summary>
    public bool SetWordStart(WordLocation location, TimeSpan start)
    {
        if (Current.FindWord(location) is not { } word)
        {
            return false;
        }

        var previous = FindNeighbor(location, -1);
        var next = FindNeighbor(location, +1);

        var lower = previous is { } p ? Current.FindWord(p)!.Start : TimeSpan.Zero;
        var upper = next is { } n ? Current.FindWord(n)!.Start : TimeSpan.MaxValue;
        start = Clamp(start, lower, upper);

        var end = word.End > start ? word.End : Min(start + word.Duration, Max(upper, start));
        var transcript = ReplaceWord(Current, location, word.WithTiming(start, end));
        if (previous is { } prevLocation && transcript.FindWord(prevLocation) is { } prevWord && prevWord.End > start)
        {
            transcript = ReplaceWord(transcript, prevLocation, prevWord.WithTiming(prevWord.Start, start));
        }

        return Apply(transcript);
    }

    /// <summary>Define o fim da palavra, limitado entre o início dela e o início da próxima.</summary>
    public bool SetWordEnd(WordLocation location, TimeSpan end)
    {
        if (Current.FindWord(location) is not { } word)
        {
            return false;
        }

        var upper = FindNeighbor(location, +1) is { } n ? Current.FindWord(n)!.Start : TimeSpan.MaxValue;
        end = Clamp(end, word.Start, Max(upper, word.Start));
        return Apply(ReplaceWord(Current, location, word.WithTiming(word.Start, end)));
    }

    public bool ShiftWordStart(WordLocation location, TimeSpan delta) =>
        Current.FindWord(location) is { } word && SetWordStart(location, Max(TimeSpan.Zero, word.Start + delta));

    public bool ShiftWordEnd(WordLocation location, TimeSpan delta) =>
        Current.FindWord(location) is { } word && SetWordEnd(location, Max(TimeSpan.Zero, word.End + delta));

    /// <summary>Divide o trecho em dois: a palavra indicada passa a ser a primeira do novo trecho.</summary>
    public bool SplitSegmentBefore(WordLocation location)
    {
        if (Current.FindWord(location) is null || location.WordIndex == 0)
        {
            return false;
        }

        var segment = Current.Segments[location.SegmentIndex];
        var first = new Segment(segment.Words.Take(location.WordIndex));
        var second = new Segment(segment.Words.Skip(location.WordIndex));
        var segments = Current.Segments
            .SetItem(location.SegmentIndex, first)
            .Insert(location.SegmentIndex + 1, second);
        return Apply(Current.WithSegments(segments));
    }

    /// <summary>Junta o trecho com o seguinte.</summary>
    public bool MergeWithNext(int segmentIndex)
    {
        if (!IsValidSegment(segmentIndex) || !IsValidSegment(segmentIndex + 1))
        {
            return false;
        }

        var first = Current.Segments[segmentIndex];
        var second = Current.Segments[segmentIndex + 1];
        var merged = new Segment(first.Words.Concat(second.Words).OrderBy(w => w.Start));
        var segments = Current.Segments.RemoveAt(segmentIndex + 1).SetItem(segmentIndex, merged);
        return Apply(Current.WithSegments(segments));
    }

    /// <summary>Junta o trecho com o anterior.</summary>
    public bool MergeWithPrevious(int segmentIndex) => MergeWithNext(segmentIndex - 1);

    private bool Apply(Transcript next)
    {
        if (next.Equals(Current))
        {
            return false;
        }

        _undo.Add(Current);
        if (_undo.Count > MaxHistory)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        Current = next;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool Restore(List<Transcript> from, List<Transcript> to)
    {
        if (from.Count == 0)
        {
            return false;
        }

        to.Add(Current);
        Current = from[^1];
        from.RemoveAt(from.Count - 1);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool IsValidSegment(int index) => (uint)index < (uint)Current.Segments.Length;

    /// <summary>Palavra vizinha na ordem global (atravessando trechos). <paramref name="direction"/>: -1 ou +1.</summary>
    private WordLocation? FindNeighbor(WordLocation location, int direction)
    {
        var segments = Current.Segments;
        var wordIndex = location.WordIndex + direction;
        if ((uint)wordIndex < (uint)segments[location.SegmentIndex].Words.Length)
        {
            return location with { WordIndex = wordIndex };
        }

        var segmentIndex = location.SegmentIndex + direction;
        if ((uint)segmentIndex >= (uint)segments.Length)
        {
            return null;
        }

        return new WordLocation(segmentIndex, direction > 0 ? 0 : segments[segmentIndex].Words.Length - 1);
    }

    private static Transcript ReplaceWord(Transcript transcript, WordLocation location, Word word)
    {
        var words = transcript.Segments[location.SegmentIndex].Words.SetItem(location.WordIndex, word);
        return ReplaceSegment(transcript, location.SegmentIndex, words);
    }

    /// <summary>Troca as palavras de um trecho; sem palavras, o trecho é removido.</summary>
    private static Transcript ReplaceSegment(Transcript transcript, int segmentIndex, ImmutableArray<Word> words)
    {
        var segments = words.IsEmpty
            ? transcript.Segments.RemoveAt(segmentIndex)
            : transcript.Segments.SetItem(segmentIndex, new Segment(words));
        return transcript.WithSegments(segments);
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) =>
        value < min ? min : value > max ? max : value;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
