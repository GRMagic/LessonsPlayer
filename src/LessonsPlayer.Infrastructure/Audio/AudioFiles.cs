using System.Runtime.InteropServices;
using LessonsPlayer.Core.Audio;

namespace LessonsPlayer.Infrastructure.Audio;

/// <summary>Abertura de arquivos de áudio comum ao player e ao decodificador da transcrição.</summary>
internal static class AudioFiles
{
    /// <summary>
    /// Abre o arquivo com <see cref="AudioFileSource"/> (seek exato também em MP3 VBR). Falhas de decodificação (Media Foundation, cabeçalho
    /// WAV inválido, arquivo vazio) viram <see cref="UnsupportedAudioFileException"/>; arquivo inexistente ou
    /// sem permissão continuam como exceções de E/S.
    /// </summary>
    public static AudioFileSource OpenReader(string filePath)
    {
        try
        {
            return AudioFileSource.Open(filePath);
        }
        catch (Exception ex) when (ex is COMException or FormatException or InvalidDataException or EndOfStreamException)
        {
            throw new UnsupportedAudioFileException($"Não foi possível decodificar \"{filePath}\": {ex.Message}", ex);
        }
    }
}
