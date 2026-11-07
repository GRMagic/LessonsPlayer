using LessonsPlayer.Core.Subtitles;

namespace LessonsPlayer.Core.Transcription;

/// <summary>
/// Palavras do fim de uma fatia que o alinhamento (DTW) não conseguiu pôr no áudio: ficam todas no último instante
/// alinhado, com duração zero.
/// <para>
/// <b>Causa:</b> o DTW do whisper.cpp só usa o áudio até o último timestamp que o decodificador previu
/// (<c>n_frames = min(seek_delta, ...)</c> em <c>whisper_full_with_state</c>). Quando o texto vai além desse ponto,
/// o que sobra é empilhado nele. Às vezes é fala real que ficou depois do último timestamp; às vezes é texto que
/// não está no áudio. Medido em 3 vídeos reais com small, small.en e large-v3-turbo: 14 fatias com palavras
/// empilhadas. Com 1 a 3 palavras (10 fatias), todas eram fala real e estavam a até 0,3 s do tempo certo. Com 8 ou
/// mais (4 fatias), havia texto inventado por cima da fala real (<c>"The United States Senate and the United States
/// Senate..."</c>, <c>"...that it's a little bit more than a plane."</c>), uma frase da fatia anterior repetida
/// na música, ou fala real até 3 s antes da hora.
/// </para>
/// <para>
/// <b>Regra:</b> a partir de <see cref="MinimumStackedWords"/> palavras empilhadas, a sequência final de palavras
/// que termina no último instante alinhado é descartada (as empilhadas e a palavra esticada até ali). Quem decide
/// o que fazer com o áudio dessas palavras é o transcritor (ver <see cref="LastAlignedEnd"/>).
/// </para>
/// </summary>
/// <param name="Kept">A fatia sem as palavras descartadas (trechos inteiros ou cortados).</param>
/// <param name="StackedWords">Quantas palavras estavam empilhadas no último instante alinhado.</param>
/// <param name="DroppedWords">Quantas palavras foram descartadas (empilhadas e esticadas).</param>
/// <param name="AlignedUntil">Fim do áudio alinhado: o fim do último trecho da fatia.</param>
/// <param name="LastAlignedEnd">Fim da última palavra que ficou, ou <c>null</c> se não sobrou palavra alinhada.</param>
public sealed record UnalignedTail(
    IReadOnlyList<RecognizedSegment> Kept,
    int StackedWords,
    int DroppedWords,
    TimeSpan AlignedUntil,
    TimeSpan? LastAlignedEnd)
{
    /// <summary>Palavras empilhadas a partir das quais a sequência final é descartada (até 3, eram reais).</summary>
    public const int MinimumStackedWords = 4;

    /// <summary>"No último instante": até dois quadros do DTW (20 ms cada) antes do fim do áudio alinhado.</summary>
    private static readonly TimeSpan LastFrameTolerance = TimeSpan.FromMilliseconds(40);

    /// <summary>
    /// Procura palavras empilhadas no fim da fatia (os trechos de uma janela, em ordem). Retorna <c>null</c> se houver
    /// menos de <see cref="MinimumStackedWords"/> ou se a fatia não tiver alinhamento.
    /// </summary>
    public static UnalignedTail? Find(IReadOnlyList<RecognizedSegment> slice)
    {
        ArgumentNullException.ThrowIfNull(slice);
        if (slice.Count == 0)
        {
            return null;
        }

        var (words, first, alignedUntil) = SplitAtLastFrame(slice);
        var lastFrame = alignedUntil - LastFrameTolerance;
        var stacked = words.Skip(first).Count(w => w.Word.Start >= lastFrame);
        if (stacked < MinimumStackedWords)
        {
            return null;
        }

        // O trecho em que a sequência começa é cortado antes dela; se o texto não casar com os tokens, sai inteiro.
        var boundary = words[first];
        var cut = boundary.Position > 0 ? KeepFirstWords(slice[boundary.Segment], boundary.Position) : null;
        if (cut is null)
        {
            first = words.FindIndex(w => w.Segment == boundary.Segment);
        }

        var kept = slice.Take(boundary.Segment).ToList();
        if (cut is not null)
        {
            kept.Add(cut);
        }

        return new UnalignedTail(kept, stacked, words.Count - first, alignedUntil, first > 0 ? words[first - 1].Word.End : null);
    }

    /// <summary>
    /// Indica se o alinhamento pôs alguma palavra da fatia antes do último instante alinhado. Sem nenhuma, o DTW não
    /// achou no áudio nada do que foi escrito (ex.: <c>"Music"</c> esticado sobre a fatia inteira).
    /// </summary>
    public static bool HasAlignedWords(IReadOnlyList<RecognizedSegment> slice)
    {
        ArgumentNullException.ThrowIfNull(slice);
        return slice.Count > 0 && SplitAtLastFrame(slice).First > 0;
    }

    /// <summary>
    /// As palavras da fatia (com trecho e posição) e o índice da primeira da sequência final que termina no último
    /// instante alinhado (<c>words.Count</c> se nenhuma termina ali).
    /// </summary>
    private static (List<(int Segment, int Position, TokenWordMerger.TokenWord Word)> Words, int First, TimeSpan AlignedUntil)
        SplitAtLastFrame(IReadOnlyList<RecognizedSegment> slice)
    {
        var alignedUntil = slice.Max(s => s.End);
        var lastFrame = alignedUntil - LastFrameTolerance;
        var words = slice
            .SelectMany((segment, index) => TokenWordMerger.GroupTokensIntoWords(segment.Tokens)
                .Select((word, position) => (Segment: index, Position: position, Word: word)))
            .ToList();

        var first = words.Count;
        while (first > 0 && words[first - 1].Word.End is { } end && end >= lastFrame)
        {
            first--;
        }

        return (words, first, alignedUntil);
    }

    /// <summary>
    /// O trecho só com as suas primeiras <paramref name="count"/> palavras, ou <c>null</c> se as palavras do texto não
    /// casarem com as dos tokens. O texto vem do próprio trecho, para não quebrar acentos divididos entre tokens.
    /// </summary>
    private static RecognizedSegment? KeepFirstWords(RecognizedSegment segment, int count)
    {
        var tokenWords = TokenWordMerger.GroupTokensIntoWords(segment.Tokens);
        if (TextTokens.Split(segment.Text).Length != tokenWords.Count)
        {
            return null;
        }

        var lastEnd = tokenWords[count - 1].End ?? segment.End;
        return segment with
        {
            Text = segment.Text[..EndOfWords(segment.Text, count)],
            Tokens = segment.Tokens[..tokenWords[count].FirstToken],
            End = lastEnd < segment.Start ? segment.Start : lastEnd < segment.End ? lastEnd : segment.End,
        };
    }

    /// <summary>Posição logo depois da palavra número <paramref name="count"/> (palavras separadas por espaço).</summary>
    private static int EndOfWords(string text, int count)
    {
        var index = 0;
        for (var word = 0; word < count; word++)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            while (index < text.Length && !char.IsWhiteSpace(text[index]))
            {
                index++;
            }
        }

        return index;
    }
}
