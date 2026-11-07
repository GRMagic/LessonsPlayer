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
/// <para>
/// <b>Uma janela por chamada:</b> depois da primeira janela, o whisper.cpp seguiria com a sobra da fatia (do fim
/// do último trecho até o fim da fatia). Os trechos dessa sobra não chegam ao Whisper.net (mesmo bug do callback),
/// e a fatia seguinte processa esse áudio de novo. Por isso o encoder é interrompido antes da segunda janela.
/// Além de economizar uma passada por fatia, isso evita um abort nativo: em sobras de até ~150 ms, o filtro
/// mediano do DTW falha em <c>WHISPER_ASSERT(filter_width &lt; a-&gt;ne[2])</c>
/// (<c>median_filter</c> no whisper.cpp do Whisper.net 1.9.1; corrigido depois no whisper.cpp) e derruba o
/// processo. Medido em aulas TTS de 1,5 a 2,5 min (4 modelos, 3 áudios): mesmos trechos e tempos, e transcrição
/// mais rápida em 11 de 12 casos (medium: 45 s → 22 s). Num experimento sem prompt, o abort acontecia na sobra
/// da 3ª fatia e deixou de acontecer com a interrupção. O log nativo
/// <c>"encoder_begin_callback returned false - aborting"</c>, de nível erro, é esperado a cada fatia.
/// </para>
/// <para>
/// <b>Trechos sem fala:</b> com a detecção de voz (<see cref="TranscriberSettings.Speech"/>), uma fatia que
/// começaria num trecho sem fala de <see cref="MinimumSkip"/> ou mais começa <see cref="SpeechLeadIn"/> antes da
/// próxima fala, e uma que passaria por cima de um trecho sem fala de <see cref="MinimumCutPause"/> ou mais termina
/// <see cref="SpeechLeadIn"/> depois do fim da fala. Depois da última fala, a transcrição termina. É em música,
/// créditos e silêncio longo que o Whisper inventa texto, e o que ele não ouve não tem como inventar. O áudio não é
/// cortado nem emendado (só mudam o início e o fim da fatia), então os tempos continuam absolutos. Sem isso, a
/// primeira palavra depois de uma vinheta saía no começo da fatia, até 27 s antes de ser dita. O texto que é só
/// marcação (<c>[BLANK_AUDIO]</c>) não vai para o prompt: com ele, num teste com fatias cortadas em pausas de 2 s,
/// o modelo passou a responder <c>[BLANK_AUDIO]</c> por 50 s de fala real (NASA, small.en).
/// </para>
/// <para>
/// <b>Laço de repetição:</b> às vezes o decodificador repete uma frase até o fim da janela, no lugar da fala
/// (<see cref="RepetitionDetector"/>). Isso vem do prompt: decodificada de novo sem ele, a janela sai certa.
/// Medido na entrevista da NASA (small): <c>"I think it's a great opportunity to talk about space."</c> 14 vezes,
/// razão de compressão 11,2; sem o prompt, 1,6 e a fala real. Fica a versão menos repetitiva das duas.
/// </para>
/// <para>
/// <b>Fala pulada:</b> às vezes o último trecho de uma fatia vai até o fim da janela, mas o texto para bem antes:
/// o modelo pulou a fala do resto da janela. Medido na NASA (small.en): última palavra em 4:38,7, trecho até
/// 5:03,7, com 17 s de fala segundo o VAD; 55 palavras da legenda automática sumiram. Quando o VAD ouve pelo menos
/// <see cref="MinimumSkippedSpeech"/> de fala nessa sobra, a próxima fatia começa no fim da última palavra. O
/// Whisper original, com <c>word_timestamps</c>, recomeça sempre na última palavra; aqui, sem a exigência do VAD,
/// isso fez o small.en perder 676 palavras na mesma entrevista.
/// </para>
/// <para>
/// <b>Sobra já ouvida:</b> o VAD estende a fala um pouco além do último trecho do Whisper (folga do detector, sílaba
/// final). Quando a janela terminava no fim da fala, essa sobra virava uma fatia própria, de ~1 s, com áudio que a
/// janela anterior já continha, e o modelo completava a frase com texto inventado, todo no mesmo instante. Medido no
/// áudio de teste do usuário (small): <c>"I'm going to show you how to do that."</c> no silêncio depois de
/// <c>"application,"</c>. Em 4 áudios reais com 3 modelos, foram 8 fatias assim e nenhuma palavra real: em 5,
/// texto inventado (<c>"you"</c>, <c>"Bye."</c>, <c>"Thank you."</c>); nas outras, só marcações. Por isso uma fatia
/// sem fala depois do fim da janela anterior não é decodificada. É o que o Whisper original faz quando a janela termina
/// com um trecho fechado: avança a janela inteira. A exceção é o recomeço por fala pulada, que decodifica de novo
/// de propósito.
/// </para>
/// <para>
/// <b>Descarte:</b> liberar o modelo enquanto o whisper.cpp ainda calcula derruba o processo (0xC0000005 em
/// ggml-cpu-whisper.dll; acontecia ao fechar o app durante uma geração na CPU). Por isso <see cref="Dispose"/>
/// cancela o trabalho em andamento e espera ele terminar antes de liberar o modelo.
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

    /// <summary>Espera máxima, no descarte, para a transcrição em andamento parar.</summary>
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Ao pular um trecho sem fala, a fatia começa esse tanto antes da fala (o VAD corta sílabas fracas).</summary>
    private static readonly TimeSpan SpeechLeadIn = TimeSpan.FromMilliseconds(500);

    /// <summary>Trechos sem fala mais curtos que isso não são pulados (pausas normais entre frases).</summary>
    private static readonly TimeSpan MinimumSkip = TimeSpan.FromSeconds(2);

    /// <summary>
    /// A fatia termina antes de um trecho sem fala a partir desse tamanho (vinheta, intervalo com música). Cortar em
    /// pausas curtas também funciona, mas multiplica as fatias numa conversa e deixa a transcrição até 2x mais lenta.
    /// </summary>
    private static readonly TimeSpan MinimumCutPause = TimeSpan.FromSeconds(10);

    /// <summary>Fala que o VAD precisa ouvir depois da última palavra de uma fatia para ela ser processada de novo.</summary>
    private static readonly TimeSpan MinimumSkippedSpeech = TimeSpan.FromSeconds(2);

    private readonly ILogger<WhisperNetTranscriber> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _disposing = new();
    private (string ModelPath, bool UseGpu, WhisperFactory Factory)? _cached;
    private int _disposed;

    public WhisperNetTranscriber(ILogger<WhisperNetTranscriber> logger)
    {
        _logger = logger;
        WhisperRuntime.EnsureConfigured();
    }

    public async IAsyncEnumerable<RecognizedSegment> TranscribeAsync(
        AudioSamples audio,
        TranscriberSettings settings,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        // O descarte também cancela: assim ele não espera a transcrição inteira terminar.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposing.Token);
        cancellationToken = linked.Token;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var factory = GetOrCreateFactory(settings);
            var language = string.IsNullOrWhiteSpace(settings.Language) ? TranscriptionLanguages.Auto : settings.Language;
            var context = string.Empty;
            var offset = TimeSpan.Zero;
            TimeSpan? heardUntil = null; // fim da janela anterior: o Whisper já ouviu o áudio até ali
            LogStarted(_logger, audio.Duration, settings.ModelSize, language);

            while (audio.Duration - offset >= MinimumRemainder)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (settings.Speech is { } speech)
                {
                    if (SkipNonSpeech(speech, offset, audio.Duration) is not { } next)
                    {
                        break; // não há mais fala
                    }

                    offset = next;
                }

                var sliceEnd = SliceEnd(audio, offset, settings.Speech);
                if (heardUntil is { } until && IsAlreadyHeard(settings.Speech, offset, sliceEnd, until))
                {
                    LogAlreadyHeard(_logger, offset, until);
                    offset = until;
                    heardUntil = null;
                    continue;
                }

                var window = SliceWindow(audio, offset, sliceEnd);
                var windowEnd = offset + TimeSpan.FromSeconds((double)window.Length / audio.SampleRate);
                var slice = await DecodeSliceAsync(factory, window, offset, windowEnd, language, context, cancellationToken);
                if (context.Length > 0 && RepetitionDetector.IsRepetitive(TextOf(slice)))
                {
                    // Laço de repetição: o texto anterior (prompt) costuma prender o modelo no ciclo.
                    var retry = await DecodeSliceAsync(factory, window, offset, windowEnd, language, string.Empty, cancellationToken);
                    var before = RepetitionDetector.CompressionRatio(TextOf(slice));
                    var after = RepetitionDetector.CompressionRatio(TextOf(retry));
                    LogRepetition(_logger, offset, before, after);
                    slice = after < before ? retry : slice;
                }

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    var speechInSlice = settings.Speech?.SpeechBetween(offset, windowEnd);
                    var sliceText = TextOf(slice);
                    LogSlice(_logger, offset, windowEnd, speechInSlice, sliceText);
                }

                var lastEnd = offset;
                foreach (var recognized in slice)
                {
                    if (language == TranscriptionLanguages.Auto && !string.IsNullOrWhiteSpace(recognized.Language))
                    {
                        language = recognized.Language; // fixa o idioma detectado para as próximas janelas
                    }

                    if (!NonSpeechText.IsAnnotationOnly(recognized.Text))
                    {
                        // Com [BLANK_AUDIO] no prompt, o modelo passa a responder [BLANK_AUDIO] por cima da fala.
                        context = Tail(context + recognized.Text, PromptCharacters);
                    }

                    lastEnd = recognized.End > lastEnd ? recognized.End : lastEnd;
                    yield return recognized;
                }

                var nextOffset = NextOffset(slice, offset, windowEnd, lastEnd, settings.Speech);

                // Recomeçar antes do último trecho (fala pulada) é de propósito: essa fala precisa ser decodificada de novo.
                heardUntil = nextOffset < lastEnd ? null : windowEnd;
                offset = nextOffset;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _disposing.Cancel();

        // O gate só é liberado depois que o processador do Whisper.net terminou (o DisposeAsync dele espera
        // o processamento nativo). Antes disso o modelo ainda está em uso.
        if (!_gate.Wait(DisposeTimeout))
        {
            // O processo está terminando: deixar o modelo sem liberar é inofensivo; liberar em uso, não.
            LogDisposeTimedOut(_logger, DisposeTimeout);
            return;
        }

        _cached?.Factory.Dispose();
        _cached = null;
        _gate.Release();
        _gate.Dispose();
        _disposing.Dispose();
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
            HeadsPreset = ToHeadsPreset(settings.ModelSize, settings.EnglishOnly),
            UseFlashAttention = false, // o DTW exige flash attention desligado
        });

    /// <summary>
    /// Processa uma fatia (só a primeira janela; ver o resumo). Os trechos chegam todos no fim, depois do DTW,
    /// então juntá-los numa lista não atrasa nada.
    /// </summary>
    private static async Task<List<RecognizedSegment>> DecodeSliceAsync(
        WhisperFactory factory,
        ReadOnlyMemory<float> window,
        TimeSpan offset,
        TimeSpan windowEnd,
        string language,
        string prompt,
        CancellationToken cancellationToken)
    {
        var encoderRuns = 0;
        var builder = factory.CreateBuilder()
            .WithLanguage(language)
            .WithTokenTimestamps()
            .WithThreads(Math.Clamp(Environment.ProcessorCount - 1, 1, 8))
            .WithEncoderBeginHandler(_ => ++encoderRuns == 1); // só a primeira janela da fatia (ver o resumo)
        if (prompt.Length > 0)
        {
            builder = builder.WithPrompt(prompt);
        }

        var segments = new List<RecognizedSegment>();
        await using var processor = builder.Build();
        await foreach (var segment in processor.ProcessAsync(window, cancellationToken))
        {
            segments.Add(Map(segment, offset, windowEnd));
        }

        return segments;
    }

    /// <summary>
    /// De onde a próxima fatia começa, pulando o trecho sem fala (ver o resumo), ou <c>null</c> se não houver
    /// mais fala para transcrever.
    /// </summary>
    private TimeSpan? SkipNonSpeech(SpeechActivity speech, TimeSpan offset, TimeSpan duration)
    {
        if (speech.NextSpeechFrom(offset, SpeechLeadIn) is not { } next)
        {
            LogSkipped(_logger, offset, duration);
            return null;
        }

        if (next - offset < MinimumSkip)
        {
            return offset; // pausa curta: segue de onde estava
        }

        LogSkipped(_logger, offset, next);
        return duration - next >= MinimumRemainder ? next : null;
    }

    /// <summary>
    /// Onde a próxima fatia começa: no fim do último trecho (janela sem fala, ou sem progresso, é pulada inteira).
    /// Exceção: se o último trecho termina bem depois da sua última palavra e o VAD ouviu fala nessa sobra, o modelo
    /// pulou fala, e a próxima fatia começa no fim da última palavra (ver o resumo).
    /// </summary>
    private TimeSpan NextOffset(
        List<RecognizedSegment> slice, TimeSpan offset, TimeSpan windowEnd, TimeSpan lastEnd, SpeechActivity? speech)
    {
        if (lastEnd <= offset)
        {
            return windowEnd;
        }

        if (speech is not null && slice.Count > 0
            && TokenWordMerger.LastAlignedWordEnd(slice[^1]) is { } wordEnd
            && wordEnd > offset + MinimumRemainder
            && speech.SpeechBetween(wordEnd, lastEnd) >= MinimumSkippedSpeech)
        {
            LogSkippedSpeech(_logger, wordEnd, lastEnd);
            return wordEnd;
        }

        return lastEnd;
    }

    /// <summary>
    /// Indica se a fatia de <paramref name="offset"/> a <paramref name="sliceEnd"/> só tem fala que a janela anterior,
    /// até <paramref name="heardUntil"/>, já continha (ver o resumo).
    /// </summary>
    private static bool IsAlreadyHeard(SpeechActivity? speech, TimeSpan offset, TimeSpan sliceEnd, TimeSpan heardUntil) =>
        speech is not null
        && offset < heardUntil
        && (sliceEnd <= heardUntil || speech.SpeechBetween(heardUntil, sliceEnd) == TimeSpan.Zero);

    private static string TextOf(List<RecognizedSegment> segments) => string.Concat(segments.Select(s => s.Text));

    /// <summary>Cabeças de atenção usadas pelo DTW; os modelos .en têm as suas próprias.</summary>
    private static WhisperAlignmentHeadsPreset ToHeadsPreset(WhisperModelSize size, bool englishOnly) => (size, englishOnly) switch
    {
        (WhisperModelSize.Tiny, false) => WhisperAlignmentHeadsPreset.Tiny,
        (WhisperModelSize.Tiny, true) => WhisperAlignmentHeadsPreset.TinyEn,
        (WhisperModelSize.Base, false) => WhisperAlignmentHeadsPreset.Base,
        (WhisperModelSize.Base, true) => WhisperAlignmentHeadsPreset.BaseEn,
        (WhisperModelSize.Small, false) => WhisperAlignmentHeadsPreset.Small,
        (WhisperModelSize.Small, true) => WhisperAlignmentHeadsPreset.SmallEn,
        (WhisperModelSize.Medium, false) => WhisperAlignmentHeadsPreset.Medium,
        (WhisperModelSize.Medium, true) => WhisperAlignmentHeadsPreset.MediumEn,
        (WhisperModelSize.LargeV3Turbo, false) => WhisperAlignmentHeadsPreset.LargeV3Turbo,
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

    /// <summary>
    /// Fim da fatia: uma janela depois do início ou, com detecção de voz, o fim da fala contínua mais
    /// <see cref="SpeechLeadIn"/>, se vier antes (ver o resumo). Nunca menos que <see cref="MinimumRemainder"/>.
    /// </summary>
    private static TimeSpan SliceEnd(AudioSamples audio, TimeSpan offset, SpeechActivity? speech)
    {
        var end = offset + WindowLength < audio.Duration ? offset + WindowLength : audio.Duration;
        // Com a mesma folga de SkipNonSpeech: um início logo depois do fim de uma fala ainda pertence a ela.
        if (speech?.EndOfSpeechFrom(offset - SpeechLeadIn, MinimumCutPause, end) is not { } speechEnd)
        {
            return end;
        }

        var trimmed = speechEnd + SpeechLeadIn;
        var minimum = offset + MinimumRemainder;
        return trimmed < minimum ? minimum : trimmed < end ? trimmed : end;
    }

    private static ReadOnlyMemory<float> SliceWindow(AudioSamples audio, TimeSpan offset, TimeSpan end)
    {
        var start = (int)Math.Min(audio.Samples.Length, Math.Round(offset.TotalSeconds * audio.SampleRate));
        var length = (int)Math.Min(audio.Samples.Length - start, Math.Round((end - offset).TotalSeconds * audio.SampleRate));
        return audio.Samples.Slice(start, Math.Max(0, length));
    }

    private static string Tail(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[^maxLength..];

    private static TimeSpan FromWhisperUnits(long value) => TimeSpan.FromTicks(Math.Max(0, value) * TicksPerWhisperUnit);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transcrevendo {Duration} com modelo {ModelSize}, idioma {Language}")]
    private static partial void LogStarted(ILogger logger, TimeSpan duration, WhisperModelSize modelSize, string language);

    [LoggerMessage(Level = LogLevel.Information, Message = "Modelo carregado: {ModelPath} (runtime {Runtime}, GPU solicitada: {UseGpu})")]
    private static partial void LogFactoryCreated(ILogger logger, string modelPath, RuntimeLibrary? runtime, bool useGpu);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A transcrição não parou em {Timeout}; o modelo não será liberado")]
    private static partial void LogDisposeTimedOut(ILogger logger, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Repetição em {Offset} (razão de compressão {Before:F1}); sem o prompt: {After:F1}")]
    private static partial void LogRepetition(ILogger logger, TimeSpan offset, double before, double after);

    [LoggerMessage(Level = LogLevel.Information, Message = "O modelo pulou fala entre {From} e {To}; a próxima fatia recomeça em {From}")]
    private static partial void LogSkippedSpeech(ILogger logger, TimeSpan from, TimeSpan to);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sobra de {From} a {To} já ouvida pela janela anterior: não é decodificada")]
    private static partial void LogAlreadyHeard(ILogger logger, TimeSpan from, TimeSpan to);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fatia {From} a {To} ({Speech} de fala): {Text}")]
    private static partial void LogSlice(ILogger logger, TimeSpan from, TimeSpan to, TimeSpan? speech, string text);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sem fala de {From} a {To}: trecho pulado")]
    private static partial void LogSkipped(ILogger logger, TimeSpan from, TimeSpan to);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao iniciar com GPU; usando CPU")]
    private static partial void LogGpuFallback(ILogger logger, Exception exception);
}
