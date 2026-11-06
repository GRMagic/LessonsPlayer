using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LessonsPlayer.Integration.Tests.Support;

/// <summary>Arquivos de tom puro, para testar o player sem depender de TTS.</summary>
internal static class ToneAudio
{
    /// <summary>Cria um tom de 440 Hz, 44,1 kHz estéreo, e retorna o caminho do arquivo.</summary>
    public static string Create(string pathWithoutExtension, TimeSpan duration, TestAudioFormat format)
    {
        var tone = new SignalGenerator(44_100, 2) { Frequency = 440, Gain = 0.1 }.Take(duration);
        if (format == TestAudioFormat.Mp3)
        {
            var mp3Path = pathWithoutExtension + ".mp3";
            MediaFoundationEncoder.EncodeToMp3(new SampleToWaveProvider16(tone), mp3Path, 128_000);
            return mp3Path;
        }

        var wavPath = pathWithoutExtension + ".wav";
        WaveFileWriter.CreateWaveFile16(wavPath, tone);
        return wavPath;
    }
}
