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
    /// Formato nativo das vozes do Windows (ex.: Zira, Maria): o único em que <c>AudioPosition</c> sai correto.
    /// Bug do System.Speech (não documentado): <c>AudioFileOut.Duration</c> divide os bytes já convertidos
    /// para o formato de saída pelo <c>nAvgBytesPerSec</c> do formato do engine. Ver
    /// dotnet/runtime src/libraries/System.Speech/src/Internal/Synthesis/AudioFileOut.cs
    /// (<c>_bytesWritten * TicksPerSecond / _wfxIn.nAvgBytesPerSec</c>). Medido: a 22.050 Hz estéreo, a
    /// posição sai multiplicada por 88.200/32.000 = 2,756.
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

        // A API não expõe o formato nativo da voz; se não for 16 kHz mono, as posições saem escaladas
        // e passam do fim do arquivo. Melhor falhar claramente do que medir contra uma referência errada.
        using (var reference = new AudioFileReader(referencePath))
        {
            if (words.Count > 0 && words[^1].Start > reference.TotalTime)
            {
                throw new InvalidOperationException(
                    $"A voz '{voice.VoiceInfo.Name}' não gera {SapiReferenceSampleRate} Hz mono nativamente: " +
                    $"posição {words[^1].Start} além da duração {reference.TotalTime}.");
            }
        }

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
