using LessonsPlayer.Core.Subtitles;
using static LessonsPlayer.Core.Tests.TestData;

namespace LessonsPlayer.Core.Tests.Subtitles;

public sealed class WordIndexTests
{
    private readonly WordIndex _index = new(Sample());

    [Fact]
    public void Before_first_word_returns_null() =>
        new WordIndex(Tr(Seg(W(500, 900, "a")))).FindLastStartedAt(Ms(100)).ShouldBeNull();

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(399, 0, 0)]
    [InlineData(400, 0, 1)]
    [InlineData(1200, 0, 1)] // pausa entre trechos: continua na última palavra iniciada
    [InlineData(1500, 1, 0)]
    [InlineData(99_999, 1, 1)]
    public void Finds_last_word_started_at_position(int positionMs, int segment, int word) =>
        _index.FindLastStartedAt(Ms(positionMs)).ShouldBe(new WordLocation(segment, word));

    [Fact]
    public void Words_with_same_start_resolve_to_the_last_one()
    {
        var index = new WordIndex(Tr(Seg(W(100, 100, "a"), W(100, 300, "b"))));

        index.FindLastStartedAt(Ms(100)).ShouldBe(new WordLocation(0, 1));
    }

    [Fact]
    public void Empty_transcript_has_no_words() =>
        new WordIndex(Transcript.Empty).FindLastStartedAt(Ms(1)).ShouldBeNull();
}
