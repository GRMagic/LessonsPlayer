using LessonsPlayer.Core.Transcription;

namespace LessonsPlayer.Core.Tests.Transcription;

public sealed class RepetitionDetectorTests
{
    [Fact]
    public void Sentence_repeated_until_the_end_of_the_window_is_repetitive()
    {
        var loop = string.Concat(Enumerable.Repeat(" >> I think it's a great opportunity to talk about space.", 14));

        RepetitionDetector.IsRepetitive(loop).ShouldBeTrue();
        RepetitionDetector.CompressionRatio(loop).ShouldBeGreaterThan(5);
    }

    [Fact]
    public void Normal_speech_is_not_repetitive()
    {
        const string speech =
            " Good morning, everyone. Today we are going to practice the present perfect tense." +
            " First, listen carefully to each sentence. Then repeat it slowly, paying attention" +
            " to the pronunciation of every single word. I have lived here for ten years.";

        RepetitionDetector.IsRepetitive(speech).ShouldBeFalse();
    }

    [Fact]
    public void Short_invented_phrase_is_not_caught_here()
    {
        RepetitionDetector.IsRepetitive(" you you Thank you.").ShouldBeFalse();
    }

    [Fact]
    public void Empty_text_has_no_compression_ratio()
    {
        RepetitionDetector.CompressionRatio(string.Empty).ShouldBe(0);
    }
}
