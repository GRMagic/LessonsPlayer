namespace LessonsPlayer.Core.Audio;

/// <summary>
/// O arquivo não pôde ser decodificado como áudio: formato não suportado, arquivo vazio ou danificado.
/// A mensagem é técnica (vai para o log); a interface mostra um texto próprio para esse caso.
/// </summary>
public sealed class UnsupportedAudioFileException : Exception
{
    public UnsupportedAudioFileException()
    {
    }

    public UnsupportedAudioFileException(string message)
        : base(message)
    {
    }

    public UnsupportedAudioFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
