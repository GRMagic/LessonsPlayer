using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Audio;
using LessonsPlayer.Core.Transcription;
using NSubstitute;
using static LessonsPlayer.Core.Tests.TestData;

namespace LessonsPlayer.Core.Tests.Transcription;

public sealed class SubtitleGeneratorTests
{
    private readonly IModelStore _models = Substitute.For<IModelStore>();
    private readonly IAudioDecoder _decoder = Substitute.For<IAudioDecoder>();
    private readonly ITranscriber _transcriber = Substitute.For<ITranscriber>();
    private readonly SubtitleGenerator _generator;

    public SubtitleGeneratorTests()
    {
        _generator = new SubtitleGenerator(_models, _decoder, _transcriber, TimeProvider.System);
        _models.EnsureDownloadedAsync(default!, default, default).ReturnsForAnyArgs("C:/models/small.bin");
        _decoder.DecodeForSpeechAsync(default!, default)
            .ReturnsForAnyArgs(new AudioSamples(new float[AudioSamples.SpeechSampleRate * 4], AudioSamples.SpeechSampleRate));
    }

    [Fact]
    public async Task Generates_transcript_with_metadata_and_progress()
    {
        _transcriber.TranscribeAsync(default!, default!, TestContext.Current.CancellationToken).ReturnsForAnyArgs(Segments(
            new RecognizedSegment(Ms(0), Ms(1000), " Olá mundo", [], "pt"),
            new RecognizedSegment(Ms(2000), Ms(4000), " Tchau", [], "pt")));
        var reports = new List<TranscriptionProgress>();

        var transcript = await _generator.GenerateAsync(
            @"C:\aulas\aula01.mp3",
            new TranscriptionOptions(WhisperModels.Small, TranscriptionLanguages.Auto, UseGpu: false),
            new CollectingProgress(reports),
            TestContext.Current.CancellationToken);

        transcript.Segments.Select(s => s.Text).ShouldBe(["Olá mundo", "Tchau"]);
        transcript.Metadata.AudioFileName.ShouldBe("aula01.mp3");
        transcript.Metadata.Model.ShouldBe("small-q5_1");
        transcript.Metadata.Language.ShouldBe("pt");
        reports.ShouldContain(new TranscriptionProgress(TranscriptionStage.Transcribing, 0.25));
        reports[^1].ShouldBe(new TranscriptionProgress(TranscriptionStage.Transcribing, 1));
    }

    [Fact]
    public async Task Passes_model_path_and_options_to_transcriber()
    {
        _transcriber.TranscribeAsync(default!, default!, TestContext.Current.CancellationToken).ReturnsForAnyArgs(Segments());

        await _generator.GenerateAsync(
            "a.mp3", new TranscriptionOptions(WhisperModels.Small, "en", UseGpu: true), null,
            TestContext.Current.CancellationToken);

        _transcriber.Received(1).TranscribeAsync(
            Arg.Any<AudioSamples>(),
            new TranscriberSettings("C:/models/small.bin", WhisperModelSize.Small, "en", true),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task English_only_model_forces_english()
    {
        _transcriber.TranscribeAsync(default!, default!, TestContext.Current.CancellationToken).ReturnsForAnyArgs(Segments(
            new RecognizedSegment(Ms(0), Ms(1000), " Hello", [], "en")));

        var transcript = await _generator.GenerateAsync(
            "a.mp3", new TranscriptionOptions(WhisperModels.SmallEnglish, TranscriptionLanguages.Auto, UseGpu: true), null,
            TestContext.Current.CancellationToken);

        _transcriber.Received(1).TranscribeAsync(
            Arg.Any<AudioSamples>(),
            new TranscriberSettings("C:/models/small.bin", WhisperModelSize.Small, "en", true, EnglishOnly: true),
            TestContext.Current.CancellationToken);
        transcript.Metadata.Language.ShouldBe("en");
        transcript.Metadata.Model.ShouldBe("small.en-q5_1");
    }

    private static async IAsyncEnumerable<RecognizedSegment> Segments(params RecognizedSegment[] segments)
    {
        foreach (var segment in segments)
        {
            await Task.Yield();
            yield return segment;
        }
    }

    private sealed class CollectingProgress(List<TranscriptionProgress> reports) : IProgress<TranscriptionProgress>
    {
        public void Report(TranscriptionProgress value) => reports.Add(value);
    }
}
