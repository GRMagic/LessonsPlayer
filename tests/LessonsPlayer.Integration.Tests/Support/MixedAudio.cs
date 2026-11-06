using NAudio.Wave;

namespace LessonsPlayer.Integration.Tests.Support;

/// <summary>Parte de um <see cref="MixedAudio"/>: fala sintetizada ou trecho sem fala.</summary>
internal abstract record AudioPart;

/// <summary>Texto falado pelo TTS.</summary>
internal sealed record SpeechPart(string Text) : AudioPart;

/// <summary>Trecho sem fala (música, ruído ou silêncio) com a duração dada.</summary>
internal sealed record NonSpeechPart(NonSpeechKind Kind, TimeSpan Duration, float Level = 0.1f) : AudioPart;

/// <summary>
/// Fala TTS intercalada com trechos sem fala, com o tempo real de cada palavra e de cada trecho sem fala.
/// Entre as partes vai uma pausa curta, como num vídeo real.
/// </summary>
internal sealed record MixedAudio(
    string FilePath, IReadOnlyList<SpokenWord> Words, IReadOnlyList<(TimeSpan Start, TimeSpan End)> NonSpeech)
{
    private const int SampleRate = SpeechAudio.SapiReferenceSampleRate;

    private static readonly TimeSpan PauseBetweenParts = TimeSpan.FromMilliseconds(500);

    /// <summary>Monta o áudio (44,1 kHz estéreo, WAV ou MP3). Retorna <c>null</c> se não houver voz para o idioma.</summary>
    public static MixedAudio? TryCreate(
        string cultureName, string outputPathWithoutExtension, TestAudioFormat format, params IReadOnlyList<AudioPart> parts)
    {
        var samples = new List<float>();
        var words = new List<SpokenWord>();
        var nonSpeech = new List<(TimeSpan, TimeSpan)>();
        var seed = 1;
        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0)
            {
                samples.AddRange(new float[(int)(PauseBetweenParts.TotalSeconds * SampleRate)]);
            }

            var offset = TimeSpan.FromSeconds((double)samples.Count / SampleRate);
            switch (parts[i])
            {
                case SpeechPart speech:
                    var referencePath = $"{outputPathWithoutExtension}.part{i}.16k.wav";
                    if (SpeechAudio.TrySynthesizeReference(speech.Text, cultureName, referencePath) is not { } spoken)
                    {
                        return null;
                    }

                    words.AddRange(spoken.Select(w => w with { Start = w.Start + offset }));
                    samples.AddRange(ReadSamples(referencePath));
                    break;
                case NonSpeechPart filler:
                    samples.AddRange(NonSpeechAudio.Create(filler.Kind, filler.Duration, SampleRate, filler.Level, seed++));
                    nonSpeech.Add((offset, offset + filler.Duration));
                    break;
                default:
                    throw new ArgumentException($"Parte desconhecida: {parts[i]}", nameof(parts));
            }
        }

        var mixedPath = outputPathWithoutExtension + ".16k.wav";
        using (var writer = new WaveFileWriter(mixedPath, WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1)))
        {
            writer.WriteSamples([.. samples], 0, samples.Count);
        }

        var outputPath = outputPathWithoutExtension + (format == TestAudioFormat.Mp3 ? ".mp3" : ".wav");
        SpeechAudio.ConvertToStereo44K(mixedPath, outputPath, format);
        return new MixedAudio(outputPath, words, nonSpeech);
    }

    private static float[] ReadSamples(string path)
    {
        using var reader = new AudioFileReader(path);
        var buffer = new float[(reader.Length / sizeof(float)) + SampleRate];
        var read = ((ISampleProvider)reader).Read(buffer);
        return buffer[..read];
    }
}
