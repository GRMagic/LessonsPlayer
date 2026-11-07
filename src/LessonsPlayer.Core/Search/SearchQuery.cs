using System.Globalization;
using System.Text;
using LessonsPlayer.Core.Subtitles;

namespace LessonsPlayer.Core.Search;

/// <summary>
/// O que o usuário digitou na busca, pronto para comparar. Ignora acentos e maiúsculas ("licao" acha "Lição") e
/// casa por palavras: cada palavra digitada precisa aparecer no texto, em qualquer ordem ("perfect present" acha
/// "present perfect").
/// </summary>
public sealed class SearchQuery
{
    public static SearchQuery Empty { get; } = new([]);

    private SearchQuery(IReadOnlyList<string> terms) => Terms = terms;

    /// <summary>As palavras da busca, já normalizadas.</summary>
    public IReadOnlyList<string> Terms { get; }

    public bool IsEmpty => Terms.Count == 0;

    public static SearchQuery Parse(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? Empty
            : new([.. Normalize(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal)]);

    /// <summary>Verdadeiro se todas as palavras da busca aparecem no texto. Uma busca vazia casa com tudo.</summary>
    public bool Matches(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (IsEmpty)
        {
            return true;
        }

        var normalized = Normalize(text);
        return Terms.All(term => normalized.Contains(term, StringComparison.Ordinal));
    }

    /// <summary>Os trechos da legenda que casam com a busca, em ordem, até <paramref name="limit"/>.</summary>
    public IEnumerable<SearchMatch> FindIn(Transcript transcript, int limit = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        if (IsEmpty)
        {
            return [];
        }

        return transcript.Segments
            .Select((segment, index) => (Segment: segment, Index: index))
            .Where(x => Matches(x.Segment.Text))
            .Take(limit)
            .Select(x => new SearchMatch(x.Index, x.Segment.Start, x.Segment.Text));
    }

    /// <summary>Minúsculas, sem acentos (á → a, ç → c) e com o apóstrofo tipográfico trocado pelo simples.</summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var decomposed = text.Replace('’', '\'').Replace('‘', '\'').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}

/// <summary>Um trecho da legenda que casa com a busca.</summary>
/// <param name="SegmentIndex">Posição do trecho na legenda.</param>
/// <param name="Start">Início do trecho (a primeira palavra).</param>
/// <param name="Text">O texto do trecho.</param>
public sealed record SearchMatch(int SegmentIndex, TimeSpan Start, string Text);
