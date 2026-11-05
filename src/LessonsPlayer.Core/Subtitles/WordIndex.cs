namespace LessonsPlayer.Core.Subtitles;

/// <summary>
/// Índice para descobrir, em O(log n), qual palavra está sendo falada num instante.
/// Construído a partir de um <see cref="Transcript"/> (que é imutável); reconstrua após cada edição.
/// </summary>
public sealed class WordIndex
{
    private readonly TimeSpan[] _starts;
    private readonly WordLocation[] _locations;

    public WordIndex(Transcript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        var count = transcript.WordCount;
        _starts = new TimeSpan[count];
        _locations = new WordLocation[count];

        var k = 0;
        for (var s = 0; s < transcript.Segments.Length; s++)
        {
            var words = transcript.Segments[s].Words;
            for (var w = 0; w < words.Length; w++)
            {
                _starts[k] = words[w].Start;
                _locations[k] = new WordLocation(s, w);
                k++;
            }
        }
    }

    public int Count => _starts.Length;

    /// <summary>
    /// Retorna a última palavra cujo início é menor ou igual a <paramref name="position"/>,
    /// ou <c>null</c> se a posição for anterior à primeira palavra.
    /// </summary>
    public WordLocation? FindLastStartedAt(TimeSpan position)
    {
        var index = Array.BinarySearch(_starts, position);
        if (index < 0)
        {
            index = ~index - 1;
        }
        else
        {
            // Palavras com o mesmo início: fica com a última.
            while (index + 1 < _starts.Length && _starts[index + 1] == position)
            {
                index++;
            }
        }

        return index < 0 ? null : _locations[index];
    }
}
