using System.Collections.Immutable;

namespace LessonsPlayer.Core.Subtitles;

/// <summary>Distribui um intervalo de tempo entre palavras, proporcionalmente ao tamanho de cada uma.</summary>
internal static class TimeDistribution
{
    public static ImmutableArray<Word> Distribute(TimeSpan start, TimeSpan end, IReadOnlyList<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Count == 0)
        {
            return [];
        }

        if (end < start)
        {
            end = start;
        }

        var weights = tokens.Select(t => Math.Max(1, t.Length)).ToArray();
        var totalWeight = weights.Sum();
        var spanMs = (end - start).TotalMilliseconds;

        var builder = ImmutableArray.CreateBuilder<Word>(tokens.Count);
        var accumulated = 0;
        var wordStart = start;
        for (var i = 0; i < tokens.Count; i++)
        {
            accumulated += weights[i];
            var wordEnd = i == tokens.Count - 1
                ? end
                : start + TimeSpan.FromMilliseconds(Math.Round(spanMs * accumulated / totalWeight));

            builder.Add(new Word(wordStart, wordEnd, tokens[i]));
            wordStart = wordEnd;
        }

        return builder.MoveToImmutable();
    }
}
