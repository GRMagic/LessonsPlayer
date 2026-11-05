using LessonsPlayer.Core.Transcription;
using static LessonsPlayer.Core.Tests.TestData;

namespace LessonsPlayer.Core.Tests.Transcription;

public sealed class TokenWordMergerTests
{
    /// <summary>Token com início estimado pelo decodificador e fim pelo alinhamento DTW.</summary>
    private static RecognizedToken Token(string text, int startMs, int alignedEndMs) =>
        new(text, Ms(startMs), Ms(startMs), Ms(alignedEndMs));

    private static RecognizedSegment SegmentOf(int startMs, int endMs, string text, params RecognizedToken[] tokens) =>
        new(Ms(startMs), Ms(endMs), text, [.. tokens]);

    [Fact]
    public void Word_starts_where_previous_token_ends_and_ends_at_its_last_letter_token()
    {
        var segment = SegmentOf(1000, 3000, " Hello everyone, welcome.",
            new RecognizedToken("[_BEG_]", Ms(1000), Ms(1000), null),
            Token(" Hello", 1100, 1500),
            Token(" every", 1500, 1700),
            Token("one", 1700, 1900),
            Token(",", 1900, 2200),  // a vírgula cobre a pausa
            Token(" welcome", 2200, 2700),
            Token(".", 2700, 3000),
            new RecognizedToken("[_TT_150]", Ms(3000), Ms(3000), null));

        var result = TokenWordMerger.ToSegment(segment)!;

        result.Words.Select(w => (w.Text, w.Start, w.End)).ShouldBe(
        [
            ("Hello", Ms(1100), Ms(1500)),     // primeira palavra: início estimado pelo decodificador
            ("everyone,", Ms(1500), Ms(1900)),
            ("welcome.", Ms(2200), Ms(2700)),  // começa depois da pausa absorvida pela vírgula
        ]);
    }

    [Fact]
    public void Word_text_comes_from_segment_text_not_from_broken_token_text()
    {
        // "ação" com o "ç" dividido em dois tokens que, isolados, não decodificam.
        var segment = SegmentOf(0, 1000, " ação boa",
            Token(" a", 100, 150), Token("�", 150, 160), Token("�", 160, 170),
            Token("ão", 170, 400), Token(" boa", 400, 800));

        TokenWordMerger.ToSegment(segment)!.Words.Select(w => (w.Text, w.Start, w.End)).ShouldBe(
        [
            ("ação", Ms(100), Ms(400)),
            ("boa", Ms(400), Ms(800)),
        ]);
    }

    [Fact]
    public void Falls_back_to_decoder_timestamps_when_alignment_is_missing()
    {
        var segment = SegmentOf(0, 2000, " oi tudo",
            new RecognizedToken(" oi", Ms(300), Ms(500), AlignedEnd: null),
            new RecognizedToken(" tudo", Ms(600), Ms(900), AlignedEnd: null));

        TokenWordMerger.ToSegment(segment)!.Words.Select(w => (w.Start, w.End)).ShouldBe(
        [
            (Ms(300), Ms(500)),   // termina onde a próxima começa (fim do token anterior)
            (Ms(500), Ms(2000)),
        ]);
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
        var segment = SegmentOf(0, 1000, "aaaa bbbb cccc", Token(" aaaa", 0, 300), Token(" bbbbcccc", 300, 1000));

        var words = TokenWordMerger.ToSegment(segment)!.Words;

        words[0].Start.ShouldBe(Ms(0));
        words[1].Start.ShouldBe(Ms(300));
        words[2].Start.ShouldBeInRange(Ms(300), Ms(1000));
        words[0].End.ShouldBe(words[1].Start);
    }

    [Fact]
    public void Times_are_clamped_to_segment_and_made_monotonic()
    {
        var segment = SegmentOf(1000, 2000, " a b c", Token(" a", 500, 1800), Token(" b", 1800, 1200), Token(" c", 1200, 2500));

        var words = TokenWordMerger.ToSegment(segment)!.Words;

        words.Select(w => w.Start).ShouldBe([Ms(1000), Ms(1800), Ms(1800)]);
        words.ShouldAllBe(w => w.End >= w.Start && w.End <= Ms(2000));
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
