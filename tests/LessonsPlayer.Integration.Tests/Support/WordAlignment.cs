using System.Globalization;

namespace LessonsPlayer.Integration.Tests.Support;

/// <summary>Casa duas sequências de palavras (LCS sobre o texto normalizado) para comparar tempos.</summary>
internal static class WordAlignment
{
    public static List<(int Expected, int Actual)> Match(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        var a = expected.Select(Normalize).ToArray();
        var b = actual.Select(Normalize).ToArray();
        var lengths = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                lengths[i, j] = a[i] == b[j] ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var pairs = new List<(int, int)>();
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y])
            {
                pairs.Add((x++, y++));
            }
            else if (lengths[x + 1, y] >= lengths[x, y + 1])
            {
                x++;
            }
            else
            {
                y++;
            }
        }

        return pairs;
    }

    private static string Normalize(string word) =>
        new string([.. word.Where(char.IsLetterOrDigit)]).ToLower(CultureInfo.InvariantCulture);
}
