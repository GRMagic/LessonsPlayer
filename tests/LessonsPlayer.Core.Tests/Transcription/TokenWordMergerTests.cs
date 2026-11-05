using LessonsPlayer.Core.Transcription;
using static LessonsPlayer.Core.Tests.TestData;

namespace LessonsPlayer.Core.Tests.Transcription;

public sealed class TokenWordMergerTests
{
    private static RecognizedToken Token(string text, int alignedMs) =>
        new(text, Ms(alignedMs), Ms(alignedMs), Ms(alignedMs));

    private static RecognizedSegment SegmentOf(int startMs, int endMs, string text, params RecognizedToken[] tokens) =>
        new(Ms(startMs), Ms(endMs), text, [.. tokens]);

    [Fact]
    public void Groups_sub_word_tokens_and_uses_aligned_start_of_first_token()
    {
        var segment = SegmentOf(1000, 3000, " Hello everyone, welcome.",
            Token("[_BEG_]", 1000),
            Token(" Hello", 1100),
            Token(" every", 1500),
            Token("one", 1700),
            Token(",", 1900),
            Token(" welcome", 2200),
            Token(".", 2800),
            Token("[_TT_150]", 3000));

        var result = TokenWordMerger.ToSegment(segment)!;

        result.Words.Select(w => (w.Text, w.Start, w.End)).ShouldBe(
        [
            ("Hello", Ms(1100), Ms(1500)),
            ("everyone,", Ms(1500), Ms(2200)),
            ("welcome.", Ms(2200), Ms(3000)),
        ]);
    }

    [Fact]
    public void Word_text_comes_from_segment_text_not_from_broken_token_text()
    {
        // "ação" com o "ç" dividido em dois tokens que, isolados, não decodificam.
        var segment = SegmentOf(0, 1000, " ação boa",
            Token(" a", 100), Token("�", 150), Token("�", 160), Token("ão", 200), Token(" boa", 600));

        TokenWordMerger.ToSegment(segment)!.Words.Select(w => (w.Text, w.Start)).ShouldBe(
        [
            ("ação", Ms(100)),
            ("boa", Ms(600)),
        ]);
    }

    [Fact]
    public void Falls_back_to_decoder_timestamps_when_alignment_is_missing()
    {
        var segment = SegmentOf(0, 1000, " oi", new RecognizedToken(" oi", Ms(300), Ms(500), AlignedStart: null));

        TokenWordMerger.ToSegment(segment)!.Words[0].Start.ShouldBe(Ms(300));
    }

    [Fact]
    public void Without_tokens_distributes_segment_time()
    {
        var result = TokenWordMerger.ToSegment(SegmentOf(0, 1000, "ab ab"))!;

        result.Words.Select(w => w.Start).ShouldBe([Ms(0), Ms(500)]);
    }

    [Fact]
    public void Word_count_mismatch_interpolates_by_character_position()
    {
        // Tokens formam 2 palavras, mas o texto tem 3: a do meio é interpolada.
        var segment = SegmentOf(0, 1000, "aaaa bbbb cccc", Token(" aaaa", 0), Token(" bbbbcccc", 300));

        var starts = TokenWordMerger.ToSegment(segment)!.Words.Select(w => w.Start).ToArray();

        starts[0].ShouldBe(Ms(0));
        starts[1].ShouldBe(Ms(300));
        starts[2].ShouldBeInRange(Ms(300), Ms(1000));
    }

    [Fact]
    public void Aligned_times_are_clamped_and_made_monotonic()
    {
        var segment = SegmentOf(1000, 2000, " a b c", Token(" a", 500), Token(" b", 1800), Token(" c", 1200));

        TokenWordMerger.ToSegment(segment)!.Words.Select(w => w.Start).ShouldBe([Ms(1000), Ms(1800), Ms(1800)]);
    }

    [Fact]
    public void Empty_segment_text_returns_null() =>
        TokenWordMerger.ToSegment(SegmentOf(0, 1000, "  ")).ShouldBeNull();

    [Theory]
    [InlineData("[_BEG_]", true)]
    [InlineData("[_TT_42]", true)]
    [InlineData("<|endoftext|>", true)]
    [InlineData(" [Music]", false)]
    [InlineData(" word", false)]
    public void Detects_special_tokens(string text, bool expected) =>
        TokenWordMerger.IsSpecialToken(text).ShouldBe(expected);
}
