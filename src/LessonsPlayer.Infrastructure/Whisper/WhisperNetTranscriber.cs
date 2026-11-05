using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Audio;
using LessonsPlayer.Core.Transcription;
using Microsoft.Extensions.Logging;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace LessonsPlayer.Infrastructure.Whisper;

/// <summary>
/// Transcrição com Whisper.net (whisper.cpp) e timestamps por DTW, que alinham cada token ao áudio
/// pela atenção cruzada do modelo. O runtime nativo é escolhido na ordem Vulkan → CPU, então o mesmo
/// executável usa GPU quando há driver Vulkan e cai para a CPU quando não há.
/// O modelo carregado fica em cache entre execuções (carregar leva alguns segundos).
/// <para>
/// <b>Janela a janela:</b> com DTW ligado, o whisper.cpp só entrega os trechos da primeira janela de 30 s
/// de cada chamada. A causa está em <c>whisper_full_with_state</c>: o laço que dispara <c>new_segment_callback</c>
/// depois do DTW usa <c>seg &lt; n_segments</c> em vez de <c>seg &lt; result_all.size()</c>, e a partir da
/// segunda janela não executa. O Whisper.net só lê trechos nesse callback. Ver
/// https://github.com/ggml-org/whisper.cpp/blob/f24588a272ae8e23280d9c220536437164e6ed28/src/whisper.cpp#L7727
/// e as issues #2669 e #3111. Medido: num áudio de 85 s a transcrição parou em 28,8 s, e sem DTW foi até o fim.
/// Por isso o áudio é processado em fatias de uma janela, cada uma começando onde o último trecho terminou.
/// Esse é o mesmo avanço (seek) que o Whisper faz internamente. O idioma detectado na primeira fatia vale para
/// as seguintes, e o texto anterior vai como prompt, para manter o contexto.
/// </para>
/// </summary>
public sealed partial class WhisperNetTranscriber : ITranscriber, IDisposable
{
    /// <summary>Unidade dos timestamps do whisper.cpp: centésimos de segundo.</summary>
    private const long TicksPerWhisperUnit = TimeSpan.TicksPerMillisecond * 10;

    /// <summary>Contexto passado entre janelas (o Whisper usa até metade do contexto do decodificador).</summary>
    private const int PromptCharacters = 224;

    /// <summary>Janela de áudio do Whisper.</summary>
    private static readonly TimeSpan WindowLength = TimeSpan.FromSeconds(30);

    /// <summary>Restos menores que isso no fim do áudio são ignorados (o whisper.cpp faz o mesmo).</summary>
    private static readonly TimeSpan MinimumRemainder = TimeSpan.FromSeconds(1);

    private static readonly Lock RuntimeConfigurationLock = new();
    private static bool _runtimeConfigured;

    private readonly ILogger<WhisperNetTranscriber> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (string ModelPath, bool UseGpu, WhisperFactory Factory)? _cached;

    public WhisperNetTranscriber(ILogger<WhisperNetTranscriber> logger)
    {
        _logger = logger;
        ConfigureRuntimeOnce();
    }

    public async IAsyncEnumerable<RecognizedSegment> TranscribeAsync(
        AudioSamples audio,
        TranscriberSettings settings,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(settings);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var factory = GetOrCreateFactory(settings);
            var language = string.IsNullOrWhiteSpace(settings.Language) ? TranscriptionLanguages.Auto : settings.Language;
            var context = string.Empty;
            var offset = TimeSpan.Zero;
            LogStarted(_logger, audio.Duration, settings.ModelSize, language);

            while (audio.Duration - offset >= MinimumRemainder)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var window = SliceWindow(audio, offset);
                var windowEnd = offset + TimeSpan.FromSeconds((double)window.Length / audio.SampleRate);

                var builder = factory.CreateBuilder()
                    .WithLanguage(language)
                    .WithTokenTimestamps()
                    .WithThreads(Math.Clamp(Environment.ProcessorCount - 1, 1, 8));
                if (context.Length > 0)
                {
                    builder = builder.WithPrompt(context);
                }

                var lastEnd = offset;
                await using (var processor = builder.Build())
                {
                    await foreach (var segment in processor.ProcessAsync(window, cancellationToken))
                    {
                        var recognized = Map(segment, offset, windowEnd);
                        if (language == TranscriptionLanguages.Auto && !string.IsNullOrWhiteSpace(segment.Language))
                        {
                            language = segment.Language; // fixa o idioma detectado para as próximas janelas
                        }

                        context = Tail(context + recognized.Text, PromptCharacters);
                        lastEnd = recognized.End > lastEnd ? recognized.End : lastEnd;
                        yield return recognized;
                    }
                }

                // Avança até o fim do último trecho; janela sem fala (ou sem progresso) é pulada inteira.
                offset = lastEnd > offset ? lastEnd : windowEnd;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _cached?.Factory.Dispose();
        _cached = null;
        _gate.Dispose();
    }

    private WhisperFactory GetOrCreateFactory(TranscriberSettings settings)
    {
        if (_cached is { } cached && cached.ModelPath == settings.ModelPath && cached.UseGpu == settings.UseGpu)
        {
            return cached.Factory;
        }

        _cached?.Factory.Dispose();
        _cached = null;

        WhisperFactory factory;
        try
        {
            factory = CreateFactory(settings, settings.UseGpu);
        }
        catch (Exception ex) when (settings.UseGpu)
        {
            // Driver de GPU problemático: tenta de novo só com CPU.
            LogGpuFallback(_logger, ex);
            factory = CreateFactory(settings, useGpu: false);
        }

        LogFactoryCreated(_logger, settings.ModelPath, RuntimeOptions.LoadedLibrary, settings.UseGpu);
        _cached = (settings.ModelPath, settings.UseGpu, factory);
        return factory;
    }

    private static WhisperFactory CreateFactory(TranscriberSettings settings, bool useGpu) =>
        WhisperFactory.FromPath(settings.ModelPath, new WhisperFactoryOptions
        {
            UseGpu = useGpu,
            UseDtwTimeStamps = true,
            HeadsPreset = ToHeadsPreset(settings.ModelSize),
            UseFlashAttention = false, // o DTW exige flash attention desligado
        });

    private static WhisperAlignmentHeadsPreset ToHeadsPreset(WhisperModelSize size) => size switch
    {
        WhisperModelSize.Tiny => WhisperAlignmentHeadsPreset.Tiny,
        WhisperModelSize.Base => WhisperAlignmentHeadsPreset.Base,
        WhisperModelSize.Small => WhisperAlignmentHeadsPreset.Small,
        WhisperModelSize.Medium => WhisperAlignmentHeadsPreset.Medium,
        WhisperModelSize.LargeV3Turbo => WhisperAlignmentHeadsPreset.LargeV3Turbo,
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Modelo sem preset de alinhamento."),
    };

    /// <summary>Converte um trecho da fatia para tempos absolutos no áudio (somando o início da fatia).</summary>
    private static RecognizedSegment Map(SegmentData segment, TimeSpan offset, TimeSpan windowEnd)
    {
        var tokens = segment.Tokens is null
            ? ImmutableArray<RecognizedToken>.Empty
            : [.. segment.Tokens.Select(t => new RecognizedToken(
                t.Text ?? string.Empty,
                offset + FromWhisperUnits(t.Start),
                offset + FromWhisperUnits(t.End),
                t.DtwTimestamp >= 0 ? offset + FromWhisperUnits(t.DtwTimestamp) : null))]; // t_dtw = fim do token

        var start = offset + segment.Start;
        var end = offset + segment.End;
        return new RecognizedSegment(
            start,
            end < windowEnd ? end : windowEnd, // o whisper às vezes estima o fim além do áudio
            segment.Text ?? string.Empty,
            tokens,
            segment.Language);
    }

    private static ReadOnlyMemory<float> SliceWindow(AudioSamples audio, TimeSpan offset)
    {
        var start = (int)Math.Min(audio.Samples.Length, Math.Round(offset.TotalSeconds * audio.SampleRate));
        var length = (int)Math.Min(audio.Samples.Length - start, WindowLength.TotalSeconds * audio.SampleRate);
        return audio.Samples.Slice(start, length);
    }

    private static string Tail(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[^maxLength..];

    private static TimeSpan FromWhisperUnits(long value) => TimeSpan.FromTicks(Math.Max(0, value) * TicksPerWhisperUnit);

    /// <summary>Ordem de carregamento dos runtimes nativos; precisa ser definida antes da primeira factory.</summary>
    private static void ConfigureRuntimeOnce()
    {
        lock (RuntimeConfigurationLock)
        {
            if (_runtimeConfigured)
            {
                return;
            }

            RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx];
            _runtimeConfigured = true;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Transcrevendo {Duration} com modelo {ModelSize}, idioma {Language}")]
    private static partial void LogStarted(ILogger logger, TimeSpan duration, WhisperModelSize modelSize, string language);

    [LoggerMessage(Level = LogLevel.Information, Message = "Modelo carregado: {ModelPath} (runtime {Runtime}, GPU solicitada: {UseGpu})")]
    private static partial void LogFactoryCreated(ILogger logger, string modelPath, RuntimeLibrary? runtime, bool useGpu);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao iniciar com GPU; usando CPU")]
    private static partial void LogGpuFallback(ILogger logger, Exception exception);
}
