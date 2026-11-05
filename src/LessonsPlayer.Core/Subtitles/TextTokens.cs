using System.Globalization;

namespace LessonsPlayer.Core.Subtitles;

/// <summary>Utilitários para dividir e comparar texto em palavras.</summary>
internal static class TextTokens
{
    private static readonly char[] Punctuation = ".,;:!?¡¿…\"'“”‘’«»()[]{}-–—".ToCharArray();

    /// <summary>Divide o texto em palavras usando qualquer espaço em branco como separador.</summary>
    public static string[] Split(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Indica se o texto é exatamente uma palavra (não vazio e sem espaços).</summary>
    public static bool IsSingleToken(string? text) =>
        !string.IsNullOrEmpty(text) && !text.Any(char.IsWhiteSpace);

    /// <summary>
    /// Forma normalizada para comparação: minúsculas e sem pontuação nas pontas.
    /// Assim "Olá," e "olá" são considerados a mesma palavra.
    /// </summary>
    public static string Normalize(string token)
    {
        var trimmed = token.AsSpan().Trim().TrimStart(Punctuation).TrimEnd(Punctuation);
        var core = trimmed.IsEmpty ? token.AsSpan().Trim() : trimmed;
        return core.ToString().ToLower(CultureInfo.InvariantCulture);
    }
}
