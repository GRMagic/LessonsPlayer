using LessonsPlayer.Core.Subtitles;

namespace LessonsPlayer.Presentation.Tests;

internal static class Samples
{
    public static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    /// <summary>"Olá pessoal" (0–1000 ms) e "bem vindos" (1500–2500 ms).</summary>
    public static Transcript TwoSegments() => new(
        TranscriptMetadata.Empty,
        [
            new Segment([new Word(Ms(0), Ms(400), "Olá"), new Word(Ms(400), Ms(1000), "pessoal")]),
            new Segment([new Word(Ms(1500), Ms(2000), "bem"), new Word(Ms(2000), Ms(2500), "vindos")]),
        ]);

    public static Transcript SingleWord(int startMs, int endMs, string text) =>
        new(TranscriptMetadata.Empty, [new Segment([new Word(Ms(startMs), Ms(endMs), text)])]);
}
