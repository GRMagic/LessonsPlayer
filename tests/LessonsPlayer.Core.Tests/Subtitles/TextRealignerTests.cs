using LessonsPlayer.Core.Subtitles;
using static LessonsPlayer.Core.Tests.TestData;

namespace LessonsPlayer.Core.Tests.Subtitles;

public sealed class TextRealignerTests
{
    private static readonly Word[] Original =
    [
        W(0, 100, "o"),
        W(100, 400, "gato"),
        W(400, 600, "come"),
        W(600, 1000, "peixe."),
    ];

    private static Word[] Realign(string text) =>
        [.. TextRealigner.Realign(Original, TextTokens.Split(text))];

    [Fact]
    public void Unchanged_words_keep_their_timing_even_with_new_case_or_punctuation()
    {
        var result = Realign("O gato come peixe!");

        result.Select(w => (w.Text, w.Start, w.End)).ShouldBe(
        [
            ("O", Ms(0), Ms(100)),
            ("gato", Ms(100), Ms(400)),
            ("come", Ms(400), Ms(600)),
            ("peixe!", Ms(600), Ms(1000)),
        ]);
    }

    [Fact]
    public void Replaced_word_takes_the_interval_of_the_old_one()
    {
        var result = Realign("o cachorro come peixe.");

        result[1].ShouldBe(W(100, 400, "cachorro"));
        result[2].ShouldBe(W(400, 600, "come"));
    }

    [Fact]
    public void Replacing_one_word_with_two_splits_the_interval_proportionally()
    {
        var result = Realign("o gato bebe leite peixe.");

        // 200 ms divididos 4:5 pelo tamanho das palavras.
        result[2].ShouldBe(W(400, 489, "bebe"));
        result[3].ShouldBe(W(489, 600, "leite"));
    }

    [Fact]
    public void Pure_insertion_shares_the_previous_word_interval()
    {
        var result = Realign("o gato preto come peixe.");

        // 300 ms divididos 4:5 pelo tamanho das palavras.
        result[1].ShouldBe(W(100, 233, "gato"));
        result[2].ShouldBe(W(233, 400, "preto"));
        result[3].ShouldBe(W(400, 600, "come"));
    }

    [Fact]
    public void Insertion_at_the_beginning_shares_the_first_word_interval()
    {
        var result = Realign("ontem o gato come peixe.");

        result[0].Start.ShouldBe(Ms(0));
        result[1].End.ShouldBe(Ms(100));
        result[2].ShouldBe(W(100, 400, "gato"));
    }

    [Fact]
    public void Deleted_words_are_dropped_and_others_keep_timing()
    {
        var result = Realign("gato peixe.");

        result.ShouldBe([W(100, 400, "gato"), W(600, 1000, "peixe.")]);
    }

    [Fact]
    public void Completely_new_text_spans_the_original_interval()
    {
        var result = Realign("tudo diferente");

        result[0].Start.ShouldBe(Ms(0));
        result[^1].End.ShouldBe(Ms(1000));
    }

    [Fact]
    public void Empty_text_returns_no_words() => Realign("   ").ShouldBeEmpty();
}
