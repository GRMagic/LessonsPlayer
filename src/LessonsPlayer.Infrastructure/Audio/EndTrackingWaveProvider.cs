using NAudio.Wave;

namespace LessonsPlayer.Infrastructure.Audio;

/// <summary>
/// Repassa o áudio da fonte e marca quando ela se esgota (<c>Read</c> devolve 0). É o único sinal confiável
/// de fim de faixa: em MP3 o tamanho total (<c>Length</c>) é uma estimativa e pode passar do que de fato
/// é decodificado, então comparar posição com tamanho nunca chega ao fim.
/// </summary>
internal sealed class EndTrackingWaveProvider(IWaveProvider source) : IWaveProvider
{
    private volatile bool _isExhausted;

    public WaveFormat WaveFormat => source.WaveFormat;

    /// <summary>A fonte já entregou tudo; lido pela thread da UI, escrito pela de reprodução.</summary>
    public bool IsExhausted => _isExhausted;

    /// <summary>Chamado depois de reposicionar a fonte.</summary>
    public void Reset() => _isExhausted = false;

    public int Read(Span<byte> buffer)
    {
        var read = source.Read(buffer);
        if (read == 0 && !buffer.IsEmpty)
        {
            _isExhausted = true;
        }

        return read;
    }
}
