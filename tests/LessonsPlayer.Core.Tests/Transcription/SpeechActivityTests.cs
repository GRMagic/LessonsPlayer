using LessonsPlayer.Core.Transcription;
using static LessonsPlayer.Core.Tests.TestData;

namespace LessonsPlayer.Core.Tests.Transcription;

public sealed class SpeechActivityTests
{
    private static readonly TimeSpan Margin = Ms(1000);

    /// <summary>Fala em 10–20 s e 40–50 s.</summary>
    private static readonly SpeechActivity Speech = new([R(40_000, 50_000), R(10_000, 20_000)]);

    [Fact]
    public void Sorts_and_merges_overlapping_regions_and_drops_empty_ones()
    {
        var speech = new SpeechActivity([R(5000, 6000), R(1000, 3000), R(2500, 4000), R(4000, 4500), R(7000, 7000)]);

        speech.Regions.ShouldBe([R(1000, 4500), R(5000, 6000)]);
        speech.TotalSpeech.ShouldBe(Ms(4500));
        speech.HasSpeech.ShouldBeTrue();
    }

    [Fact]
    public void No_regions_means_no_speech()
    {
        new SpeechActivity([]).HasSpeech.ShouldBeFalse();
    }

    [Theory]
    [InlineData(12_000, true)]  // dentro da fala
    [InlineData(9_200, true)]   // dentro da margem antes da fala
    [InlineData(20_800, true)]  // dentro da margem depois da fala
    [InlineData(8_500, false)]  // antes da margem
    [InlineData(30_000, false)] // entre as falas, longe das duas
    [InlineData(51_500, false)] // depois da última fala
    public void Detects_instants_near_speech(int timeMs, bool expected)
    {
        Speech.IsNearSpeech(Ms(timeMs), Margin).ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, 9_000)]       // antes da fala: pula para o início dela menos a margem
    [InlineData(9_500, 9_500)]   // já dentro da margem: fica onde está
    [InlineData(15_000, 15_000)] // dentro da fala
    [InlineData(20_800, 20_800)] // dentro da margem depois da fala
    [InlineData(25_000, 39_000)] // entre as falas: pula para a próxima
    public void Next_speech_skips_only_the_part_without_speech(int positionMs, int expectedMs)
    {
        Speech.NextSpeechFrom(Ms(positionMs), Margin).ShouldBe(Ms(expectedMs));
    }

    [Fact]
    public void Next_speech_is_null_after_the_last_region()
    {
        Speech.NextSpeechFrom(Ms(51_500), Margin).ShouldBeNull();
    }

    [Fact]
    public void End_of_speech_joins_regions_separated_by_short_pauses()
    {
        var speech = new SpeechActivity([R(1000, 3000), R(3500, 6000), R(9000, 12_000), R(12_500, 14_000)]);

        speech.EndOfSpeechFrom(Ms(0), Ms(2000), Ms(60_000)).ShouldBe(Ms(6000));       // pausa de 3 s depois de 6 s
        speech.EndOfSpeechFrom(Ms(7000), Ms(2000), Ms(60_000)).ShouldBe(Ms(14_000));  // começa na próxima fala
        speech.EndOfSpeechFrom(Ms(0), Ms(5000), Ms(60_000)).ShouldBe(Ms(14_000));     // pausas toleradas
        speech.EndOfSpeechFrom(Ms(0), Ms(5000), Ms(10_000)).ShouldBe(Ms(10_000));     // limitado
        speech.EndOfSpeechFrom(Ms(15_000), Ms(2000), Ms(60_000)).ShouldBeNull();
    }

    [Theory]
    [InlineData(0, 60_000, 20_000)]      // as duas falas inteiras
    [InlineData(15_000, 45_000, 10_000)] // metade de cada
    [InlineData(21_000, 39_000, 0)]      // entre as falas
    [InlineData(45_000, 45_000, 0)]      // intervalo vazio
    public void Measures_speech_between_two_instants(int startMs, int endMs, int expectedMs)
    {
        Speech.SpeechBetween(Ms(startMs), Ms(endMs)).ShouldBe(Ms(expectedMs));
    }

    private static SpeechRegion R(int startMs, int endMs) => new(Ms(startMs), Ms(endMs));
}
