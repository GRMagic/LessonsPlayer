using LessonsPlayer.Infrastructure.Whisper;
using Microsoft.Extensions.Logging;

namespace LessonsPlayer.Integration.Tests.Support;

/// <summary>Fatia de áudio decodificada pelo Whisper, como registrada no log de depuração do transcritor.</summary>
internal readonly record struct DecodedSlice(TimeSpan From, TimeSpan To, string Text);

/// <summary>
/// Logger que guarda as fatias que o <see cref="WhisperNetTranscriber"/> decodificou (mensagem de depuração
/// "Fatia {From} a {To} ..."), para os testes verificarem como o áudio foi fatiado.
/// </summary>
internal sealed class SliceLog : ILogger<WhisperNetTranscriber>
{
    private readonly List<DecodedSlice> _slices = [];

    public IReadOnlyList<DecodedSlice> Slices => _slices;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (state is not IReadOnlyList<KeyValuePair<string, object?>> values
            || !values.Any(v => v.Key == "{OriginalFormat}" && v.Value is string format && format.StartsWith("Fatia ", StringComparison.Ordinal)))
        {
            return;
        }

        var fields = values.ToDictionary(v => v.Key, v => v.Value);
        _slices.Add(new DecodedSlice((TimeSpan)fields["From"]!, (TimeSpan)fields["To"]!, (string?)fields["Text"] ?? string.Empty));
    }
}
