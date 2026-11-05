using System.Globalization;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LessonsPlayer.Integration.Tests.Support;

/// <summary>Palavra falada pelo TTS e o instante exato em que começa no arquivo gerado (ground truth).</summary>
internal sealed record SpokenWord(string Text, TimeSpan Start);

/// <summary>Áudio sintetizado com as vozes do Windows (SAPI), com o tempo real de cada palavra.</summary>
internal sealed record SpeechAudio(string FilePath, IReadOnlyList<SpokenWord> Words)
{
    /// <summary>
    /// Formato em que o SAPI reporta <c>AudioPosition</c> corretamente. Em outros formatos o valor vem
    /// escalado como se o fluxo fosse 16 kHz mono 16 bits (bug conhecido do System.Speech).
    /// </summary>
    private const int SapiReferenceSampleRate = 16_000;

    /// <summary>
    /// Gera um WAV 44,1 kHz estéreo (para exercitar downmix e resample do decodificador);
    /// retorna <c>null</c> se não houver voz instalada para o idioma.
    /// </summary>
    public static SpeechAudio? TrySynthesize(string text, string cultureName, string outputPath)
    {
        using var synthesizer = new SpeechSynthesizer();
        var voice = synthesizer.GetInstalledVoices(new CultureInfo(cultureName)).FirstOrDefault(v => v.Enabled);
        if (voice is null)
        {
            return null;
        }

        var referencePath = Path.ChangeExtension(outputPath, ".16k.wav");
        synthesizer.SelectVoice(voice.VoiceInfo.Name);
        synthesizer.SetOutputToWaveFile(
            referencePath,
            new SpeechAudioFormatInfo(SapiReferenceSampleRate, AudioBitsPerSample.Sixteen, AudioChannel.Mono));

        var words = new List<SpokenWord>();
        synthesizer.SpeakProgress += (_, e) =>
            words.Add(new SpokenWord(text.Substring(e.CharacterPosition, e.CharacterCount), e.AudioPosition));

        synthesizer.Speak(text);
        synthesizer.SetOutputToNull();

        ConvertToStereo44K(referencePath, outputPath);
        return new SpeechAudio(outputPath, words);
    }

    private static void ConvertToStereo44K(string sourcePath, string targetPath)
    {
        using var reader = new AudioFileReader(sourcePath);
        var stereo = new MonoToStereoSampleProvider(new WdlResamplingSampleProvider(reader, 44_100));
        WaveFileWriter.CreateWaveFile16(targetPath, stereo);
    }
}
