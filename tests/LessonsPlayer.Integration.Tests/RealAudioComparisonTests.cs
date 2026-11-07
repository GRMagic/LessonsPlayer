using System.Diagnostics;
using System.Globalization;
using System.Text;
using LessonsPlayer.Core.Subtitles;
using LessonsPlayer.Core.Transcription;
using LessonsPlayer.Infrastructure;
using LessonsPlayer.Infrastructure.Audio;
using LessonsPlayer.Infrastructure.Whisper;
using LessonsPlayer.Integration.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace LessonsPlayer.Integration.Tests;

/// <summary>
/// Medição com áudio real (aulas e conversas do YouTube), que não fica no repositório. Só roda com
/// <c>LESSONSPLAYER_REAL_AUDIO_DIR</c> apontando para uma pasta com uma subpasta por áudio, cada uma com:
/// <list type="bullet">
/// <item><c>audio.mp3</c>;</item>
/// <item><c>reference-segments.tsv</c> (<c>start_ms, end_ms, text</c>), da legenda feita por humanos: referência de <b>texto</b>
/// e do início das frases;</item>
/// <item><c>reference-words.tsv</c> (<c>start_ms, word</c>), da legenda automática do YouTube, que tem o tempo de cada
/// palavra: referência de <b>tempo</b> (é de outro reconhecedor, então também tem erro).</item>
/// </list>
/// Escreve o relatório em <c>comparison-&lt;modelo&gt;.txt</c> na pasta raiz e a legenda gerada ao lado do áudio.
/// Modelos: <c>LESSONSPLAYER_TEST_MODELS</c> (padrão: small).
/// </summary>
[Trait("Category", "Integration")]
public sealed class RealAudioComparisonTests
{
    private const string RootVariable = "LESSONSPLAYER_REAL_AUDIO_DIR";
    private const string ModelsVariable = "LESSONSPLAYER_TEST_MODELS";

    /// <summary>Palavras reconhecidas fora das frases legendadas (ex.: trecho sem legenda humana) não contam no WER.</summary>
    private static readonly TimeSpan CoverageMargin = TimeSpan.FromSeconds(5);

    /// <summary>Pausas maiores que isso entre legendas humanas são tratadas como trecho sem legenda.</summary>
    private static readonly TimeSpan CoverageGap = TimeSpan.FromSeconds(20);

    /// <summary>Uma palavra sem palavra da legenda automática a menos disso (e fora da legenda humana) conta como inventada.</summary>
    private static readonly TimeSpan InventedWordWindow = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task Compare_with_real_audio_references()
    {
        var root = Environment.GetEnvironmentVariable(RootVariable);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            Assert.Skip($"Defina {RootVariable} com a pasta dos áudios reais.");
        }

        var modelIds = (Environment.GetEnvironmentVariable(ModelsVariable) ?? WhisperModels.Small.Id)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var folders = Directory.GetDirectories(root)
            .Where(d => File.Exists(Path.Combine(d, "audio.mp3")) && File.Exists(Path.Combine(d, "reference-segments.tsv")))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        folders.ShouldNotBeEmpty();

        foreach (var modelId in modelIds)
        {
            var report = new StringBuilder();
            foreach (var folder in folders)
            {
                report.AppendLine(await MeasureAsync(folder, WhisperModels.FindOrDefault(modelId), TestContext.Current.CancellationToken));
            }

            var reportPath = Path.Combine(root, $"comparison-{modelId}.txt");
            await File.WriteAllTextAsync(reportPath, report.ToString(), TestContext.Current.CancellationToken);
            TestContext.Current.TestOutputHelper!.WriteLine(report.ToString());
        }
    }

    private static async Task<string> MeasureAsync(string folder, WhisperModel model, CancellationToken ct)
    {
        var audioPath = Path.Combine(folder, "audio.mp3");
        var cues = ReadCues(Path.Combine(folder, "reference-segments.tsv"));
        var autoWords = ReadAutoWords(Path.Combine(folder, "reference-words.tsv"));

        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var models = new HuggingFaceModelStore(http, InfrastructureOptions.CreateDefault(), NullLogger<HuggingFaceModelStore>.Instance);
        using var transcriber = new WhisperNetTranscriber(NullLogger<WhisperNetTranscriber>.Instance);
        var generator = new SubtitleGenerator(models, new AudioFileDecoder(), TestSpeechDetector.Create(), transcriber, TimeProvider.System);

        var stopwatch = Stopwatch.StartNew();
        var transcript = await generator.GenerateAsync(audioPath, new TranscriptionOptions(model, "en", UseGpu: true), progress: null, ct);
        stopwatch.Stop();
        await File.WriteAllTextAsync(
            Path.Combine(folder, $"audio.{model.Id}.subtitle.txt"), SubtitleTxtFormat.Serialize(transcript), ct);

        var words = transcript.Segments.SelectMany(s => s.Words).ToList();
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"== {Path.GetFileName(folder)} | modelo {model.Id} | {stopwatch.Elapsed:m\\:ss} para transcrever");
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"   {words.Count} palavras em {transcript.Segments.Length} trechos; última palavra em {Format(words.Count > 0 ? words[^1].Start : TimeSpan.Zero)}" +
            $" (última legenda humana em {Format(cues.Count > 0 ? cues[^1].Start : TimeSpan.Zero)})");
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"   trechos sem pontuação final (frase cortada): {transcript.Segments.Count(s => !EndsSentence(s.Text))} de {transcript.Segments.Length}");

        AppendTextAccuracy(sb, cues, words);
        AppendInventedWords(sb, cues, autoWords, words);
        AppendWordTiming(sb, autoWords, words);
        AppendCueTiming(sb, cues, words);
        return sb.ToString();
    }

    /// <summary>WER contra a legenda humana, só nos trechos que ela cobre.</summary>
    private static void AppendTextAccuracy(StringBuilder sb, List<Cue> cues, List<Word> words)
    {
        var covered = CoveredIntervals(cues);
        var reference = cues.SelectMany(c => SplitWords(c.Text)).ToArray();
        var hypothesis = words
            .Where(w => covered.Any(i => w.Start >= i.Start && w.Start <= i.End))
            .Select(w => NormalizeWord(w.Text))
            .Where(w => w.Length > 0)
            .ToArray();

        var errors = EditDistance(reference, hypothesis);
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"   texto: WER {100.0 * errors / Math.Max(1, reference.Length):F1}% ({errors} erros em {reference.Length} palavras da legenda humana; " +
            $"{words.Count - hypothesis.Length} palavras reconhecidas fora dos trechos legendados)");
    }

    /// <summary>
    /// Palavras provavelmente inventadas: fora das frases da legenda humana e sem nenhuma palavra da legenda
    /// automática a menos de <see cref="InventedWordWindow"/>. Pega o texto inventado em trechos sem fala
    /// (créditos, música), não o erro de reconhecimento dentro da fala.
    /// </summary>
    private static void AppendInventedWords(
        StringBuilder sb, List<Cue> cues, List<(TimeSpan Start, string Text)> autoWords, List<Word> words)
    {
        var invented = words
            .Where(w => !cues.Any(c => w.Start >= c.Start && w.Start <= c.End))
            .Where(w => !autoWords.Any(a => (a.Start - w.Start).Duration() <= InventedWordWindow))
            .ToList();
        var list = string.Join(' ', invented.Select(w => $"[{Format(w.Start)}]{w.Text}"));
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"   palavras inventadas (longe de qualquer legenda): {invented.Count}{(invented.Count > 0 ? " -> " : string.Empty)}{list}");
    }

    /// <summary>Início de cada palavra contra a legenda automática do YouTube (tempo por palavra).</summary>
    private static void AppendWordTiming(StringBuilder sb, List<(TimeSpan Start, string Text)> autoWords, List<Word> words)
    {
        if (autoWords.Count == 0)
        {
            return;
        }

        var matches = WordAlignment.Match([.. autoWords.Select(w => w.Text)], [.. words.Select(w => w.Text)]);
        var signed = matches.Select(m => (words[m.Actual].Start - autoWords[m.Expected].Start).TotalMilliseconds).ToArray();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"   tempo por palavra vs. automática do YouTube ({matches.Count}/{autoWords.Count} casadas): {Stats(signed)}");
    }

    /// <summary>Início das frases contra a legenda humana (só faz sentido se ela for bem sincronizada).</summary>
    private static void AppendCueTiming(StringBuilder sb, List<Cue> cues, List<Word> words)
    {
        var referenceWords = new List<string>();
        var cueOfFirstWord = new Dictionary<int, Cue>();
        foreach (var cue in cues)
        {
            var cueWords = SplitWords(cue.Text);
            if (cueWords.Length == 0)
            {
                continue;
            }

            cueOfFirstWord[referenceWords.Count] = cue;
            referenceWords.AddRange(cueWords);
        }

        var matches = WordAlignment.Match(referenceWords, [.. words.Select(w => w.Text)]);
        var signed = matches
            .Where(m => cueOfFirstWord.ContainsKey(m.Expected))
            .Select(m => (words[m.Actual].Start - cueOfFirstWord[m.Expected].Start).TotalMilliseconds)
            .ToArray();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"   início de frase vs. legenda humana ({signed.Length}/{cueOfFirstWord.Count} frases): {Stats(signed)}");
    }

    private static string Stats(double[] signedErrors)
    {
        if (signedErrors.Length == 0)
        {
            return "sem dados";
        }

        var absolute = signedErrors.Select(Math.Abs).Order().ToArray();
        var ordered = signedErrors.Order().ToArray();
        double Within(double ms) => 100.0 * absolute.Count(e => e <= ms) / absolute.Length;
        return string.Create(CultureInfo.InvariantCulture,
            $"|erro| mediana {Percentile(absolute, 0.5):F0} ms, p90 {Percentile(absolute, 0.9):F0} ms; " +
            $"viés (mediana com sinal) {Percentile(ordered, 0.5):+0;-0} ms; " +
            $"≤100 ms {Within(100):F0}%, ≤250 ms {Within(250):F0}%, ≤500 ms {Within(500):F0}%");
    }

    private static List<(TimeSpan Start, TimeSpan End)> CoveredIntervals(List<Cue> cues)
    {
        var intervals = new List<(TimeSpan Start, TimeSpan End)>();
        foreach (var cue in cues.OrderBy(c => c.Start))
        {
            if (intervals.Count > 0 && cue.Start - intervals[^1].End <= CoverageGap)
            {
                intervals[^1] = (intervals[^1].Start, cue.End > intervals[^1].End ? cue.End : intervals[^1].End);
            }
            else
            {
                intervals.Add((cue.Start, cue.End));
            }
        }

        return [.. intervals.Select(i => (i.Start - CoverageMargin, i.End + CoverageMargin))];
    }

    private static int EditDistance(string[] a, string[] b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        var current = new int[b.Length + 1];
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(substitution, Math.Min(previous[j] + 1, current[j - 1] + 1));
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private static string[] SplitWords(string text) =>
        [.. text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(NormalizeWord).Where(w => w.Length > 0)];

    /// <summary>Minúsculas, só letras, dígitos e apóstrofo ("that's" ≠ "thats" seria injusto com as duas legendas).</summary>
    private static string NormalizeWord(string word) =>
        new string([.. word.Where(c => char.IsLetterOrDigit(c) || c is '\'' or '’').Select(c => c == '’' ? '\'' : c)])
            .ToLower(CultureInfo.InvariantCulture);

    private static bool EndsSentence(string text) =>
        text.TrimEnd().TrimEnd('"', '”', '\'', ')') is { Length: > 0 } t && t[^1] is '.' or '?' or '!' or '…';

    private static List<Cue> ReadCues(string path) =>
        [.. ReadTsv(path).Where(c => c.Length >= 3).Select(c => new Cue(Ms(c[0]), Ms(c[1]), c[2]))];

    private static List<(TimeSpan Start, string Text)> ReadAutoWords(string path) =>
        File.Exists(path) ? [.. ReadTsv(path).Where(c => c.Length >= 2).Select(c => (Ms(c[0]), c[1]))] : [];

    /// <summary>Linhas TSV, sem o cabeçalho (qualquer linha cuja primeira coluna não é número).</summary>
    private static IEnumerable<string[]> ReadTsv(string path) =>
        File.ReadLines(path)
            .Select(line => line.Split('\t'))
            .Where(c => long.TryParse(c[0], NumberStyles.None, CultureInfo.InvariantCulture, out _));

    private static TimeSpan Ms(string value) => TimeSpan.FromMilliseconds(long.Parse(value, CultureInfo.InvariantCulture));

    private static string Format(TimeSpan time) => time.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    private static double Percentile(double[] sorted, double p) =>
        sorted[(int)Math.Clamp(Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];

    private sealed record Cue(TimeSpan Start, TimeSpan End, string Text);
}
