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
/// </summary>
public sealed partial class WhisperNetTranscriber : ITranscriber, IDisposable
{
    /// <summary>Unidade dos timestamps do whisper.cpp: centésimos de segundo.</summary>
    private const long TicksPerWhisperUnit = TimeSpan.TicksPerMillisecond * 10;

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
            await using var processor = factory.CreateBuilder()
                .WithLanguage(string.IsNullOrWhiteSpace(settings.Language) ? TranscriptionLanguages.Auto : settings.Language)
                .WithTokenTimestamps()
                .WithThreads(Math.Clamp(Environment.ProcessorCount - 1, 1, 8))
                .Build();

            LogStarted(_logger, audio.Duration, settings.ModelSize, settings.Language);
            await foreach (var segment in processor.ProcessAsync(audio.Samples, cancellationToken))
            {
                yield return Map(segment);
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

    private static RecognizedSegment Map(SegmentData segment)
    {
        var tokens = segment.Tokens is null
            ? ImmutableArray<RecognizedToken>.Empty
            : [.. segment.Tokens.Select(t => new RecognizedToken(
                t.Text ?? string.Empty,
                FromWhisperUnits(t.Start),
                FromWhisperUnits(t.End),
                t.DtwTimestamp >= 0 ? FromWhisperUnits(t.DtwTimestamp) : null))]; // t_dtw = fim do token

        return new RecognizedSegment(segment.Start, segment.End, segment.Text ?? string.Empty, tokens, segment.Language);
    }

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
