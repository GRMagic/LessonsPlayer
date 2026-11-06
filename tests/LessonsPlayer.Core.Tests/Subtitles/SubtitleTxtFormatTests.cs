using LessonsPlayer.Core.Subtitles;
using static LessonsPlayer.Core.Tests.TestData;

namespace LessonsPlayer.Core.Tests.Subtitles;

public sealed class SubtitleTxtFormatTests
{
    [Fact]
    public void Serialize_then_Parse_round_trips_words_and_metadata()
    {
        var created = new DateTimeOffset(2026, 11, 4, 10, 15, 0, TimeSpan.FromHours(-3));
        var original = Sample().WithMetadata(new TranscriptMetadata("aula01.mp3", "small-q5_1", "pt", created));

        var result = SubtitleTxtFormat.Parse(SubtitleTxtFormat.Serialize(original));

        result.HasWarnings.ShouldBeFalse();
        result.Transcript.ShouldBe(original);
    }

    [Fact]
    public void Serialize_writes_readable_header_and_word_lines()
    {
        var text = SubtitleTxtFormat.Serialize(Tr(Seg(W(1240, 1610, "Olá"), W(1610, 4880, "pessoal,"))));

        text.ShouldContain("# LessonsPlayer legenda v1");
        text.ShouldContain("[00:00:01.240 --> 00:00:04.880] Olá pessoal,");
        text.ShouldContain("00:00:01.240 00:00:01.610 Olá");
        text.ShouldContain("00:00:01.610 00:00:04.880 pessoal,");
    }

    [Fact]
    public void Parse_segment_without_word_lines_distributes_time_by_length()
    {
        var result = SubtitleTxtFormat.Parse("[00:10.000 --> 00:12.000] ab abcd ab");

        var words = result.Transcript.Segments.ShouldHaveSingleItem().Words;
        words.Select(w => w.Text).ShouldBe(["ab", "abcd", "ab"]);
        words[0].Start.ShouldBe(Ms(10_000));
        words[1].Start.ShouldBe(Ms(10_500));
        words[2].Start.ShouldBe(Ms(11_500));
        words[2].End.ShouldBe(Ms(12_000));
    }

    [Theory]
    [InlineData("01:02:03.456", 3_723_456)]
    [InlineData("02:03.4", 123_400)]
    [InlineData("02:03,45", 123_450)]
    [InlineData("02:03", 123_000)]
    public void TryParseTime_accepts_supported_formats(string text, int expectedMs)
    {
        SubtitleTxtFormat.TryParseTime(text, out var time).ShouldBeTrue();
        time.ShouldBe(Ms(expectedMs));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("00:61.000")]
    [InlineData("1.5")]
    public void TryParseTime_rejects_invalid_values(string text) =>
        SubtitleTxtFormat.TryParseTime(text, out _).ShouldBeFalse();

    [Fact]
    public void Parse_ignores_unrecognized_lines_and_reports_them()
    {
        const string content = """
            # LessonsPlayer legenda v1
            00:00:00.000 00:00:00.500 solta
            [00:00:01.000 --> 00:00:02.000] Oi
            00:00:01.000 00:00:02.000 Oi
            isto não é uma linha válida
            """;

        var result = SubtitleTxtFormat.Parse(content);

        result.Transcript.Segments.ShouldHaveSingleItem().Text.ShouldBe("Oi");
        result.Warnings.ShouldBe(
        [
            new ParseWarning(2, ParseWarningKind.WordOutsideSegment),
            new ParseWarning(5, ParseWarningKind.UnrecognizedLine),
        ]);
    }

    [Fact]
    public void Parse_word_lines_take_precedence_over_header_text()
    {
        const string content = """
            [00:00:00.000 --> 00:00:09.000] texto antigo do cabeçalho
            00:00:01.000 00:00:02.000 novo
            """;

        var segment = SubtitleTxtFormat.Parse(content).Transcript.Segments.ShouldHaveSingleItem();

        segment.Text.ShouldBe("novo");
        segment.Start.ShouldBe(Ms(1000));
    }

    [Fact]
    public void Parse_fixes_inverted_ranges_and_sorts_segments()
    {
        const string content = """
            [00:00:05.000 --> 00:00:06.000] depois
            [00:00:01.000 --> 00:00:02.000] antes
            00:00:01.500 00:00:01.000 antes
            """;

        var result = SubtitleTxtFormat.Parse(content);

        result.Transcript.Segments.Select(s => s.Text).ShouldBe(["antes", "depois"]);
        result.Transcript.Segments[0].Words[0].End.ShouldBe(Ms(2000), "sem duração não apareceria no karaokê: vai até o fim do cabeçalho");
        result.Warnings.ShouldContain(new ParseWarning(3, ParseWarningKind.InvalidTimeRange));
        result.CorrectedTimeCount.ShouldBe(1);
        result.IgnoredLineCount.ShouldBe(0);
    }

    [Fact]
    public void Parse_gives_instant_words_the_time_until_the_next_word()
    {
        const string content = """
            [00:00:01.000 --> 00:00:03.000] um dois três
            00:00:01.000 00:00:01.400 um
            00:00:01.800 00:00:01.800 dois
            00:00:02.500 00:00:02.900 três
            """;

        var words = SubtitleTxtFormat.Parse(content).Transcript.Segments[0].Words;

        words.Select(w => (w.Start, w.End)).ShouldBe([(Ms(1000), Ms(1400)), (Ms(1800), Ms(2500)), (Ms(2500), Ms(2900))]);
    }

    [Fact]
    public void Parse_counts_ignored_lines_apart_from_corrected_times()
    {
        const string content = """
            linha solta
            [00:00:01.000 --> 00:00:02.000] oi
            00:00:01.500 00:00:01.000 oi
            texto qualquer
            [xx --> yy] quebrado
            """;

        var result = SubtitleTxtFormat.Parse(content);

        result.IgnoredLineCount.ShouldBe(3);
        result.CorrectedTimeCount.ShouldBe(1);
    }

    [Fact]
    public void Parse_word_line_with_spaces_splits_into_words()
    {
        const string content = """
            [00:00:00.000 --> 00:00:01.000] x
            00:00:00.000 00:00:01.000 duas palavras
            """;

        var words = SubtitleTxtFormat.Parse(content).Transcript.Segments[0].Words;

        words.Select(w => w.Text).ShouldBe(["duas", "palavras"]);
        words[^1].End.ShouldBe(Ms(1000));
    }

    [Fact]
    public void Parse_empty_header_without_words_is_reported_and_skipped()
    {
        var result = SubtitleTxtFormat.Parse("[00:00:01.000 --> 00:00:02.000]");

        result.Transcript.IsEmpty.ShouldBeTrue();
        result.Warnings.ShouldBe([new ParseWarning(1, ParseWarningKind.EmptySegment)]);
    }
}
