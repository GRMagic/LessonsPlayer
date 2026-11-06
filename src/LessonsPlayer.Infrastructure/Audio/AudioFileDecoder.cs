using System.Buffers;
using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LessonsPlayer.Infrastructure.Audio;

/// <summary>
/// Decodifica o áudio para 16 kHz mono float, como exige o Whisper.
/// Usa o mesmo <see cref="AudioFileSource"/> do player, para que a base de tempo da legenda seja
/// idêntica à da reprodução (atraso inicial de MP3, seek em VBR etc.).
/// </summary>
public sealed class AudioFileDecoder : IAudioDecoder
{
    public Task<AudioSamples> DecodeForSpeechAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return Task.Run(() => Decode(filePath, cancellationToken), cancellationToken);
    }

    private static AudioSamples Decode(string filePath, CancellationToken cancellationToken)
    {
        using var reader = AudioFiles.OpenReader(filePath);
        ISampleProvider mono = reader.WaveFormat.Channels == 1 ? reader : new MonoDownmixSampleProvider(reader);
        ISampleProvider resampled = mono.WaveFormat.SampleRate == AudioSamples.SpeechSampleRate
            ? mono
            : new WdlResamplingSampleProvider(mono, AudioSamples.SpeechSampleRate);

        // Reserva o tamanho estimado (+1 s de folga) para evitar realocações em áudios longos.
        var estimated = (int)Math.Min(Array.MaxLength, (reader.TotalTime.TotalSeconds + 1) * AudioSamples.SpeechSampleRate);
        var samples = new float[Math.Max(estimated, AudioSamples.SpeechSampleRate)];
        var count = 0;

        var chunk = ArrayPool<float>.Shared.Rent(AudioSamples.SpeechSampleRate);
        try
        {
            int read;
            while ((read = resampled.Read(chunk.AsSpan(0, AudioSamples.SpeechSampleRate))) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (count + read > samples.Length)
                {
                    Array.Resize(ref samples, Math.Max(samples.Length * 2, count + read));
                }

                chunk.AsSpan(0, read).CopyTo(samples.AsSpan(count));
                count += read;
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(chunk);
        }

        return new AudioSamples(samples.AsMemory(0, count), AudioSamples.SpeechSampleRate);
    }
}
