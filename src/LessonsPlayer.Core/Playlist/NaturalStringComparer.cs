using System.Globalization;

namespace LessonsPlayer.Core.Playlist;

/// <summary>
/// Compara textos tratando sequências de dígitos como números: "aula2" &lt; "aula10".
/// O restante é comparado sem diferenciar maiúsculas, respeitando acentos.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string?>
{
    private static readonly CompareInfo Culture = CultureInfo.InvariantCulture.CompareInfo;

    private NaturalStringComparer()
    {
    }

    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            var runX = NextRun(x, ref i, out var isNumberX);
            var runY = NextRun(y, ref j, out var isNumberY);

            var result = isNumberX && isNumberY
                ? CompareNumbers(runX, runY)
                : Culture.Compare(runX, runY, CompareOptions.IgnoreCase);

            if (result != 0)
            {
                return result;
            }
        }

        var remaining = (x.Length - i).CompareTo(y.Length - j);
        return remaining != 0 ? remaining : string.CompareOrdinal(x, y);
    }

    /// <summary>Lê a próxima sequência só de dígitos ou só de não dígitos.</summary>
    private static ReadOnlySpan<char> NextRun(string text, ref int index, out bool isNumber)
    {
        var start = index;
        isNumber = char.IsAsciiDigit(text[index]);
        while (index < text.Length && char.IsAsciiDigit(text[index]) == isNumber)
        {
            index++;
        }

        return text.AsSpan(start, index - start);
    }

    private static int CompareNumbers(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        var trimmedA = a.TrimStart('0');
        var trimmedB = b.TrimStart('0');
        return trimmedA.Length != trimmedB.Length
            ? trimmedA.Length.CompareTo(trimmedB.Length)
            : trimmedA.SequenceCompareTo(trimmedB);
    }
}
