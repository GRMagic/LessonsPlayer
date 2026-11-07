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
/// <param name="Stage">A etapa atual.</param>
/// <param name="Fraction">Quanto dela já foi feito, de 0 a 1.</param>
/// <param name="Gpu">Na transcrição, a GPU em uso; <c>null</c> é a CPU.</param>
public readonly record struct TranscriptionProgress(TranscriptionStage Stage, double Fraction, GpuDevice? Gpu = null);

/// <summary>Opções para gerar a legenda: o modelo e o idioma escolhidos pelo usuário, e a GPU detectada.</summary>
/// <param name="Model">O modelo do Whisper.</param>
/// <param name="Language">Código do idioma ou <c>auto</c>.</param>
/// <param name="Gpu">A GPU a usar, ou <c>null</c> para a CPU.</param>
public sealed record TranscriptionOptions(WhisperModel Model, string Language, GpuDevice? Gpu);

/// <summary>
/// Caso de uso "gerar legenda": garante o modelo, decodifica o áudio, detecta onde há fala, transcreve e monta
/// o <see cref="Transcript"/>. As palavras que o Whisper inventa longe da fala são descartadas
/// (<see cref="HallucinationFilter"/>).
/// </summary>
public sealed class SubtitleGenerator(
    IModelStore modelStore,
    IAudioDecoder audioDecoder,
    ISpeechDetector speechDetector,
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
        var speech = await DetectSpeechAsync(audio, cancellationToken); // rápido: ~2 s por 10 min de áudio na CPU

        // Modelos .en só transcrevem inglês: detectar idioma ou pedir outro não faz sentido.
        var requestedLanguage = options.Model.EnglishOnly ? EnglishLanguage : options.Language;
        var settings = new TranscriberSettings(
            modelPath, options.Model.Size, requestedLanguage, options.Gpu, options.Model.EnglishOnly, speech);
        // Onde o modelo ficou de fato: se a GPU falhar, o motor usa a CPU, e o progresso mostra isso.
        var gpu = await transcriber.LoadModelAsync(settings, cancellationToken);
        progress?.Report(new TranscriptionProgress(TranscriptionStage.Transcribing, 0, gpu));
        var segments = new List<Segment>();
        string? detectedLanguage = null;

        await foreach (var recognized in transcriber.TranscribeAsync(audio, settings, cancellationToken))
        {
            if (TokenWordMerger.ToSegment(recognized) is { } segment
                && HallucinationFilter.KeepWordsNearSpeech(segment, speech) is { } spoken)
            {
                segments.Add(spoken);
            }

            detectedLanguage ??= recognized.Language;
            progress?.Report(new TranscriptionProgress(TranscriptionStage.Transcribing, Fraction(recognized.End, audio), gpu));
        }

        progress?.Report(new TranscriptionProgress(TranscriptionStage.Transcribing, 1, gpu));

        var language = requestedLanguage == TranscriptionLanguages.Auto ? detectedLanguage : requestedLanguage;
        var metadata = new TranscriptMetadata(
            Path.GetFileName(audioFilePath), options.Model.Id, language, timeProvider.GetLocalNow());

        // Ordenação estável por segurança: o Transcript exige segmentos em ordem de início.
        // Depois, as frases que o Whisper cortou em mais de um trecho são juntadas.
        return new Transcript(metadata, SentenceRegrouper.Regroup(segments.OrderBy(s => s.Start)));
    }

    /// <summary>
    /// Regiões com fala, ou <c>null</c> para transcrever tudo. Um áudio inteiro "sem fala" não é levado a sério:
    /// mais provável que o detector tenha falhado (gravação muito baixa, voz distorcida) do que a aula não ter
    /// fala nenhuma, e perder a fala real é pior do que deixar passar um texto inventado.
    /// </summary>
    private async Task<SpeechActivity?> DetectSpeechAsync(AudioSamples audio, CancellationToken cancellationToken)
    {
        var speech = await speechDetector.DetectAsync(audio, cancellationToken);
        return speech is { HasSpeech: true } ? speech : null;
    }

    private static double Fraction(TimeSpan position, AudioSamples audio) =>
        audio.Duration > TimeSpan.Zero ? Math.Clamp(position / audio.Duration, 0, 1) : 0;

    /// <summary>Repassa o progresso na hora, sem postar no SynchronizationContext como faz <see cref="Progress{T}"/>.</summary>
    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
