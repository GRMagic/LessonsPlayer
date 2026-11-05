using System.Collections.Immutable;

namespace LessonsPlayer.Core.Subtitles;

/// <summary>
/// Um trecho (frase) da legenda. O início, o fim e o texto são derivados das palavras.
/// Invariantes: tem pelo menos uma palavra e as palavras estão em ordem de início.
/// </summary>
public sealed record Segment
{
    public Segment(IEnumerable<Word> words)
    {
        ArgumentNullException.ThrowIfNull(words);

        var array = words.ToImmutableArray();
        if (array.IsEmpty)
        {
            throw new ArgumentException("Um segmento precisa de pelo menos uma palavra.", nameof(words));
        }

        for (var i = 1; i < array.Length; i++)
        {
            if (array[i].Start < array[i - 1].Start)
            {
                throw new ArgumentException("As palavras precisam estar em ordem de início.", nameof(words));
            }
        }

        Words = array;
        Start = array[0].Start;
        End = array.Max(w => w.End);
        Text = string.Join(' ', array.Select(w => w.Text));
    }

    public ImmutableArray<Word> Words { get; }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public string Text { get; }

    public bool Equals(Segment? other) =>
        other is not null && (ReferenceEquals(this, other) || Words.SequenceEqual(other.Words));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var word in Words)
        {
            hash.Add(word);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => Text;
}
