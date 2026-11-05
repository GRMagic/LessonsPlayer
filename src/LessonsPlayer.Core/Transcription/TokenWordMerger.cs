using System.Collections.Immutable;
using LessonsPlayer.Core.Subtitles;

namespace LessonsPlayer.Core.Transcription;

/// <summary>
/// Converte um trecho reconhecido (tokens com tempo) em um <see cref="Segment"/> de palavras com tempo.
/// <para>
/// O texto das palavras vem de <see cref="RecognizedSegment.Text"/>, que é decodificado inteiro.
/// O texto de cada token isolado pode trazer caracteres acentuados quebrados, quando um caractere
/// UTF-8 é dividido entre tokens. Dos tokens vem apenas o tempo: o início de uma palavra é o tempo
/// alinhado (DTW) do primeiro token dela, e o fim é o início da palavra seguinte.
/// </para>
/// </summary>
public static class TokenWordMerger
{
    public static Segment? ToSegment(RecognizedSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        var textWords = TextTokens.Split(segment.Text);
        if (textWords.Length == 0)
        {
            return null;
        }

        var segmentStart = segment.Start;
        var segmentEnd = segment.End < segmentStart ? segmentStart : segment.End;
        var tokenWords = GroupTokensIntoWords(segment.Tokens, segmentStart, segmentEnd);

        var starts = tokenWords.Count == textWords.Length
            ? tokenWords.Select(w => w.Start).ToArray()
            : tokenWords.Count == 0
                ? TimeDistribution.Distribute(segmentStart, segmentEnd, textWords).Select(w => w.Start).ToArray()
                : InterpolateByCharacterPosition(tokenWords, textWords, segmentEnd);

        var words = ImmutableArray.CreateBuilder<Word>(textWords.Length);
        var previousStart = segmentStart;
        for (var i = 0; i < textWords.Length; i++)
        {
            var start = Clamp(starts[i], previousStart, segmentEnd); // garante ordem crescente
            starts[i] = start;
            previousStart = start;
        }

        for (var i = 0; i < textWords.Length; i++)
        {
            var end = i + 1 < textWords.Length ? starts[i + 1] : segmentEnd;
            words.Add(new Word(starts[i], end, textWords[i]));
        }

        return new Segment(words.MoveToImmutable());
    }

    /// <summary>Tokens especiais do whisper.cpp: <c>[_BEG_]</c>, <c>[_TT_123]</c>, <c>&lt;|en|&gt;</c> etc.</summary>
    internal static bool IsSpecialToken(string text)
    {
        var t = text.Trim();
        return (t.StartsWith("[_", StringComparison.Ordinal) && t.EndsWith(']'))
            || (t.StartsWith("<|", StringComparison.Ordinal) && t.EndsWith("|>", StringComparison.Ordinal));
    }

    /// <summary>Junta tokens em palavras: um token com espaço inicial começa uma palavra nova.</summary>
    private static List<TokenWord> GroupTokensIntoWords(
        ImmutableArray<RecognizedToken> tokens, TimeSpan segmentStart, TimeSpan segmentEnd)
    {
        var words = new List<TokenWord>();
        if (tokens.IsDefaultOrEmpty)
        {
            return words;
        }

        var startNewWord = true;
        foreach (var token in tokens)
        {
            if (string.IsNullOrEmpty(token.Text) || IsSpecialToken(token.Text))
            {
                continue;
            }

            var beginsWithSpace = char.IsWhiteSpace(token.Text[0]);
            var trimmed = token.Text.Trim();
            if (trimmed.Length == 0)
            {
                startNewWord = true; // token só de espaço: a próxima palavra começa depois dele
                continue;
            }

            if (startNewWord || beginsWithSpace || words.Count == 0)
            {
                var time = Clamp(token.AlignedStart ?? token.Start, segmentStart, segmentEnd);
                words.Add(new TokenWord(trimmed.Length, time));
                startNewWord = false;
            }
            else
            {
                words[^1] = words[^1] with { Length = words[^1].Length + trimmed.Length };
            }

            // Palavras não contêm espaço interno; se o token tiver, o que vem depois é outra palavra.
            if (trimmed.Any(char.IsWhiteSpace))
            {
                startNewWord = true;
            }
        }

        return words;
    }

    /// <summary>
    /// Quando o número de palavras dos tokens difere do texto, estima o início de cada palavra do texto
    /// interpolando o tempo pela posição relativa dos caracteres.
    /// </summary>
    private static TimeSpan[] InterpolateByCharacterPosition(
        List<TokenWord> tokenWords, string[] textWords, TimeSpan segmentEnd)
    {
        // Pontos (posição relativa do caractere, tempo) dos tokens, terminando em (1, fim do trecho).
        var totalTokenChars = Math.Max(1, tokenWords.Sum(w => w.Length));
        var points = new List<(double Position, TimeSpan Time)>(tokenWords.Count + 1);
        var offset = 0;
        foreach (var word in tokenWords)
        {
            points.Add(((double)offset / totalTokenChars, word.Start));
            offset += word.Length;
        }

        points.Add((1.0, segmentEnd));

        var totalTextChars = Math.Max(1, textWords.Sum(w => w.Length));
        var result = new TimeSpan[textWords.Length];
        var textOffset = 0;
        for (var i = 0; i < textWords.Length; i++)
        {
            result[i] = Interpolate(points, (double)textOffset / totalTextChars);
            textOffset += textWords[i].Length;
        }

        return result;
    }

    private static TimeSpan Interpolate(List<(double Position, TimeSpan Time)> points, double position)
    {
        if (position <= points[0].Position)
        {
            return points[0].Time;
        }

        for (var k = 1; k < points.Count; k++)
        {
            var (p1, t1) = points[k];
            if (position <= p1)
            {
                var (p0, t0) = points[k - 1];
                var fraction = p1 > p0 ? (position - p0) / (p1 - p0) : 0;
                return t0 + ((t1 - t0) * fraction);
            }
        }

        return points[^1].Time;
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) =>
        value < min ? min : value > max ? max : value;

    private readonly record struct TokenWord(int Length, TimeSpan Start);
}
