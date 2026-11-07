using LessonsPlayer.Infrastructure.Audio;
using LessonsPlayer.Integration.Tests.Support;
using NAudio.Wave;

namespace LessonsPlayer.Integration.Tests;

/// <summary>
/// Regressão do bug "a legenda sai de sincronia depois de clicar numa palavra": num MP3 VBR, o seek do Media
/// Foundation caía até 3 s longe da posição pedida (medido num MP3 real de 56 s). A legenda é gerada lendo o
/// arquivo do início, então depois de um seek o áudio e o karaokê se desencontravam. O teste compara o áudio lido
/// depois de cada seek com o mesmo trecho da leitura contínua.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SeekAccuracyTests : IDisposable
{
    /// <summary>Um quadro de MP3 tem 26 ms; o leitor posiciona dentro do quadro, então sobra só o erro da medição.</summary>
    private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(30);

    private readonly string _directory = Directory.CreateTempSubdirectory("lessonsplayer-seek-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Seek_lands_where_requested_in_a_variable_bitrate_mp3()
    {
        var path = VbrMp3.Create(_directory, TimeSpan.FromSeconds(10));
        var output = TestContext.Current.TestOutputHelper!;

        using var probe = AudioFiles.OpenReader(path);
        probe.DecoderName.ShouldBe(nameof(Mp3FileReader));
        var continuous = ReadAll(probe);

        LogMediaFoundationError(path, continuous, output);

        foreach (var seconds in new[] { 2.0, 6, 9, 11, 14, 17 })
        {
            using var reader = AudioFiles.OpenReader(path);
            reader.CurrentTime = TimeSpan.FromSeconds(seconds);
            var error = SeekError(reader, continuous, seconds);
            output.WriteLine($"seek {seconds:F1} s: erro {error.TotalMilliseconds:+0;-0} ms");
            error.Duration().ShouldBeLessThanOrEqualTo(Tolerance, $"seek para {seconds} s");
        }
    }

    /// <summary>Só documenta o problema original; não falha se o Media Foundation mudar.</summary>
    private static void LogMediaFoundationError(string path, float[] continuous, ITestOutputHelper output)
    {
        using var mediaFoundation = new AudioFileReader(path);
        mediaFoundation.CurrentTime = TimeSpan.FromSeconds(14);
        var chunk = Read(mediaFoundation, VbrMp3.SampleRate / 4);
        var lag = BestLag(continuous, chunk, 14 * VbrMp3.SampleRate, 4 * VbrMp3.SampleRate);
        output.WriteLine($"Media Foundation (antes da correção), seek 14 s: erro {lag * 1000.0 / VbrMp3.SampleRate:+0;-0} ms");
    }

    private static TimeSpan SeekError(ISampleProvider reader, float[] continuous, double seconds)
    {
        var chunk = Read(reader, VbrMp3.SampleRate / 4);
        var lag = BestLag(continuous, chunk, (int)(seconds * VbrMp3.SampleRate), 4 * VbrMp3.SampleRate);
        return TimeSpan.FromSeconds((double)lag / VbrMp3.SampleRate);
    }

    private static float[] ReadAll(ISampleProvider reader) => Read(reader, int.MaxValue);

    private static float[] Read(ISampleProvider reader, int maxSamples)
    {
        var samples = new List<float>();
        var buffer = new float[4096];
        int read;
        while (samples.Count < maxSamples && (read = reader.Read(buffer.AsSpan(0, Math.Min(buffer.Length, maxSamples - samples.Count)))) > 0)
        {
            samples.AddRange(buffer.AsSpan(0, read));
        }

        return [.. samples];
    }

    /// <summary>
    /// Deslocamento (em amostras) com maior correlação normalizada: busca grossa e depois fina. O sinal de teste
    /// é ruído em passa-baixa, cuja correlação se mantém por dezenas de amostras, então o passo grosso não o perde.
    /// </summary>
    private static int BestLag(float[] reference, float[] chunk, int expected, int maxLag)
    {
        var coarse = Search(reference, chunk, expected, -maxLag, maxLag, step: 8);
        return Search(reference, chunk, expected, coarse - 16, coarse + 16, step: 1);
    }

    /// <summary>Correlação normalizada (1 = idêntico) entre o trecho e a referência a partir de <paramref name="start"/>.</summary>
    private static double Score(float[] reference, float[] chunk, int start)
    {
        double dot = 0, a = 0, b = 0;
        for (var i = 0; i < chunk.Length && start + i < reference.Length; i += 2)
        {
            dot += chunk[i] * reference[start + i];
            a += chunk[i] * chunk[i];
            b += reference[start + i] * reference[start + i];
        }

        return a > 0 && b > 0 ? dot / Math.Sqrt(a * b) : 0;
    }

    private static int Search(float[] reference, float[] chunk, int expected, int from, int to, int step)
    {
        var best = from;
        var bestScore = double.NegativeInfinity;
        for (var lag = from; lag <= to; lag += step)
        {
            var start = expected + lag;
            if (start < 0 || start + chunk.Length > reference.Length)
            {
                continue;
            }

            var score = Score(reference, chunk, start);
            if (score > bestScore)
            {
                bestScore = score;
                best = lag;
            }
        }

        return best;
    }
}
