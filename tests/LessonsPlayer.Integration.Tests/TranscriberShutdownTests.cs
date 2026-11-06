using System.Diagnostics;
using LessonsPlayer.Core.Transcription;
using LessonsPlayer.Infrastructure;
using LessonsPlayer.Infrastructure.Audio;
using LessonsPlayer.Infrastructure.Whisper;
using LessonsPlayer.Integration.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace LessonsPlayer.Integration.Tests;

/// <summary>
/// Fechar o app no meio de uma geração: o host descarta o transcritor enquanto o whisper.cpp ainda
/// calcula na CPU. Descartar o modelo nativo nesse momento derrubava o processo (0xC0000005 em
/// ggml-cpu-whisper.dll).
/// </summary>
[Trait("Category", "Integration")]
public sealed class TranscriberShutdownTests : IDisposable
{
    private const string Text =
        "Today we are going to review the present perfect tense. We use it to talk about experiences, " +
        "about actions that started in the past and continue now, and about recent events with a result in the present. " +
        "For example, I have lived here for ten years, she has already finished her homework, and we have never been to Japan. " +
        "Remember that the present perfect is formed with have or has and the past participle of the main verb. " +
        "Irregular verbs have their own past participles, so you need to memorize them, like gone, seen, eaten and written.";

    private readonly string _workDirectory = Directory.CreateTempSubdirectory("lessonsplayer-shutdown-").FullName;

    public void Dispose() => Directory.Delete(_workDirectory, recursive: true);

    /// <summary>
    /// Medido antes da correção: com o large-v3-turbo o processo caía (0xC0000005). Depois: o descarte espera
    /// o whisper.cpp parar, o que leva de ~50 ms (small) a ~20 s (large na CPU, que só para entre etapas).
    /// </summary>
    [Theory]
    [InlineData("small-q5_1", true)]
    [InlineData("large-v3-turbo-q5_0", false)] // o próprio Dispose cancela
    public async Task Disposing_during_cpu_transcription_does_not_crash(string modelId, bool cancelFirst)
    {
        var output = TestContext.Current.TestOutputHelper!;
        var ct = TestContext.Current.CancellationToken;
        var audio = SpeechAudio.TrySynthesize(Text + " " + Text, "en-US", Path.Combine(_workDirectory, "lesson"), TestAudioFormat.Mp3)
            ?? throw new InvalidOperationException("Sem voz en-US");

        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var models = new HuggingFaceModelStore(http, InfrastructureOptions.CreateDefault(), NullLogger<HuggingFaceModelStore>.Instance);
        var transcriber = new WhisperNetTranscriber(NullLogger<WhisperNetTranscriber>.Instance);
        var generator = new SubtitleGenerator(models, new AudioFileDecoder(), TestSpeechDetector.Create(), transcriber, TimeProvider.System);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var progressed = new TaskCompletionSource();
        var generation = Task.Run(() => generator.GenerateAsync(
            audio.FilePath,
            new TranscriptionOptions(WhisperModels.FindOrDefault(modelId), "en", UseGpu: false),
            new SynchronousProgress(p => { if (p.Stage == TranscriptionStage.Transcribing) progressed.TrySetResult(); }),
            cts.Token), ct);

        await progressed.Task.WaitAsync(TimeSpan.FromMinutes(2), ct);
        await Task.Delay(1500, ct); // whisper_full rodando na CPU

        // O que o app faz ao fechar: cancela e o host descarta o transcritor logo em seguida.
        var stopwatch = Stopwatch.StartNew();
        if (cancelFirst)
        {
            await cts.CancelAsync();
        }

        transcriber.Dispose();
        output.WriteLine($"{modelId} cancel={cancelFirst}: Dispose levou {stopwatch.ElapsedMilliseconds} ms");

        var failure = await Record.ExceptionAsync(() => generation);
        output.WriteLine($"Geração terminou {stopwatch.ElapsedMilliseconds} ms depois, com {failure?.GetType().Name ?? "sucesso"}: {failure?.Message}");
        failure.ShouldBeAssignableTo<OperationCanceledException>();
    }

    private sealed class SynchronousProgress(Action<TranscriptionProgress> handler) : IProgress<TranscriptionProgress>
    {
        public void Report(TranscriptionProgress value) => handler(value);
    }
}
