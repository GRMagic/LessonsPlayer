using System.Buffers;
using NAudio.Wave;

namespace LessonsPlayer.Infrastructure.Audio;

/// <summary>Converte qualquer número de canais em mono, pela média dos canais.</summary>
internal sealed class MonoDownmixSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;

    public MonoDownmixSampleProvider(ISampleProvider source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _channels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(Span<float> buffer)
    {
        var needed = buffer.Length * _channels;
        var interleaved = ArrayPool<float>.Shared.Rent(needed);
        try
        {
            var read = _source.Read(interleaved.AsSpan(0, needed));
            var frames = read / _channels;
            for (var frame = 0; frame < frames; frame++)
            {
                var sum = 0f;
                var offset = frame * _channels;
                for (var c = 0; c < _channels; c++)
                {
                    sum += interleaved[offset + c];
                }

                buffer[frame] = sum / _channels;
            }

            return frames;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(interleaved);
        }
    }
}
