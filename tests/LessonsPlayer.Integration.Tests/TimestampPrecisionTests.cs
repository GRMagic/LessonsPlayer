using System.Globalization;
using LessonsPlayer.Core.Subtitles;
using LessonsPlayer.Core.Transcription;
using LessonsPlayer.Infrastructure;
using LessonsPlayer.Infrastructure.Audio;
using LessonsPlayer.Infrastructure.Whisper;
using LessonsPlayer.Integration.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace LessonsPlayer.Integration.Tests;

/// <summary>
/// Mede a precisão dos timestamps por palavra comparando a transcrição com o tempo real de cada
/// palavra de um áudio sintetizado (TTS do Windows). Baixa os modelos na primeira execução.
/// <para>
/// Modelos testados: variável de ambiente <c>LESSONSPLAYER_TEST_MODELS</c> (ids separados por vírgula,
/// ex.: <c>small-q5_1,large-v3-turbo-q5_0</c>); padrão <c>base-q5_1,small-q5_1</c>.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public sealed class TimestampPrecisionTests : IDisposable
{
    private const string ModelsVariable = "LESSONSPLAYER_TEST_MODELS";

    private const string PortugueseText =
        "Olá, pessoal. Hoje vamos estudar a diferença entre os tempos verbais do inglês. " +
        "Primeiro, vamos ouvir alguns exemplos simples. Depois, cada um vai repetir as frases em voz alta. " +
        "Prestem atenção na pronúncia de cada palavra, principalmente nas palavras mais longas.";

    private const string EnglishText =
        "Good morning, everyone. Today we are going to practice the present perfect tense. " +
        "First, listen carefully to each sentence. Then repeat it slowly, paying attention " +
        "to the pronunciation of every single word.";

    private readonly string _workDirectory = Directory.CreateTempSubdirectory("lessonsplayer-tests-").FullName;

    public static TheoryData<string, string, string> Cases()
    {
        var models = (Environment.GetEnvironmentVariable(ModelsVariable) ?? "base-q5_1,small-q5_1")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var data = new TheoryData<string, string, string>();
        foreach (var model in models)
        {
            data.Add("pt-BR", "pt", model);
            data.Add("en-US", "en", model);
        }

        return data;
    }

    public void Dispose() => Directory.Delete(_workDirectory, recursive: true);

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Word_start_times_match_spoken_audio(string culture, string language, string modelId)
    {
        var ct = TestContext.Current.CancellationToken;
        var text = culture.StartsWith("pt", StringComparison.Ordinal) ? PortugueseText : EnglishText;
        var audio = SpeechAudio.TrySynthesize(text, culture, Path.Combine(_workDirectory, $"{culture}.wav"));
        if (audio is null)
        {
            Assert.Skip($"Nenhuma voz TTS instalada para {culture}.");
        }

        var transcript = await TranscribeAsync(audio.FilePath, WhisperModels.FindOrDefault(modelId), language, ct);

        var recognized = transcript.Segments.SelectMany(s => s.Words).ToList();
        var matches = WordAlignment.Match([.. audio.Words.Select(w => w.Text)], [.. recognized.Select(w => w.Text)]);
        var errors = matches
            .Select(m => Math.Abs((recognized[m.Actual].Start - audio.Words[m.Expected].Start).TotalMilliseconds))
            .Order()
            .ToArray();

        var output = TestContext.Current.TestOutputHelper!;
        output.WriteLine($"Modelo {modelId}, {culture}: {matches.Count}/{audio.Words.Count} palavras casadas");
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Erro de início (ms): mediana {Percentile(errors, 0.5):F0}, p90 {Percentile(errors, 0.9):F0}, máx {errors[^1]:F0}"));
        foreach (var (expected, actual) in matches)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {audio.Words[expected].Text,-14} real {audio.Words[expected].Start.TotalSeconds,7:F2}s  " +
                $"whisper {recognized[actual].Start.TotalSeconds,7:F2}s"));
        }

        ((double)matches.Count / audio.Words.Count).ShouldBeGreaterThan(0.8, "o reconhecimento deveria acertar a maioria das palavras");
        // Medido em 2026-11: mediana ~40–50 ms e p90 ~130–150 ms (base e small, pt e en).
        Percentile(errors, 0.5).ShouldBeLessThan(100, "erro mediano de início de palavra");
        Percentile(errors, 0.9).ShouldBeLessThan(250, "90% das palavras dentro de 250 ms");
    }

    private static async Task<Transcript> TranscribeAsync(
        string audioPath, WhisperModel model, string language, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var models = new HuggingFaceModelStore(
            http, InfrastructureOptions.CreateDefault(), NullLogger<HuggingFaceModelStore>.Instance);
        using var transcriber = new WhisperNetTranscriber(NullLogger<WhisperNetTranscriber>.Instance);
        var generator = new SubtitleGenerator(models, new AudioFileDecoder(), transcriber, TimeProvider.System);

        return await generator.GenerateAsync(
            audioPath, new TranscriptionOptions(model, language, UseGpu: true), progress: null, ct);
    }

    private static double Percentile(double[] sorted, double p) =>
        sorted.Length == 0 ? double.NaN : sorted[(int)Math.Clamp(Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];
}
