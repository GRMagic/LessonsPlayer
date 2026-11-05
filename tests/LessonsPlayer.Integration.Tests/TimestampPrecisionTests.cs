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

    /// <summary>~80 s de fala: atravessa três janelas de 30 s do Whisper.</summary>
    private const string LongPortugueseText =
        "Bom dia a todos e sejam bem-vindos ao curso de inglês. Hoje vamos falar sobre os verbos irregulares mais comuns. " +
        "O verbo ir, em inglês, é go, e no passado se torna went. O verbo ter é have, e no passado vira had. " +
        "Vamos praticar a pronúncia com calma, sem pressa. Repita depois de mim, prestando atenção em cada som. " +
        "Agora vamos ouvir um pequeno diálogo entre dois amigos. Eles conversam sobre o que fizeram no fim de semana. " +
        "Um deles foi à praia, e o outro ficou em casa estudando para a prova. " +
        "Anote as palavras novas no seu caderno de vocabulário e revise antes de dormir. " +
        "Na próxima aula, vamos revisar tudo o que vimos hoje e fazer alguns exercícios. " +
        "Obrigado pela atenção e até a próxima aula.";

    /// <summary>~80 s de aula em inglês (o caso principal do app), com frases longas e enumerações.</summary>
    private const string LongEnglishText =
        "Welcome back, everyone. In today's lesson we are going to review the most common irregular verbs in English. " +
        "Let's start with a few examples: go, went, gone; have, had, had; and take, took, taken. " +
        "Notice that the past simple and the past participle are sometimes the same, but very often they are different. " +
        "Now, listen to this short dialogue between two friends who are talking about their weekend. " +
        "One of them went to the beach with his family, while the other stayed at home and studied for an important exam. " +
        "Pay close attention to the pronunciation of each verb, especially the ones that end with a consonant sound. " +
        "Before the next class, please write ten sentences using the verbs we practiced today, " +
        "and remember to read them out loud at least twice. Thank you for your attention, and see you next time.";

    private readonly string _workDirectory = Directory.CreateTempSubdirectory("lessonsplayer-tests-").FullName;

    /// <summary>Português e inglês, em WAV e MP3 (o formato mais comum das aulas). Modelos .en só em inglês.</summary>
    public static TheoryData<string, string, string, TestAudioFormat> Cases()
    {
        var data = new TheoryData<string, string, string, TestAudioFormat>();
        foreach (var model in Models())
        {
            foreach (var format in new[] { TestAudioFormat.Wav, TestAudioFormat.Mp3 })
            {
                if (!WhisperModels.FindOrDefault(model).EnglishOnly)
                {
                    data.Add("pt-BR", "pt", model, format);
                }

                data.Add("en-US", "en", model, format);
            }
        }

        return data;
    }

    /// <summary>Áudio longo em MP3: inglês sempre, português só para modelos multilíngues.</summary>
    public static TheoryData<string, string, string> LongCases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var model in Models())
        {
            data.Add("en-US", "en", model);
            if (!WhisperModels.FindOrDefault(model).EnglishOnly)
            {
                data.Add("pt-BR", "pt", model);
            }
        }

        return data;
    }

    public void Dispose() => Directory.Delete(_workDirectory, recursive: true);

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Word_start_times_match_spoken_audio(
        string culture, string language, string modelId, TestAudioFormat format)
    {
        var text = culture.StartsWith("pt", StringComparison.Ordinal) ? PortugueseText : EnglishText;

        var result = await MeasureAsync(text, culture, language, modelId, format);

        result.MatchRatio.ShouldBeGreaterThan(0.8, "o reconhecimento deveria acertar a maioria das palavras");
        // Medido em 2026-11: mediana ~40–80 ms e p90 ~130–250 ms (base, small, medium e large-v3-turbo).
        result.Median.ShouldBeLessThan(100, "erro mediano de início de palavra");
        result.P90.ShouldBeLessThan(250, "90% das palavras dentro de 250 ms");
    }

    [Theory]
    [MemberData(nameof(LongCases))]
    public async Task Long_audio_is_transcribed_to_the_end_with_precise_timestamps(string culture, string language, string modelId)
    {
        var text = culture.StartsWith("pt", StringComparison.Ordinal) ? LongPortugueseText : LongEnglishText;

        var result = await MeasureAsync(text, culture, language, modelId, TestAudioFormat.Mp3);

        // Antes do processamento janela a janela, o DTW parava depois dos primeiros 30 s.
        result.LastRecognizedStart.ShouldBeGreaterThan(result.LastSpokenStart - TimeSpan.FromSeconds(2), "transcreveu até o fim");
        result.MatchRatio.ShouldBeGreaterThan(0.8);
        result.Median.ShouldBeLessThan(100);
        result.P90.ShouldBeLessThan(250);
    }

    private static string[] Models() =>
        (Environment.GetEnvironmentVariable(ModelsVariable) ?? "base-q5_1,small-q5_1")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private async Task<Measurement> MeasureAsync(
        string text, string culture, string language, string modelId, TestAudioFormat format)
    {
        var ct = TestContext.Current.CancellationToken;
        var audio = SpeechAudio.TrySynthesize(text, culture, Path.Combine(_workDirectory, $"{culture}-{text.Length}"), format);
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

        var result = new Measurement(
            (double)matches.Count / audio.Words.Count,
            Percentile(errors, 0.5),
            Percentile(errors, 0.9),
            recognized.Count > 0 ? recognized[^1].Start : TimeSpan.Zero,
            audio.Words[^1].Start);

        var output = TestContext.Current.TestOutputHelper!;
        output.WriteLine($"Modelo {modelId}, {culture}, {format}: {matches.Count}/{audio.Words.Count} palavras casadas");
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Erro de início (ms): mediana {result.Median:F0}, p90 {result.P90:F0}, máx {(errors.Length > 0 ? errors[^1] : 0):F0}; " +
            $"última palavra: real {result.LastSpokenStart.TotalSeconds:F1}s, whisper {result.LastRecognizedStart.TotalSeconds:F1}s"));
        return result;
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

    private sealed record Measurement(
        double MatchRatio, double Median, double P90, TimeSpan LastRecognizedStart, TimeSpan LastSpokenStart);
}
