using System.Collections.Immutable;

namespace LessonsPlayer.Core.Subtitles;

/// <summary>
/// Reaplica tempos quando o texto de um trecho é reescrito.
/// Palavras que continuam iguais (ignorando maiúsculas e pontuação nas pontas) mantêm o tempo original.
/// Palavras novas ou trocadas dividem, proporcionalmente ao tamanho, o intervalo das palavras que substituem.
/// Inserções puras (sem palavra antiga correspondente) dividem o intervalo da palavra vizinha.
/// </summary>
internal static class TextRealigner
{
    public static ImmutableArray<Word> Realign(IReadOnlyList<Word> oldWords, IReadOnlyList<string> newTokens)
    {
        ArgumentNullException.ThrowIfNull(oldWords);
        ArgumentNullException.ThrowIfNull(newTokens);
        if (oldWords.Count == 0)
        {
            throw new ArgumentException("É preciso haver palavras originais para realinhar.", nameof(oldWords));
        }

        if (newTokens.Count == 0)
        {
            return [];
        }

        var matches = LongestCommonSubsequence(
            oldWords.Select(w => TextTokens.Normalize(w.Text)).ToArray(),
            newTokens.Select(TextTokens.Normalize).ToArray());

        var chunks = BuildChunks(oldWords, newTokens.Count, matches);
        return Materialize(chunks, newTokens);
    }

    /// <summary>
    /// Agrupa as palavras novas em blocos contíguos, cada um com o intervalo de tempo que vai ocupar.
    /// </summary>
    private static List<Chunk> BuildChunks(IReadOnlyList<Word> oldWords, int newCount, List<(int Old, int New)> matches)
    {
        var chunks = new List<Chunk>();
        int? pendingPrefix = null; // palavras inseridas no começo, antes de qualquer bloco
        var previousOld = -1;
        var previousNew = -1;

        // Um par sentinela no fim processa o que sobrou depois do último casamento.
        foreach (var (oldIndex, newIndex) in matches.Append((oldWords.Count, newCount)))
        {
            var isSentinel = oldIndex == oldWords.Count;
            var oldGap = (Start: previousOld + 1, End: oldIndex);
            var newGap = (Start: previousNew + 1, End: newIndex);

            if (newGap.End > newGap.Start)
            {
                if (oldGap.End > oldGap.Start)
                {
                    // Substituição: as novas ocupam o intervalo das antigas que saíram.
                    AddChunk(new Chunk(newGap.Start, newGap.End,
                        oldWords[oldGap.Start].Start, oldWords[oldGap.End - 1].End, KeepsOriginalTiming: false));
                }
                else if (chunks.Count > 0)
                {
                    // Inserção pura: divide o intervalo do bloco anterior.
                    chunks[^1] = chunks[^1] with { NewEnd = newGap.End, KeepsOriginalTiming = false };
                }
                else
                {
                    pendingPrefix ??= newGap.Start;
                }
            }

            if (!isSentinel)
            {
                AddChunk(new Chunk(newIndex, newIndex + 1,
                    oldWords[oldIndex].Start, oldWords[oldIndex].End, KeepsOriginalTiming: true));
            }

            previousOld = oldIndex;
            previousNew = newIndex;
        }

        return chunks;

        void AddChunk(Chunk chunk)
        {
            if (pendingPrefix is { } prefixStart)
            {
                chunk = chunk with { NewStart = prefixStart, KeepsOriginalTiming = false };
                pendingPrefix = null;
            }

            chunks.Add(chunk);
        }
    }

    private static ImmutableArray<Word> Materialize(List<Chunk> chunks, IReadOnlyList<string> newTokens)
    {
        var builder = ImmutableArray.CreateBuilder<Word>(newTokens.Count);
        foreach (var chunk in chunks)
        {
            if (chunk.KeepsOriginalTiming)
            {
                builder.Add(new Word(chunk.Start, chunk.End, newTokens[chunk.NewStart]));
            }
            else
            {
                var tokens = newTokens.Skip(chunk.NewStart).Take(chunk.NewEnd - chunk.NewStart).ToArray();
                builder.AddRange(TimeDistribution.Distribute(chunk.Start, chunk.End, tokens));
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>LCS clássica por programação dinâmica; retorna os pares (índice antigo, índice novo) casados.</summary>
    private static List<(int Old, int New)> LongestCommonSubsequence(string[] oldKeys, string[] newKeys)
    {
        var n = oldKeys.Length;
        var m = newKeys.Length;
        var lengths = new int[n + 1, m + 1];

        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                lengths[i, j] = string.Equals(oldKeys[i], newKeys[j], StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var result = new List<(int, int)>(lengths[0, 0]);
        int a = 0, b = 0;
        while (a < n && b < m)
        {
            if (string.Equals(oldKeys[a], newKeys[b], StringComparison.Ordinal))
            {
                result.Add((a, b));
                a++;
                b++;
            }
            else if (lengths[a + 1, b] >= lengths[a, b + 1])
            {
                a++;
            }
            else
            {
                b++;
            }
        }

        return result;
    }

    /// <summary>Bloco de palavras novas [NewStart, NewEnd) e o intervalo de tempo que ele ocupa.</summary>
    private readonly record struct Chunk(int NewStart, int NewEnd, TimeSpan Start, TimeSpan End, bool KeepsOriginalTiming);
}
