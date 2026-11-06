using System.Diagnostics;
using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Audio;
using LessonsPlayer.Core.Transcription;
using Microsoft.Extensions.Logging;
using Whisper.net;

namespace LessonsPlayer.Infrastructure.Whisper;

/// <summary>
/// Detector de voz Silero VAD v6.2.0 (formato ggml), executado pelo whisper.cpp do Whisper.net, na CPU.
/// Leva ~2 s para 13 min de áudio, e o modelo (0,9 MB) vai junto com o app.
/// <para>
/// <b>Versão e limiar, medidos</b> em 3 vídeos reais contra a legenda automática do YouTube (palavras fora das
/// regiões de fala ampliadas em 0,5 s): com o v6.2.0 e limiar 0,35, ficaram de fora 0 de 1.856 palavras na
/// aula, 2 de 2.090 na entrevista via satélite e 9 de 818 na conversa com música (voz embargada; 0 com
/// margem de 1 s). O v5.1.2 deixou de fora 35 a 57 palavras da entrevista. No limiar padrão (0,5) o resultado é
/// parecido, e com 0,2 a música e o ruído começam a passar como fala.
/// </para>
/// <para>
/// Se o modelo não existir ou o whisper.cpp falhar, retorna <c>null</c> e a legenda é gerada sem o filtro.
/// </para>
/// </summary>
public sealed partial class SileroSpeechDetector(InfrastructureOptions options, ILogger<SileroSpeechDetector> logger)
    : ISpeechDetector
{
    /// <summary>Probabilidade a partir da qual um trecho conta como fala (o padrão do whisper.cpp é 0,5).</summary>
    private const float SpeechThreshold = 0.35f;

    public async Task<SpeechActivity?> DetectAsync(AudioSamples audio, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        if (audio.SampleRate != AudioSamples.SpeechSampleRate)
        {
            throw new ArgumentException($"O VAD exige {AudioSamples.SpeechSampleRate} Hz.", nameof(audio));
        }

        if (!File.Exists(options.SpeechModelPath))
        {
            LogModelMissing(logger, options.SpeechModelPath);
            return null;
        }

        WhisperRuntime.EnsureConfigured();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var factory = WhisperVadFactory.FromPath(options.SpeechModelPath);
            await using var processor = factory.CreateBuilder().WithThreshold(SpeechThreshold).Build();
            var regions = await processor.DetectSpeechAsync(audio.Samples, cancellationToken);

            var speech = new SpeechActivity(regions.Select(r => new SpeechRegion(r.Start, r.End)));
            LogDetected(logger, speech.Regions.Length, speech.TotalSpeech, audio.Duration, stopwatch.Elapsed);
            return speech;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "VAD: {Count} trechos com fala, {Speech} de {Duration} (em {Elapsed})")]
    private static partial void LogDetected(ILogger logger, int count, TimeSpan speech, TimeSpan duration, TimeSpan elapsed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Modelo do VAD não encontrado em {Path}; a legenda sai sem o filtro de fala")]
    private static partial void LogModelMissing(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha no VAD; a legenda sai sem o filtro de fala")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
