using System.Globalization;

namespace LessonsPlayer.Presentation.Formatting;

public static class TimeFormat
{
    /// <summary>Formato curto para a interface: <c>m:ss</c>, ou <c>h:mm:ss</c> a partir de uma hora.</summary>
    public static string Compact(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }

        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>Formato preciso para tempos de palavra: <c>m:ss.fff</c>.</summary>
    public static string Precise(TimeSpan time) =>
        (time < TimeSpan.Zero ? TimeSpan.Zero : time).ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture);
}
