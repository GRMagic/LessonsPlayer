namespace LessonsPlayer.Core.Transcription;

/// <summary>
/// Marcações de não fala que o Whisper escreve no lugar de texto, como <c>[BLANK_AUDIO]</c> (silêncio),
/// <c>[Music]</c>, <c>(risos)</c> ou <c>♪♪</c>. Não são palavras ditas e não devem virar legenda.
/// </summary>
internal static class NonSpeechText
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
