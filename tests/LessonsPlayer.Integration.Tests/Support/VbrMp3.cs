using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LessonsPlayer.Integration.Tests.Support;

/// <summary>
/// MP3 de bitrate variável sem depender de ferramentas externas: dois MP3 CBR (32 e 192 kbps) do codificador do
/// Windows, emendados quadro a quadro. É o caso que o Media Foundation posiciona errado (ele supõe bitrate constante).
/// O sinal é ruído com envelope variável, sem periodicidade, para a correlação achar um único alinhamento.
/// </summary>
internal static class VbrMp3
{
    public const int SampleRate = 44_100;

    /// <summary>Cria o arquivo e retorna o caminho. Cada metade tem <paramref name="halfDuration"/>.</summary>
    public static string Create(string directory, TimeSpan halfDuration)
    {
        var low = Path.Combine(directory, "low.mp3");
        var high = Path.Combine(directory, "high.mp3");
        MediaFoundationEncoder.EncodeToMp3(new SampleToWaveProvider16(new EnvelopedNoise(seed: 1).Take(halfDuration)), low, 32_000);
        MediaFoundationEncoder.EncodeToMp3(new SampleToWaveProvider16(new EnvelopedNoise(seed: 2).Take(halfDuration)), high, 192_000);

        var path = Path.Combine(directory, "vbr.mp3");
        File.WriteAllBytes(path, [.. File.ReadAllBytes(low), .. WithoutId3(File.ReadAllBytes(high))]);
        return path;
    }

    /// <summary>Tira o cabeçalho ID3v2 (se houver), para a segunda metade começar direto nos quadros.</summary>
    private static byte[] WithoutId3(byte[] data)
    {
        if (data.Length < 10 || data[0] != 'I' || data[1] != 'D' || data[2] != '3')
        {
            return data;
        }

        var size = (data[6] << 21) | (data[7] << 14) | (data[8] << 7) | data[9];
        return data[(10 + size)..];
    }

    /// <summary>
    /// Ruído em passa-baixa (~350 Hz) com volume que varia devagar e sem repetir. O passa-baixa deixa a correlação
    /// "larga" (dezenas de amostras), para a busca grossa do teste não pular o alinhamento; ruído branco perde a
    /// correlação já com uma amostra de diferença.
    /// </summary>
    private sealed class EnvelopedNoise(int seed) : ISampleProvider
    {
        private readonly Random _random = new(seed);
        private double _envelope = 0.5;
        private double _filtered;
        private long _position;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);

        public int Read(Span<float> buffer)
        {
            for (var i = 0; i < buffer.Length; i++, _position++)
            {
                if (_position % 2_205 == 0)
                {
                    _envelope = Math.Clamp(_envelope + ((_random.NextDouble() - 0.5) * 0.4), 0.05, 0.9);
                }

#pragma warning disable CA5394 // ruído de teste, não criptografia
                _filtered += 0.05 * (((_random.NextDouble() * 2) - 1) - _filtered);
#pragma warning restore CA5394
                buffer[i] = (float)(_filtered * _envelope * 4);
            }

            return buffer.Length;
        }
    }
}
