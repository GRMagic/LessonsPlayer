using LessonsPlayer.Core.Transcription;
using LessonsPlayer.Presentation.Resources;

namespace LessonsPlayer.Presentation.Tests;

public sealed class UiTextTests
{
    private static readonly GpuDevice Gpu = new("NVIDIA GeForce GTX 1650", 1);

    [Fact]
    public void Progress_says_where_the_transcription_runs()
    {
        UiText.Progress(new TranscriptionProgress(TranscriptionStage.Transcribing, 0.45, Gpu))
            .ShouldBe("Transcribing on the GPU (NVIDIA GeForce GTX 1650)… 45%");
        UiText.Progress(new TranscriptionProgress(TranscriptionStage.Transcribing, 0.45))
            .ShouldBe("Transcribing on the CPU… 45%");
    }

    [Fact]
    public void Finished_message_says_where_the_subtitles_were_generated()
    {
        UiText.TranscriptionFinished("lesson.mp3", TimeSpan.FromSeconds(65), 1234, Gpu)
            .ShouldBe("Subtitles for \"lesson.mp3\" generated on the GPU (NVIDIA GeForce GTX 1650) in 1:05 (1,234 words).");
        UiText.TranscriptionFinished("lesson.mp3", TimeSpan.FromSeconds(65), 1234, gpu: null)
            .ShouldBe("Subtitles for \"lesson.mp3\" generated on the CPU in 1:05 (1,234 words).");
    }
}
