using System.IO.Compression;
using System.Text;

namespace LessonsPlayer.Core.Transcription;

/// <summary>
/// Detecta o laço de repetição do Whisper: o decodificador entra num ciclo e repete a mesma frase até o fim da
/// janela, no lugar da fala real. A razão de compressão (tamanho do texto / tamanho comprimido) é a medida do
/// Whisper original (<c>compression_ratio_threshold</c> = 2,4): texto repetido comprime muito.
/// <para>
/// Medido em 3 vídeos reais com small, small.en e large-v3-turbo: o texto de uma janela normal fica entre 0,5 e
/// 1,7. Na entrevista da NASA, o small repetiu <c>"I think it's a great opportunity to talk about space."</c>
/// 14 vezes sobre 28 s de fala real: razão 11,2. Frases inventadas curtas (<c>"Thank you."</c>) não passam do
/// limiar; essas são tratadas pelo <see cref="HallucinationFilter"/>.
/// </para>
/// </summary>
public static class RepetitionDetector
{
    /// <summary>Acima disso o texto é considerado repetitivo (mesmo limiar do Whisper original).</summary>
    public const double CompressionRatioThreshold = 2.4;

    /// <summary>Razão entre o tamanho do texto em UTF-8 e o tamanho dele comprimido com zlib (0 para texto vazio).</summary>
    public static double CompressionRatio(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length == 0)
        {
            return 0;
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(bytes);
        }

        return (double)bytes.Length / compressed.Length;
    }

    /// <summary>Indica se o texto tem cara de laço de repetição.</summary>
    public static bool IsRepetitive(string text) => CompressionRatio(text) > CompressionRatioThreshold;
}
