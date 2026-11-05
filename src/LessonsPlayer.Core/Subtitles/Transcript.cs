using System.Collections.Immutable;

namespace LessonsPlayer.Core.Subtitles;

/// <summary>
/// A legenda completa de um áudio. Imutável: cada edição produz uma nova instância,
/// e os segmentos que não mudaram são reaproveitados (mesma referência).
/// Invariante: segmentos em ordem de início.
/// </summary>
public sealed record Transcript
{
    public Transcript(TranscriptMetadata metadata, IEnumerable<Segment> segments)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(segments);

        var array = segments.ToImmutableArray();
        for (var i = 1; i < array.Length; i++)
        {
            if (array[i].Start < array[i - 1].Start)
            {
                throw new ArgumentException("Os segmentos precisam estar em ordem de início.", nameof(segments));
            }
        }

        Metadata = metadata;
        Segments = array;
    }

    public static Transcript Empty { get; } = new(TranscriptMetadata.Empty, []);

    public TranscriptMetadata Metadata { get; }

    public ImmutableArray<Segment> Segments { get; }

    public bool IsEmpty => Segments.IsEmpty;

    public int WordCount => Segments.Sum(s => s.Words.Length);

    public Transcript WithSegments(IEnumerable<Segment> segments) => new(Metadata, segments);

    public Transcript WithMetadata(TranscriptMetadata metadata) => new(metadata, Segments);

    /// <summary>Retorna a palavra na posição indicada, ou <c>null</c> se a posição não existir.</summary>
    public Word? FindWord(WordLocation location)
    {
        if ((uint)location.SegmentIndex >= (uint)Segments.Length)
        {
            return null;
        }

        var words = Segments[location.SegmentIndex].Words;
        return (uint)location.WordIndex < (uint)words.Length ? words[location.WordIndex] : null;
    }

    public bool Equals(Transcript? other) =>
        other is not null
        && (ReferenceEquals(this, other)
            || (Metadata.Equals(other.Metadata) && Segments.SequenceEqual(other.Segments)));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Metadata);
        foreach (var segment in Segments)
        {
            hash.Add(segment);
        }

        return hash.ToHashCode();
    }
}
