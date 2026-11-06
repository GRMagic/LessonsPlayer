using LessonsPlayer.Core.Subtitles;

namespace LessonsPlayer.Core.Transcription;

/// <summary>
/// Descarta as palavras que o Whisper inventa em trechos sem fala.
/// <para>
/// <b>Causa:</b> em trechos longos sem fala (música, créditos, fim de transmissão), o Whisper às vezes escreve
/// texto que ninguém disse, com a mesma confiança da fala real: <c>"Thank you."</c>, <c>"you you"</c>,
/// <c>"We will begin the session."</c> repetido, ou uma frase parecida com o prompt. Medido em 3 vídeos reais
/// (aula, conversa com música e entrevista via satélite) com small, small.en e large-v3-turbo: nesses trechos a
/// probabilidade de "sem fala" do whisper.cpp ficou em ~0 e a probabilidade média dos tokens em 0,77–0,99, a
/// mesma faixa da fala real. A razão de compressão (&gt; 2,4 no Whisper original) não pega frases curtas.
/// Nenhum desses sinais separa o texto inventado do real; o detector de voz (VAD) separa.
/// </para>
/// <para>
/// <b>Regra:</b> uma palavra só fica se começar dentro de uma região com fala do VAD ampliada em
/// <see cref="SpeechMargin"/> para os dois lados. A margem cobre o erro do alinhamento e a fala fraca que o VAD
/// corta (sílaba final, voz embargada). Conta o início, e não o intervalo inteiro, porque uma palavra inventada
/// costuma ser esticada até a fala seguinte. Medido nos mesmos vídeos: sumiram todas as palavras inventadas que o
/// critério de referência apontava (aplicado à legenda de antes), menos 2 numa região em que o próprio VAD ouviu algo;
/// junto com o pulo dos trechos sem fala no transcritor, não sobrou nenhuma. Das palavras casadas com a legenda
/// automática do YouTube, saíram no máximo 3 por vídeo, todas postas pelo Whisper segundos antes da fala. Com 1 s
/// de margem, a perda chegava a 8 palavras; contando o intervalo inteiro da palavra, sobravam mais inventadas.
/// </para>
/// </summary>
public static class HallucinationFilter
{
    /// <summary>Folga ao redor de cada região de fala dentro da qual uma palavra pode começar.</summary>
    public static readonly TimeSpan SpeechMargin = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Mantém só as palavras que começam perto de fala. Retorna o trecho sem as palavras inventadas, ou
    /// <c>null</c> se nenhuma sobrar. Sem detecção de voz (<paramref name="speech"/> nulo), o trecho volta inteiro.
    /// </summary>
    public static Segment? KeepWordsNearSpeech(Segment segment, SpeechActivity? speech)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (speech is null)
        {
            return segment;
        }

        var kept = segment.Words.RemoveAll(w => !speech.IsNearSpeech(w.Start, SpeechMargin));
        return kept.Length == segment.Words.Length ? segment
            : kept.IsEmpty ? null
            : new Segment(kept);
    }
}
