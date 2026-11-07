using System.Globalization;
using LessonsPlayer.Core.Subtitles;
using LessonsPlayer.Core.Transcription;
using LessonsPlayer.Infrastructure;
using LessonsPlayer.Infrastructure.Audio;
using LessonsPlayer.Infrastructure.Whisper;
using LessonsPlayer.Integration.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LessonsPlayer.Integration.Tests;

/// <summary>
/// Aula TTS com 25 s sem fala (música ou ruído) no meio e no fim, como uma vinheta e os créditos. O Whisper não
/// pode inventar texto nesses trechos, e a fala depois deles precisa sair inteira e no tempo certo.
/// Medido antes do detector de voz (base, small, small.en e large-v3-turbo, MP3): 6 de 8 casos falhavam. O base
/// inventava 5 palavras na música e 7 no ruído (<c>"while the rest is in the form of a "double" sentence."</c>),
/// e a primeira palavra depois do trecho sem fala (<c>"Welcome"</c>) saía até 27 s antes de ser dita (small e
/// large). Depois: nenhuma palavra inventada e <c>"Welcome"</c> a 30 ms do tempo real nos 8 casos. Um
/// <c>"Thank you."</c> colado no fim da fala (a menos de <see cref="EdgeTolerance"/>) ainda passa: o filtro não
/// tem como separá-lo da última palavra dita. Modelos: <c>LESSONSPLAYER_TEST_MODELS</c>, como em
/// <see cref="TimestampPrecisionTests"/>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NonSpeechHallucinationTests : IDisposable
{
    private const string ModelsVariable = "LESSONSPLAYER_TEST_MODELS";

    private const string FirstPart =
        "Good morning, everyone. Today we are going to practice the present perfect tense. " +
        "First, listen carefully to each sentence. Then repeat it slowly, paying attention " +
        "to the pronunciation of every single word.";

    private const string SecondPart =
        "Welcome back. Now let's look at a few more examples. I have lived here for ten years. " +
        "She has finished her homework. We have never seen that movie before. " +
        "Thank you for your attention, and see you next time.";

    /// <summary>Termina no meio da frase, como um trecho recortado de um vídeo.</summary>
    private const string CutEnding =
        "Hello everybody. In this video I want to talk about a brand new feature, which I think many people " +
        "have been sleeping on. It can really lead to safer code. So let me show you what I have here. " +
        "So, a simple console application, and";

    private static readonly TimeSpan NonSpeechDuration = TimeSpan.FromSeconds(25);

    /// <summary>Palavras a menos disso das bordas do trecho sem fala não contam (erro normal do alinhamento).</summary>
    private static readonly TimeSpan EdgeTolerance = TimeSpan.FromSeconds(1);

    private readonly string _workDirectory = Directory.CreateTempSubdirectory("lessonsplayer-tests-").FullName;

    public static TheoryData<string, NonSpeechKind> Cases()
    {
        var data = new TheoryData<string, NonSpeechKind>();
        foreach (var model in (Environment.GetEnvironmentVariable(ModelsVariable) ?? "base-q5_1,small-q5_1")
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            data.Add(model, NonSpeechKind.Music);
            data.Add(model, NonSpeechKind.Noise);
        }

        return data;
    }

    public void Dispose() => Directory.Delete(_workDirectory, recursive: true);

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Does_not_invent_text_where_there_is_no_speech(string modelId, NonSpeechKind kind)
    {
        var ct = TestContext.Current.CancellationToken;
        var audio = MixedAudio.TryCreate(
            "en-US",
            Path.Combine(_workDirectory, $"{kind}"),
            TestAudioFormat.Mp3,
            new SpeechPart(FirstPart),
            new NonSpeechPart(kind, NonSpeechDuration),
            new SpeechPart(SecondPart),
            new NonSpeechPart(kind, NonSpeechDuration));
        if (audio is null)
        {
            Assert.Skip("Nenhuma voz TTS instalada para en-US.");
        }

        var transcript = await TranscribeAsync(audio.FilePath, WhisperModels.FindOrDefault(modelId), ct);

        var words = transcript.Segments.SelectMany(s => s.Words).ToList();
        var invented = words
            .Where(w => audio.NonSpeech.Any(g => w.Start > g.Start + EdgeTolerance && w.Start < g.End - EdgeTolerance))
            .ToList();
        var matches = WordAlignment.Match([.. audio.Words.Select(w => w.Text)], [.. words.Select(w => w.Text)]);
        var errors = matches
            .Select(m => Math.Abs((words[m.Actual].Start - audio.Words[m.Expected].Start).TotalMilliseconds))
            .Order()
            .ToArray();

        // A primeira palavra depois do trecho sem fala é a que mais sofre: o Whisper a punha no começo da fatia,
        // segundos antes da fala, esticada por cima da música.
        var firstAfterGap = audio.Words.Select((w, i) => (Word: w, Index: i))
            .First(x => x.Word.Start > audio.NonSpeech[0].End);
        var matchedFirst = matches.Where(m => m.Expected == firstAfterGap.Index).Select(m => words[m.Actual]).FirstOrDefault();

        var output = TestContext.Current.TestOutputHelper!;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Modelo {modelId}, {kind}: {invented.Count} palavras inventadas, {matches.Count}/{audio.Words.Count} casadas, " +
            $"erro de início mediana {Percentile(errors, 0.5):F0} ms, p90 {Percentile(errors, 0.9):F0} ms; " +
            $"'{firstAfterGap.Word.Text}' (depois do trecho sem fala): real {firstAfterGap.Word.Start.TotalSeconds:F2} s, " +
            $"reconhecida {matchedFirst?.Start.TotalSeconds:F2} s"));
        foreach (var segment in transcript.Segments)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  [{segment.Start.TotalSeconds,6:F2} -> {segment.End.TotalSeconds,6:F2}] {segment.Text}"));
        }

        invented.Select(w => $"{w.Start.TotalSeconds:F1}s {w.Text}").ShouldBeEmpty("texto inventado no trecho sem fala");
        ((double)matches.Count / audio.Words.Count).ShouldBeGreaterThan(0.9, "a fala antes e depois do trecho sem fala");
        Percentile(errors, 0.5).ShouldBeLessThan(100, "erro mediano de início de palavra");
        matchedFirst.ShouldNotBeNull("a primeira palavra depois do trecho sem fala");
        (matchedFirst.Start - firstAfterGap.Word.Start).Duration().ShouldBeLessThan(
            TimeSpan.FromMilliseconds(500), "a primeira palavra depois do trecho sem fala sai no tempo certo");
    }

    public static TheoryData<string> Models() =>
        [.. (Environment.GetEnvironmentVariable(ModelsVariable) ?? "base-q5_1,small-q5_1")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>
    /// Fala cortada no fim e seguida de silêncio digital, como no áudio de teste do usuário. O VAD estende a fala um
    /// pouco além do último trecho do Whisper, e essa sobra era decodificada numa fatia própria, de 1 s, que a fatia
    /// anterior já tinha ouvido inteira. Nela o modelo completava a frase: small escreveu
    /// <c>"I'm going to show you how to do that."</c> depois da última palavra dita, todas as palavras no mesmo
    /// instante. Medido em 4 áudios reais com 3 modelos: 8 fatias assim, nenhuma palavra real; em 5 delas, texto
    /// inventado (<c>"you"</c>, <c>"Bye."</c>, <c>"Thank you."</c>), nas outras só marcações.
    /// </summary>
    [Theory]
    [MemberData(nameof(Models))]
    public async Task Does_not_decode_again_speech_the_previous_slice_already_heard(string modelId)
    {
        var ct = TestContext.Current.CancellationToken;
        var audio = MixedAudio.TryCreate(
            "en-US",
            Path.Combine(_workDirectory, "cut"),
            TestAudioFormat.Mp3,
            new SpeechPart(CutEnding),
            new NonSpeechPart(NonSpeechKind.Silence, TimeSpan.FromSeconds(6)));
        if (audio is null)
        {
            Assert.Skip("Nenhuma voz TTS instalada para en-US.");
        }

        var slices = new SliceLog();
        var transcript = await TranscribeAsync(audio.FilePath, WhisperModels.FindOrDefault(modelId), ct, slices);
        var samples = await new AudioFileDecoder().DecodeForSpeechAsync(audio.FilePath, ct);
        var speech = (await TestSpeechDetector.Create().DetectAsync(samples, ct)).ShouldNotBeNull();

        var output = TestContext.Current.TestOutputHelper!;
        foreach (var slice in slices.Slices)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"fatia {slice.From.TotalSeconds:F2} -> {slice.To.TotalSeconds:F2} " +
                $"({speech.SpeechBetween(slice.From, slice.To).TotalSeconds:F2} s de fala): {slice.Text}"));
        }

        var alreadyHeard = slices.Slices.Zip(slices.Slices.Skip(1))
            .Where(p => p.Second.From < p.First.To && speech.SpeechBetween(p.First.To, p.Second.To) == TimeSpan.Zero)
            .Select(p => $"{p.Second.From.TotalSeconds:F2}s: {p.Second.Text}");
        alreadyHeard.ShouldBeEmpty("fatia só com fala que a fatia anterior já tinha ouvido");

        var speechEnd = audio.NonSpeech[^1].Start;
        transcript.Segments.SelectMany(s => s.Words)
            .Where(w => w.Start > speechEnd + EdgeTolerance)
            .Select(w => $"{w.Start.TotalSeconds:F2}s {w.Text}")
            .ShouldBeEmpty("texto depois do fim da fala");
    }

    private static async Task<Transcript> TranscribeAsync(
        string audioPath, WhisperModel model, CancellationToken ct, ILogger<WhisperNetTranscriber>? transcriberLog = null)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var models = new HuggingFaceModelStore(
            http, InfrastructureOptions.CreateDefault(), NullLogger<HuggingFaceModelStore>.Instance);
        using var transcriber = new WhisperNetTranscriber(transcriberLog ?? NullLogger<WhisperNetTranscriber>.Instance);
        var generator = new SubtitleGenerator(
            models, new AudioFileDecoder(), TestSpeechDetector.Create(), transcriber, TimeProvider.System);

        return await generator.GenerateAsync(
            audioPath, new TranscriptionOptions(model, "en", UseGpu: true), progress: null, ct);
    }

    private static double Percentile(double[] sorted, double p) =>
        sorted.Length == 0 ? double.NaN : sorted[(int)Math.Clamp(Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];
}
