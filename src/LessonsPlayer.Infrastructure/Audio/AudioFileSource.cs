using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LessonsPlayer.Infrastructure.Audio;

/// <summary>
/// Leitor de áudio em PCM float com volume, como o <see cref="AudioFileReader"/> do NAudio, mas com seek exato em MP3.
/// <para>
/// <b>Por quê:</b> no NAudio 3, o <see cref="AudioFileReader"/> decodifica MP3 pelo Media Foundation. Ele posiciona
/// MP3 VBR como se o bitrate fosse constante e informa a posição pedida, não a real. Medido num MP3 VBR de 56 s (LAME,
/// 32–224 kbps, gravação real): depois do seek o áudio começava até 3 s longe (+3,0 s em 5 s; +1,2 s em 20 s;
/// −2,0 s em 40 s). Tocar desde o início ficava sincronizado, mas qualquer clique numa palavra (seek) tirava a legenda
/// de sincronia. O <see cref="Mp3FileReader"/> indexa os quadros ao abrir e posiciona no quadro certo: erro 0 ms nos
/// mesmos pontos. A leitura contínua fica 11–12 ms atrás da do Media Foundation, de forma constante no arquivo todo
/// (medido numa aula de 10 min): o Media Foundation descarta o atraso inicial do codificador indicado no cabeçalho
/// LAME. Para as legendas geradas antes, isso é bem menos que o erro do alinhamento (30–50 ms); as novas usam este
/// mesmo leitor e não têm deslocamento nenhum.
/// </para>
/// <para>
/// O <see cref="Mp3FileReader"/> usa o codec MP3 do ACM do Windows. Se ele faltar (ou o arquivo não for MP3 de fato),
/// o leitor cai para o <see cref="AudioFileReader"/>, como antes. Os outros formatos (WAV, M4A, WMA, FLAC) também
/// usam o <see cref="AudioFileReader"/>: os contêineres deles têm índice de tempo.
/// </para>
/// </summary>
internal sealed class AudioFileSource : WaveStream, ISampleProvider
{
    private readonly WaveStream _source;
    private readonly SampleChannel _channel;
    private readonly int _sourceBlockAlign;
    private readonly int _destBlockAlign;
    private readonly Lock _lock = new();

    private AudioFileSource(WaveStream source)
    {
        _source = source;
        _channel = new SampleChannel(source, forceStereo: false);
        _sourceBlockAlign = source.WaveFormat.BlockAlign;
        _destBlockAlign = _channel.WaveFormat.BlockAlign;
        Length = SourceToDest(source.Length);
    }

    /// <summary>Nome do leitor usado por baixo (para log e testes).</summary>
    public string DecoderName => _source.GetType().Name;

    public override WaveFormat WaveFormat => _channel.WaveFormat;

    public override long Length { get; }

    public override long Position
    {
        get => SourceToDest(_source.Position);
        set
        {
            lock (_lock)
            {
                _source.Position = DestToSource(value);
            }
        }
    }

    /// <summary>Volume linear (1 = original).</summary>
    public float Volume
    {
        get => _channel.Volume;
        set => _channel.Volume = value;
    }

    /// <summary>Abre o arquivo com o leitor de seek exato para o formato (ver o resumo da classe).</summary>
    public static AudioFileSource Open(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (Path.GetExtension(filePath).Equals(".mp3", StringComparison.OrdinalIgnoreCase) && TryOpenMp3(filePath) is { } mp3)
        {
            return new AudioFileSource(mp3);
        }

        return new AudioFileSource(new AudioFileReader(filePath));
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer) => Read(MemoryMarshal.Cast<byte, float>(buffer)) * sizeof(float);

    public int Read(Span<float> buffer)
    {
        lock (_lock)
        {
            return _channel.Read(buffer);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _source.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Mp3FileReader? TryOpenMp3(string filePath)
    {
        try
        {
            return new Mp3FileReader(filePath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or FormatException
            or COMException or NAudio.MmException or EndOfStreamException)
        {
            // Sem o codec MP3 do ACM, ou arquivo com extensão .mp3 que não é MP3: o Media Foundation decide.
            return null;
        }
    }

    private long SourceToDest(long sourceBytes) => sourceBytes / _sourceBlockAlign * _destBlockAlign;

    private long DestToSource(long destBytes) => destBytes / _destBlockAlign * _sourceBlockAlign;
}
