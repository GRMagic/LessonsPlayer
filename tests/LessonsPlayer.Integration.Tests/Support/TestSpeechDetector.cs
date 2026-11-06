using LessonsPlayer.Infrastructure;
using LessonsPlayer.Infrastructure.Whisper;
using Microsoft.Extensions.Logging.Abstractions;

namespace LessonsPlayer.Integration.Tests.Support;

/// <summary>O detector de voz real, com o modelo que o build copia para a pasta do teste.</summary>
internal static class TestSpeechDetector
{
    public static SileroSpeechDetector Create()
    {
        var options = InfrastructureOptions.CreateDefault();

        // Sem o modelo o detector devolve null e a legenda sai sem filtro: o teste passaria medindo outra coisa.
        if (!File.Exists(options.SpeechModelPath))
        {
            throw new FileNotFoundException("Modelo do VAD não foi copiado para a pasta do teste.", options.SpeechModelPath);
        }

        return new SileroSpeechDetector(options, NullLogger<SileroSpeechDetector>.Instance);
    }
}
