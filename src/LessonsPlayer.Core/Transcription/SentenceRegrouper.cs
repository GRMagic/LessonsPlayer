using System.Collections.Frozen;
using System.Collections.Immutable;
using LessonsPlayer.Core.Subtitles;

namespace LessonsPlayer.Core.Transcription;

/// <summary>
/// Junta as frases que o Whisper cortou em mais de um trecho.
/// <para>
/// <b>Causa:</b> o Whisper divide o texto nos timestamps que o próprio decodificador prevê, e eles nem sempre
/// caem no fim das frases. Em frases longas, com vírgulas e enumerações, um trecho às vezes termina no meio
/// (<c>"…prestando atenção em cada"</c> / <c>"som."</c>) ou leva junto o começo da frase seguinte
/// (<c>"…sem se ver. Eles conversam sobre o que fizeram no"</c> / <c>"último fim de semana."</c>).
/// Medido com aulas TTS de 1,5 a 2,5 min (en-US e pt-BR, modelos base a large-v3-turbo): de 0 a 20 frases
/// cortadas por áudio. O small cortou todas as 15 frases de uma aula em inglês. A maioria dos cortes cai no
/// meio da janela de 30 s, e o resto no fim de uma fatia do transcritor. Também acontece sem DTW e sem prompt,
/// então é a segmentação do próprio modelo; mudar as fatias não resolve.
/// </para>
/// <para>
/// <b>Regra:</b> quando um trecho não termina em pontuação final, a frase inacabada (as palavras depois do
/// último ponto dele) é completada com as palavras dos trechos seguintes até o primeiro ponto final. O que
/// sobra depois desse ponto continua como um trecho próprio. Só o agrupamento muda; o tempo das palavras
/// fica igual. A junção só acontece se a frase completa durar no máximo <see cref="MaxSentenceDuration"/>,
/// sem pausa maior que <see cref="MaxPause"/> entre os trechos. Senão, os trechos ficam como o Whisper os
/// entregou. Trechos sem fala (<c>[MUSIC]</c>, <c>(risos)</c>) nunca são juntados, e uma transcrição sem
/// pontuação não muda.
/// </para>
/// </summary>
public static class SentenceRegrouper
{
    /// <summary>Duração máxima da frase juntada: a de uma janela do Whisper, que nunca gera trechos maiores.</summary>
    public static readonly TimeSpan MaxSentenceDuration = TimeSpan.FromSeconds(30);

    /// <summary>Pausa entre trechos a partir da qual eles não são juntados, mesmo sem pontuação final.</summary>
    public static readonly TimeSpan MaxPause = TimeSpan.FromSeconds(2);

    /// <summary>Fechamentos que podem vir depois do ponto final: <c>"Ok."</c>, <c>(fim.)</c>.</summary>
    private static readonly char[] ClosingCharacters = ['"', '\'', '”', '’', '»', ')', ']'];

    private static readonly char[] OpeningCharacters = ['"', '\'', '“', '‘', '«', '(', '[', '¿', '¡'];

    /// <summary>Abreviações de tratamento: o ponto delas não encerra a frase (<c>"Mr. Brown"</c>, <c>"Sra. Ana"</c>).</summary>
    private static readonly FrozenSet<string> TitleAbbreviations = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "mr", "mrs", "ms", "dr", "dra", "drs", "prof", "profa", "sr", "sra", "srta", "jr", "st");

    /// <summary>
    /// Reagrupa os trechos (em ordem de início) para que as frases cortadas fiquem inteiras.
    /// A saída também fica em ordem de início.
    /// </summary>
    public static ImmutableArray<Segment> Regroup(IEnumerable<Segment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var result = new List<Segment>();
        var held = new List<Segment>(); // trechos com a frase inacabada, à espera da continuação
        var tailStart = 0;              // em held[0], índice da primeira palavra da frase inacabada

        foreach (var segment in segments)
        {
            var words = segment.Words;
            if (held.Count > 0)
            {
                var sentenceStart = held[0].Words[tailStart].Start;
                var continues = !IsNonSpeech(segment)
                    && segment.Start >= held[^1].Words[^1].Start // as palavras do trecho juntado ficam em ordem
                    && segment.Start - held[^1].End <= MaxPause;
                var end = continues ? IndexOfSentenceEnd(words, first: true) : -1;

                if (end >= 0 && words[end].End - sentenceStart <= MaxSentenceDuration)
                {
                    // Frase completa: a parte já terminada do primeiro trecho fica sozinha, e a frase é juntada.
                    if (tailStart > 0)
                    {
                        result.Add(new Segment(held[0].Words[..tailStart]));
                    }

                    result.Add(new Segment(held.SelectMany(s => s.Words).Skip(tailStart).Concat(words[..(end + 1)])));
                    held.Clear();
                    words = words[(end + 1)..];
                }
                else if (continues && end < 0 && segment.End - sentenceStart <= MaxSentenceDuration)
                {
                    held.Add(segment); // a frase continua sem terminar neste trecho
                    continue;
                }
                else
                {
                    result.AddRange(held); // não dá para completar a frase: mantém os trechos como vieram
                    held.Clear();
                }
            }

            if (words.IsEmpty)
            {
                continue;
            }

            var piece = words.Length == segment.Words.Length ? segment : new Segment(words);
            var lastEnd = IndexOfSentenceEnd(words, first: false);
            if (lastEnd == words.Length - 1 || IsNonSpeech(piece))
            {
                result.Add(piece);
            }
            else
            {
                held.Add(piece);
                tailStart = lastEnd + 1;
            }
        }

        result.AddRange(held);

        // Ordenação estável: trechos sobrepostos (raros) poderiam deixar uma sobra antes do trecho seguinte.
        return [.. result.OrderBy(s => s.Start)];
    }

    /// <summary>Indica se a palavra encerra uma frase (<c>.</c>, <c>!</c>, <c>?</c>, <c>…</c>, também em CJK).</summary>
    internal static bool EndsSentence(string word)
    {
        var text = word.AsSpan().TrimEnd(ClosingCharacters);
        if (text.IsEmpty)
        {
            return false;
        }

        if (text[^1] is '!' or '?' or '…' or '。' or '！' or '？')
        {
            return true;
        }

        if (text[^1] != '.')
        {
            return false;
        }

        // "Mr.", "Dra." e siglas com ponto interno ("e.g.", "U.S.") não encerram a frase. Reticências encerram.
        var core = text.TrimEnd('.').TrimStart(OpeningCharacters);
        return !core.Contains('.') && !TitleAbbreviations.Contains(core.ToString());
    }

    /// <summary>Trecho sem fala, como <c>[MUSIC]</c>, <c>(risos)</c> ou <c>♪</c>: nunca é juntado a uma frase.</summary>
    private static bool IsNonSpeech(Segment segment)
    {
        var text = segment.Text;
        return (text[0] is '[' or '(' or '♪' or '*') && (text[^1] is ']' or ')' or '♪' or '*');
    }

    private static int IndexOfSentenceEnd(ImmutableArray<Word> words, bool first)
    {
        for (var k = 0; k < words.Length; k++)
        {
            var i = first ? k : words.Length - 1 - k;
            if (EndsSentence(words[i].Text))
            {
                return i;
            }
        }

        return -1;
    }
}
