using LessonsPlayer.Core.Transcription;
using static LessonsPlayer.Core.Tests.TestData;

namespace LessonsPlayer.Core.Tests.Transcription;

/// <summary>
/// Palavras que o DTW empilha no último instante alinhado (ver <see cref="UnalignedTail"/>). Os casos imitam os
/// tokens medidos nos vídeos reais: o fim alinhado do último trecho é o último quadro do DTW, 20 ms antes do fim.
/// </summary>
public sealed class UnalignedTailTests
{
    private static RecognizedToken Token(string text, int startMs, int alignedEndMs) =>
        new(text, Ms(startMs), Ms(startMs), Ms(alignedEndMs));

    private static RecognizedSegment SegmentOf(int startMs, int endMs, string text, params RecognizedToken[] tokens) =>
        new(Ms(startMs), Ms(endMs), text, [.. tokens]);

    /// <summary>Aula Khan (large-v3-turbo), 7:54: "...in such a specific way that it's a little bit more than a plane."</summary>
    [Fact]
    public void Words_stacked_at_the_end_of_the_aligned_audio_are_cut_from_the_segment()
    {
        var segment = SegmentOf(0, 30_000, " the wings are designed in such a specific way that it's a plane.",
            Token(" the", 26_500, 27_000),
            Token(" wings", 27_000, 27_500),
            Token(" are", 27_500, 28_000),
            Token(" designed", 28_000, 28_500),
            Token(" in", 28_500, 29_000),
            Token(" such", 29_000, 29_200),
            Token(" a", 29_200, 29_400),
            Token(" specific", 29_400, 29_980), // esticada até o último quadro
            Token(" way", 29_500, 29_980),
            Token(" that", 29_600, 29_980),
            Token(" it", 29_700, 29_980),
            Token("'s", 29_750, 29_980),
            Token(" a", 29_800, 29_980),
            Token(" plane", 29_850, 29_980),
            Token(".", 29_900, 29_980),
            new RecognizedToken("[_TT_1500]", Ms(30_000), Ms(30_000), null));

        var tail = UnalignedTail.Find([segment]).ShouldNotBeNull();

        tail.StackedWords.ShouldBe(5); // way, that, it's, a, plane.
        tail.DroppedWords.ShouldBe(6); // e a esticada: specific
        tail.AlignedUntil.ShouldBe(Ms(30_000));
        tail.LastAlignedEnd.ShouldBe(Ms(29_400));
        var kept = tail.Kept.ShouldHaveSingleItem();
        kept.Text.ShouldBe(" the wings are designed in such a");
        kept.Tokens.Select(t => t.Text).ShouldBe([" the", " wings", " are", " designed", " in", " such", " a"]);
        (kept.Start, kept.End).ShouldBe((Ms(0), Ms(29_400)));
        TokenWordMerger.ToSegment(kept)!.Text.ShouldBe("the wings are designed in such a");
    }

    /// <summary>NASA (small.en), 9:33: "Tell us about the mission." era fala real, a 0,3 s do tempo certo.</summary>
    [Fact]
    public void Up_to_three_stacked_words_are_kept()
    {
        var segment = SegmentOf(5_000, 9_970, " Tell us about the mission.",
            Token(" Tell", 9_600, 9_810),
            Token(" us", 9_810, 9_950),
            Token(" about", 9_850, 9_950),
            Token(" the", 9_900, 9_950),
            Token(" mission", 9_920, 9_950),
            Token(".", 9_940, 9_950));

        UnalignedTail.Find([segment]).ShouldBeNull();
    }

    /// <summary>
    /// NASA (small), 11:52: trechos de 1 s, cada um com a primeira palavra esticada até o último quadro e o resto
    /// empilhado. Os trechos depois da última palavra alinhada saem inteiros.
    /// </summary>
    [Fact]
    public void Whole_segments_after_the_last_aligned_word_are_dropped()
    {
        var aligned = SegmentOf(0, 1_000, " It spins on its own.",
            Token(" It", 0, 2_100), Token(" spins", 100, 2_300), Token(" on", 200, 2_500), Token(" its", 300, 2_700),
            Token(" own", 400, 2_900), Token(".", 500, 2_910));
        var look = SegmentOf(1_000, 2_000, " Look at that.",
            Token(" Look", 1_000, 2_980), Token(" at", 1_300, 2_980), Token(" that", 1_600, 2_980), Token(".", 1_900, 2_980));
        var thanks = SegmentOf(2_000, 3_000, " Thank you guys.",
            Token(" Thank", 2_000, 2_980), Token(" you", 2_300, 2_980), Token(" guys", 2_600, 2_980), Token(".", 2_900, 2_980));

        var tail = UnalignedTail.Find([aligned, look, thanks]).ShouldNotBeNull();

        tail.StackedWords.ShouldBe(4); // at, that., you, guys.
        tail.DroppedWords.ShouldBe(6);
        tail.Kept.ShouldBe([aligned]);
        tail.LastAlignedEnd.ShouldBe(Ms(2_900)); // a fatia seguinte recomeça aqui, antes de "Look"
    }

    /// <summary>NASA (small), 6:18–6:25: só texto inventado, nada alinhado antes dele.</summary>
    [Fact]
    public void Slice_without_aligned_words_keeps_nothing()
    {
        var segment = SegmentOf(0, 7_000, " The United States Senate and the United States",
            Token(" The", 0, 6_980),
            Token(" United", 300, 6_980),
            Token(" States", 900, 6_980),
            Token(" Senate", 1_400, 6_980),
            Token(" and", 2_000, 6_980),
            Token(" the", 2_200, 6_980),
            Token(" United", 2_500, 6_980),
            Token(" States", 3_000, 6_980));

        var tail = UnalignedTail.Find([segment]).ShouldNotBeNull();

        tail.Kept.ShouldBeEmpty();
        tail.LastAlignedEnd.ShouldBeNull();
        (tail.StackedWords, tail.DroppedWords).ShouldBe((7, 8));
    }

    /// <summary>O texto vem do trecho: os tokens podem ter um caractere UTF-8 dividido ("�"), o texto não.</summary>
    [Fact]
    public void Kept_text_comes_from_the_segment_text_so_split_characters_survive()
    {
        var segment = SegmentOf(0, 5_000, " Ação rápida e fácil de ver aqui.",
            Token(" A", 0, 300),
            Token("�", 300, 400),
            Token("�ão", 400, 500),
            Token(" r", 500, 700),
            Token("�pida", 700, 900),
            Token(" e", 900, 4_980),
            Token(" f", 1_000, 4_980),
            Token("�cil", 1_100, 4_980),
            Token(" de", 1_200, 4_980),
            Token(" ver", 1_300, 4_980),
            Token(" aqui", 1_400, 4_980),
            Token(".", 1_500, 4_980));

        var kept = UnalignedTail.Find([segment]).ShouldNotBeNull().Kept.ShouldHaveSingleItem();

        kept.Text.ShouldBe(" Ação rápida");
        kept.Tokens.Length.ShouldBe(5);
        kept.End.ShouldBe(Ms(900));
    }

    [Fact]
    public void Segment_whose_text_does_not_match_its_tokens_is_dropped_whole()
    {
        var before = SegmentOf(0, 1_000, " Fine.", Token(" Fine", 0, 800), Token(".", 800, 900));
        // " a lot" num token só: 7 palavras nos tokens e 8 no texto. Não dá para saber onde cortar o texto.
        var mismatched = SegmentOf(1_000, 5_000, " We have a lot of time here now",
            Token(" We", 1_000, 1_200),
            Token(" have", 1_200, 1_400),
            Token(" a lot", 1_400, 4_980),
            Token(" of", 1_500, 4_980),
            Token(" time", 1_600, 4_980),
            Token(" here", 1_700, 4_980),
            Token(" now", 1_800, 4_980));

        var tail = UnalignedTail.Find([before, mismatched]).ShouldNotBeNull();

        tail.Kept.ShouldBe([before]);
        tail.LastAlignedEnd.ShouldBe(Ms(800));
        tail.DroppedWords.ShouldBe(7);
    }

    /// <summary>
    /// StoryCorps (large-v3-turbo), 2:02: decodificada de novo sem o prompt, uma fatia de 1 s no começo da música
    /// virou só "Music", esticado até o último instante alinhado. Isso não é palavra que o DTW achou no áudio.
    /// </summary>
    [Fact]
    public void Slice_has_aligned_words_only_if_some_word_ends_before_the_last_aligned_instant()
    {
        var music = SegmentOf(122_840, 123_860, " Music", Token(" Music", 122_840, 123_820));
        var speech = SegmentOf(0, 2_000, " Hello there.", Token(" Hello", 0, 600), Token(" there", 600, 1_100), Token(".", 1_100, 1_980));

        UnalignedTail.HasAlignedWords([music]).ShouldBeFalse();
        UnalignedTail.HasAlignedWords([speech]).ShouldBeTrue();
        UnalignedTail.HasAlignedWords([]).ShouldBeFalse();
    }

    [Fact]
    public void Slice_without_alignment_or_without_segments_has_no_tail()
    {
        static RecognizedToken Unaligned(string text) => new(text, Ms(0), Ms(2_000), AlignedEnd: null);
        var noDtw = SegmentOf(0, 2_000, " one two three four five",
            Unaligned(" one"), Unaligned(" two"), Unaligned(" three"), Unaligned(" four"), Unaligned(" five"));

        UnalignedTail.Find([noDtw]).ShouldBeNull();
        UnalignedTail.Find([]).ShouldBeNull();
    }
}
