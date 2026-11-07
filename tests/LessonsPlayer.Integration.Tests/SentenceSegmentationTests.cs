using System.Globalization;
using System.Runtime.CompilerServices;
using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Audio;
using LessonsPlayer.Core.Subtitles;
using LessonsPlayer.Core.Transcription;
using LessonsPlayer.Infrastructure;
using LessonsPlayer.Infrastructure.Audio;
using LessonsPlayer.Infrastructure.Whisper;
using LessonsPlayer.Integration.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace LessonsPlayer.Integration.Tests;

/// <summary>
/// Verifica que cada frase de uma aula longa sai num trecho só. O Whisper corta frases longas, com vírgulas
/// e enumerações, no meio (medido com o small: as 15 frases desta aula saíam em dois trechos); o
/// <see cref="SentenceRegrouper"/> junta os pedaços. Modelos: <c>LESSONSPLAYER_TEST_MODELS</c>, como em
/// <see cref="TimestampPrecisionTests"/>.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Requires", "WhisperModel")] // baixa modelos e transcreve: fora do CI (ver .github/workflows/ci.yml)
public sealed class SentenceSegmentationTests : IDisposable
{
    private const string ModelsVariable = "LESSONSPLAYER_TEST_MODELS";

    /// <summary>~2,5 min de aula em inglês: 15 frases longas, várias com enumerações.</summary>
    private const string EnglishLessonText =
        "Good morning, everyone, and welcome back to the third lesson of our intermediate English course, where we are going to work on the past simple and the present perfect. " +
        "Before we begin, make sure you have your notebook, a pencil, the vocabulary list from last week, and the audio files that I shared with you on Monday. " +
        "In the first part of the lesson, we will review the regular verbs, the irregular verbs, and the most common time expressions, such as yesterday, last week, two days ago, and last year. " +
        "For example, we say I went to the beach last summer, but we say I have been to the beach many times, because the second sentence does not mention a specific time. " +
        "Remember that the present perfect is formed with have or has, followed by the past participle of the main verb, like eaten, written, spoken, taken, and given. " +
        "Now, listen carefully to the following sentences, repeat each one out loud, and pay attention to the stress, the rhythm, and the intonation of the speaker. " +
        "She has lived in Canada for ten years, he has worked at the same company since he finished college, and they have never traveled outside the country. " +
        "In the second part of the lesson, we are going to listen to a conversation between two old friends, Anna and Michael, who meet at a coffee shop after a long time. " +
        "Anna tells Michael that she has just finished her degree, that she has found a new job in the city, and that she is going to move into a new apartment next month. " +
        "Michael says that he has been very busy at work, that he has started taking guitar lessons, and that he has not had a real vacation for almost two years. " +
        "While you listen, write down every verb in the past simple and every verb in the present perfect, and try to explain why the speaker chose each tense. " +
        "For homework, please complete exercises one, two, and three on page forty five, write a short paragraph about your last vacation, and record yourself reading it aloud. " +
        "If you have any questions about the exercises, the grammar, or the pronunciation, send me a message in the class group, and I will answer as soon as I can. " +
        "In our next lesson, we will check the homework together, practice some listening exercises with recordings of native speakers, and start talking about the past continuous. " +
        "Thank you very much for your attention, have a great week, and see you next time.";

    private readonly string _workDirectory = Directory.CreateTempSubdirectory("lessonsplayer-tests-").FullName;

    public static TheoryData<string> ModelCases() =>
        [.. (Environment.GetEnvironmentVariable(ModelsVariable) ?? "base-q5_1,small-q5_1")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    public void Dispose() => Directory.Delete(_workDirectory, recursive: true);

    [Theory]
    [MemberData(nameof(ModelCases))]
    public async Task Long_english_lesson_keeps_each_sentence_in_one_segment(string modelId)
    {
        var ct = TestContext.Current.CancellationToken;
        var audio = SpeechAudio.TrySynthesize(
            EnglishLessonText, "en-US", Path.Combine(_workDirectory, "lesson"), TestAudioFormat.Mp3);
        if (audio is null)
        {
            Assert.Skip("Nenhuma voz TTS instalada para en-US.");
        }

        using var whisper = new WhisperNetTranscriber(NullLogger<WhisperNetTranscriber>.Instance);
        var recorder = new RecordingTranscriber(whisper);
        var transcript = await TranscribeAsync(audio.FilePath, WhisperModels.FindOrDefault(modelId), recorder, ct);

        // Antes: os trechos como o Whisper entregou. Depois: a legenda montada pelo SubtitleGenerator.
        var raw = recorder.Segments.Select(TokenWordMerger.ToSegment).OfType<Segment>().ToList();
        var output = TestContext.Current.TestOutputHelper!;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Modelo {modelId}: {audio.Words[^1].Start.TotalSeconds:F0} s de fala; Whisper: {raw.Count} trechos, " +
            $"{CutSentences(raw)} frases cortadas; legenda: {transcript.Segments.Length} trechos, " +
            $"{CutSentences(transcript.Segments)} frases cortadas"));
        foreach (var segment in transcript.Segments)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  [{segment.Start.TotalSeconds,6:F2} -> {segment.End.TotalSeconds,6:F2}] {segment.Text}"));
        }

        CutSentences(transcript.Segments).ShouldBe(0, "nenhum trecho deveria terminar no meio de uma frase");
        transcript.Segments[^1].Words[^1].Start.ShouldBeGreaterThan(
            audio.Words[^1].Start - TimeSpan.FromSeconds(2), "transcreveu até o fim");
    }

    private static async Task<Transcript> TranscribeAsync(
        string audioPath, WhisperModel model, ITranscriber transcriber, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var models = new HuggingFaceModelStore(
            http, InfrastructureOptions.CreateDefault(), NullLogger<HuggingFaceModelStore>.Instance);
        var generator = new SubtitleGenerator(models, new AudioFileDecoder(), TestSpeechDetector.Create(), transcriber, TimeProvider.System);

        return await generator.GenerateAsync(
            audioPath, new TranscriptionOptions(model, "en", await TestGpu.FindAsync()), progress: null, ct);
    }

    /// <summary>Trechos (fora o último) que não terminam em pontuação final.</summary>
    private static int CutSentences(IReadOnlyList<Segment> segments) =>
        segments.Take(segments.Count - 1).Count(s => !EndsSentence(s.Text));

    private static bool EndsSentence(string text)
    {
        var trimmed = text.TrimEnd().TrimEnd('"', '\'', '”', '’', ')', ']');
        return trimmed.Length > 0 && trimmed[^1] is '.' or '!' or '?' or '…';
    }

    /// <summary>Repassa ao transcritor real e guarda os trechos como o Whisper os entregou.</summary>
    private sealed class RecordingTranscriber(ITranscriber inner) : ITranscriber
    {
        public List<RecognizedSegment> Segments { get; } = [];

        public Task<GpuDevice?> LoadModelAsync(TranscriberSettings settings, CancellationToken cancellationToken) =>
            inner.LoadModelAsync(settings, cancellationToken);

        public async IAsyncEnumerable<RecognizedSegment> TranscribeAsync(
            AudioSamples audio,
            TranscriberSettings settings,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var segment in inner.TranscribeAsync(audio, settings, cancellationToken))
            {
                Segments.Add(segment);
                yield return segment;
            }
        }
    }
}
