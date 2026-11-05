using LessonsPlayer.Core.Subtitles;
using LessonsPlayer.Core.Transcription;
using static LessonsPlayer.Core.Tests.TestData;

namespace LessonsPlayer.Core.Tests.Transcription;

public sealed class SentenceRegrouperTests
{
    [Fact]
    public void Joins_sentence_cut_in_two_segments()
    {
        // Corte real do Whisper (small, en-US): a frase continua no trecho seguinte, logo depois.
        Segment[] input =
        [
            Piece(10_180, 15_500, "Before we begin, make sure you have your notebook, a pencil, the vocabulary list"),
            Piece(15_680, 19_340, "from last week, and the audio files that I shared with you on Monday."),
            Piece(20_300, 24_000, "In the first part of the lesson, we will review the verbs."),
        ];

        var result = SentenceRegrouper.Regroup(input);

        Texts(result).ShouldBe(
        [
            "Before we begin, make sure you have your notebook, a pencil, the vocabulary list from last week, and the audio files that I shared with you on Monday.",
            "In the first part of the lesson, we will review the verbs.",
        ]);
        ShouldKeepWords(input, result);
    }

    [Fact]
    public void Completes_unfinished_sentence_and_keeps_rest_of_next_segment_as_its_own()
    {
        // O trecho do meio termina uma frase e começa outra: cada frase sai num trecho.
        Segment[] input =
        [
            Piece(73_540, 79_040, "Agora vamos ouvir um pequeno diálogo entre dois amigos que se encontram numa"),
            Piece(79_040, 85_920, "cafeteria depois de muito tempo sem se ver. Eles conversam sobre o que fizeram no"),
            Piece(85_920, 92_440, "último fim de semana e sobre os planos para as férias."),
        ];

        var result = SentenceRegrouper.Regroup(input);

        Texts(result).ShouldBe(
        [
            "Agora vamos ouvir um pequeno diálogo entre dois amigos que se encontram numa cafeteria depois de muito tempo sem se ver.",
            "Eles conversam sobre o que fizeram no último fim de semana e sobre os planos para as férias.",
        ]);
        ShouldKeepWords(input, result);
    }

    [Fact]
    public void Finished_sentences_before_the_cut_stay_in_their_own_segment()
    {
        Segment[] input = [Piece(0, 3000, "Olá, pessoal. Hoje vamos estudar"), Piece(3100, 4000, "os verbos.")];

        var result = SentenceRegrouper.Regroup(input);

        Texts(result).ShouldBe(["Olá, pessoal.", "Hoje vamos estudar os verbos."]);
        ShouldKeepWords(input, result);
    }

    [Fact]
    public void Joins_through_middle_segments_without_any_sentence_end()
    {
        Segment[] input =
        [
            Piece(0, 4000, "If you have any questions about the exercises,"),
            Piece(4000, 8000, "the grammar or the pronunciation, send me"),
            Piece(8000, 12_000, "a message in the class group."),
        ];

        var result = SentenceRegrouper.Regroup(input);

        Texts(result).ShouldBe(
            ["If you have any questions about the exercises, the grammar or the pronunciation, send me a message in the class group."]);
        ShouldKeepWords(input, result);
    }

    [Fact]
    public void Does_not_join_across_a_long_pause()
    {
        Segment[] input = [Piece(0, 2000, "Então a gente vai ver que"), Piece(4500, 6000, "o resultado é dez.")];

        SentenceRegrouper.Regroup(input).ShouldBe(input);
    }

    [Fact]
    public void Joins_when_pause_is_exactly_the_limit()
    {
        var pause = (int)SentenceRegrouper.MaxPause.TotalMilliseconds;
        Segment[] input = [Piece(0, 2000, "Então a gente vai ver que"), Piece(2000 + pause, 6000, "o resultado é dez.")];

        Texts(SentenceRegrouper.Regroup(input)).ShouldBe(["Então a gente vai ver que o resultado é dez."]);
    }

    [Fact]
    public void Keeps_original_segments_when_joined_sentence_would_be_too_long()
    {
        // Frase de 40 s sem ponto até o fim: maior que o limite, fica como o Whisper entregou.
        Segment[] input =
        [
            Piece(0, 15_000, "and then we talk about this and that"),
            Piece(15_000, 30_000, "and about many other things too"),
            Piece(30_000, 40_000, "until the very end."),
        ];

        SentenceRegrouper.Regroup(input).ShouldBe(input);
    }

    [Fact]
    public void Transcript_without_punctuation_is_unchanged()
    {
        Segment[] input = [Piece(0, 3000, "olá pessoal hoje vamos"), Piece(3000, 6000, "estudar os verbos"), Piece(6000, 9000, "irregulares")];

        SentenceRegrouper.Regroup(input).ShouldBe(input);
    }

    [Fact]
    public void Never_joins_non_speech_segments()
    {
        Segment[] input =
        [
            Piece(0, 2000, "and then"),
            Piece(2000, 3000, "[MUSIC]"),
            Piece(3000, 5000, "we go home."),
            Piece(5000, 6000, "(risos)"),
            Piece(6000, 7000, "Fim."),
        ];

        SentenceRegrouper.Regroup(input).ShouldBe(input);
    }

    [Fact]
    public void Abbreviation_does_not_end_the_completed_sentence()
    {
        Segment[] input = [Piece(0, 2000, "Today I talked to"), Piece(2000, 5000, "Mr. Brown about the test. He agreed.")];

        Texts(SentenceRegrouper.Regroup(input)).ShouldBe(["Today I talked to Mr. Brown about the test.", "He agreed."]);
    }

    [Fact]
    public void Unfinished_last_segment_is_kept()
    {
        Segment[] input = [Piece(0, 2000, "Thank you very much."), Piece(2000, 3000, "See you")];

        SentenceRegrouper.Regroup(input).ShouldBe(input);
    }

    [Fact]
    public void Empty_input_gives_empty_output() => SentenceRegrouper.Regroup([]).ShouldBeEmpty();

    [Theory]
    [InlineData("som.", true)]
    [InlineData("today!", true)]
    [InlineData("why?", true)]
    [InlineData("well…", true)]
    [InlineData("wait...", true)]
    [InlineData("\"Ok.\"", true)]
    [InlineData("(fim.)", true)]
    [InlineData("etc.", true)]
    [InlineData("cada", false)]
    [InlineData("dia,", false)]
    [InlineData("assim:", false)]
    [InlineData("Mr.", false)]
    [InlineData("Dra.", false)]
    [InlineData("e.g.", false)]
    [InlineData("U.S.", false)]
    public void Detects_sentence_end(string word, bool expected) =>
        SentenceRegrouper.EndsSentence(word).ShouldBe(expected);

    /// <summary>Trecho com as palavras do texto distribuídas por igual entre o início e o fim.</summary>
    private static Segment Piece(int startMs, int endMs, string text)
    {
        var words = text.Split(' ');
        var step = (endMs - startMs) / words.Length;
        return new Segment(words.Select((w, i) =>
            W(startMs + (i * step), i == words.Length - 1 ? endMs : startMs + ((i + 1) * step), w)));
    }

    private static string[] Texts(IEnumerable<Segment> segments) => [.. segments.Select(s => s.Text)];

    /// <summary>Só o agrupamento muda: as mesmas palavras, com os mesmos tempos, na mesma ordem.</summary>
    private static void ShouldKeepWords(IEnumerable<Segment> input, IEnumerable<Segment> output) =>
        output.SelectMany(s => s.Words).ShouldBe(input.SelectMany(s => s.Words));
}
