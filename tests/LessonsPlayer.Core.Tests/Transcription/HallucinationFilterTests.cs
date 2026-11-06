using LessonsPlayer.Core.Transcription;
using static LessonsPlayer.Core.Tests.TestData;

namespace LessonsPlayer.Core.Tests.Transcription;

public sealed class HallucinationFilterTests
{
    /// <summary>Fala em 10–20 s; depois, só música.</summary>
    private static readonly SpeechActivity Speech = new([new SpeechRegion(Ms(10_000), Ms(20_000))]);

    [Fact]
    public void Keeps_segment_inside_speech_unchanged()
    {
        var segment = Seg(W(12_000, 12_400, "Hello"), W(12_400, 13_000, "everyone."));

        HallucinationFilter.KeepWordsNearSpeech(segment, Speech).ShouldBeSameAs(segment);
    }

    [Fact]
    public void Drops_segment_invented_during_music()
    {
        var invented = Seg(W(45_080, 45_560, "you"), W(49_200, 49_460, "Thank"), W(49_460, 51_757, "you."));

        HallucinationFilter.KeepWordsNearSpeech(invented, Speech).ShouldBeNull();
    }

    [Fact]
    public void Removes_only_the_invented_tail_of_a_segment()
    {
        var segment = Seg(W(19_000, 19_500, "the"), W(19_500, 20_200, "end."), W(26_000, 26_300, "Thank"), W(26_300, 27_000, "you."));

        HallucinationFilter.KeepWordsNearSpeech(segment, Speech)!.Text.ShouldBe("the end.");
    }

    [Fact]
    public void Keeps_words_that_start_within_the_margin_around_speech()
    {
        var margin = (int)HallucinationFilter.SpeechMargin.TotalMilliseconds;
        var segment = Seg(W(10_000 - margin + 100, 10_200, "So,"), W(20_000 + margin - 100, 20_000 + margin + 400, "bye."));

        HallucinationFilter.KeepWordsNearSpeech(segment, Speech).ShouldBeSameAs(segment);
    }

    [Fact]
    public void Drops_invented_word_stretched_until_the_next_speech()
    {
        // O alinhamento estica a última palavra inventada até onde a próxima começa.
        var segment = Seg(W(30_000, 30_400, "Thank"), W(30_400, 40_000, "you."));
        var speech = new SpeechActivity([new SpeechRegion(Ms(10_000), Ms(20_000)), new SpeechRegion(Ms(40_500), Ms(50_000))]);

        HallucinationFilter.KeepWordsNearSpeech(segment, speech).ShouldBeNull();
    }

    [Fact]
    public void Keeps_everything_without_speech_detection()
    {
        var segment = Seg(W(45_000, 45_500, "Music."));

        HallucinationFilter.KeepWordsNearSpeech(segment, speech: null).ShouldBeSameAs(segment);
    }
}
