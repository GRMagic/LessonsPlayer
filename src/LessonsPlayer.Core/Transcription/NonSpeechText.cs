namespace LessonsPlayer.Core.Transcription;

/// <summary>
/// Marcações de não fala que o Whisper escreve no lugar de texto. Não são palavras ditas e não devem virar legenda.
/// <para>
/// Vistas nas nossas legendas (Whisper.net 1.9.1, small/small.en/base): <c>[BLANK_AUDIO]</c> (silêncio),
/// <c>[Music]</c>, <c>[MUSIC PLAYING]</c>, <c>[ Silence ]</c>, <c>[static]</c>, <c>[typing sounds]</c>. Entre
/// parênteses ou com ♪ ainda não apareceram, mas são tratadas igual: o whisper.cpp lista <c>(</c>, <c>)</c> e
/// <c>♪</c> entre os tokens de não fala da opção <c>suppress_nst</c> (desligada e não exposta pelo Whisper.net).
/// </para>
/// </summary>
public static class NonSpeechText
{
    /// <summary>
    /// Indica se o texto não tem nenhuma letra ou dígito fora de colchetes e parênteses
    /// (só marcações, notas musicais ou pontuação).
    /// </summary>
    public static bool IsAnnotationOnly(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var depth = 0;
        foreach (var c in text)
        {
            switch (c)
            {
                case '[' or '(':
                    depth++;
                    break;
                case ']' or ')':
                    depth = Math.Max(0, depth - 1);
                    break;
                default:
                    if (depth == 0 && char.IsLetterOrDigit(c))
                    {
                        return false;
                    }

                    break;
            }
        }

        return true;
    }
}
