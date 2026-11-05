using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Audio;
using LessonsPlayer.Core.Subtitles;

namespace LessonsPlayer.Core.Transcription;

/// <summary>Etapas da geração de legenda, para exibir o progresso.</summary>
public enum TranscriptionStage
{
    DownloadingModel,
    DecodingAudio,
    Transcribing,
}

/// <summary>Progresso da geração: etapa atual e fração concluída dela (0 a 1).</summary>
public readonly record struct TranscriptionProgress(TranscriptionStage Stage, double Fraction);

/// <summary>Opções escolhidas pelo usuário para gerar a legenda.</summary>
public sealed record TranscriptionOptions(WhisperModel Model, string Language, bool UseGpu);

/// <summary>
/// Caso de uso "gerar legenda": garante o modelo, decodifica o áudio, transcreve e monta o <see cref="Transcript"/>.
/// </summary>
public sealed class SubtitleGenerator(
    IModelStore modelStore,
    IAudioDecoder audioDecoder,
    ITranscriber transcriber,
    TimeProvider timeProvider)
{
    private const string EnglishLanguage = "en";

    public async Task<Transcript> GenerateAsync(
        string audioFilePath,
        TranscriptionOptions options,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFilePath);
        ArgumentNullException.ThrowIfNull(options);

        progress?.Report(new TranscriptionProgress(TranscriptionStage.DownloadingModel, 0));
        var downloadProgress = progress is null
            ? null
            : new SynchronousProgress<double>(f => progress.Report(new(TranscriptionStage.DownloadingModel, f)));
        var modelPath = await modelStore.EnsureDownloadedAsync(options.Model, downloadProgress, cancellationToken);

        progress?.Report(new TranscriptionProgress(TranscriptionStage.DecodingAudio, 0));
        var audio = await audioDecoder.DecodeForSpeechAsync(audioFilePath, cancellationToken);

        progress?.Report(new TranscriptionProgress(TranscriptionStage.Transcribing, 0));
        // Modelos .en só transcrevem inglês: detectar idioma ou pedir outro não faz sentido.
        var requestedLanguage = options.Model.EnglishOnly ? EnglishLanguage : options.Language;
        var settings = new TranscriberSettings(
            modelPath, options.Model.Size, requestedLanguage, options.UseGpu, options.Model.EnglishOnly);
        var segments = new List<Segment>();
        string? detectedLanguage = null;

        await foreach (var recognized in transcriber.TranscribeAsync(audio, settings, cancellationToken))
        {
            if (TokenWordMerger.ToSegment(recognized) is { } segment)
            {
                segments.Add(segment);
            }

            detectedLanguage ??= recognized.Language;
            progress?.Report(new TranscriptionProgress(TranscriptionStage.Transcribing, Fraction(recognized.End, audio)));
        }

        progress?.Report(new TranscriptionProgress(TranscriptionStage.Transcribing, 1));

        var language = requestedLanguage == TranscriptionLanguages.Auto ? detectedLanguage : requestedLanguage;
        var metadata = new TranscriptMetadata(
            Path.GetFileName(audioFilePath), options.Model.Id, language, timeProvider.GetLocalNow());

        // Ordenação estável por segurança: o Transcript exige segmentos em ordem de início.
        return new Transcript(metadata, segments.OrderBy(s => s.Start));
    }

    private static double Fraction(TimeSpan position, AudioSamples audio) =>
        audio.Duration > TimeSpan.Zero ? Math.Clamp(position / audio.Duration, 0, 1) : 0;

    /// <summary>Repassa o progresso na hora, sem postar no SynchronizationContext como faz <see cref="Progress{T}"/>.</summary>
    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
