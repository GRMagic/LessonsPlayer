namespace LessonsPlayer.Core.Subtitles;

/// <summary>
/// Uma palavra da legenda e o intervalo em que ela é falada.
/// Imutável: alterações geram uma nova instância.
/// </summary>
public sealed record Word
{
    public Word(TimeSpan start, TimeSpan end, string text)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(start, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);
        if (!TextTokens.IsSingleToken(text))
        {
            throw new ArgumentException("A palavra não pode ser vazia nem conter espaços.", nameof(text));
        }

        Start = start;
        End = end;
        Text = text;
    }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public string Text { get; }

    public TimeSpan Duration => End - Start;

    public Word WithText(string text) => new(Start, End, text);

    public Word WithTiming(TimeSpan start, TimeSpan end) => new(start, end, Text);

    public override string ToString() => $"{Text} [{Start:g} – {End:g}]";
}
