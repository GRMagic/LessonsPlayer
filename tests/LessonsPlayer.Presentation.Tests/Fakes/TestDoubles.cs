using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Settings;
using LessonsPlayer.Core.Subtitles;
using LessonsPlayer.Presentation.Services;

namespace LessonsPlayer.Presentation.Tests.Fakes;

/// <summary>Executa na hora (nos testes tudo roda na mesma thread).</summary>
internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

/// <summary>Timer acionado manualmente pelo teste.</summary>
internal sealed class ManualFrameTimer : IFrameTimer
{
    public event EventHandler? Tick;

    public bool IsEnabled { get; set; }

    public void Fire() => Tick?.Invoke(this, EventArgs.Empty);
}

/// <summary>Preferências em memória.</summary>
internal sealed class InMemorySettingsStore : ISettingsStore
{
    public AppSettings Stored { get; private set; } = AppSettings.Default;

    public int SaveCount { get; private set; }

    public AppSettings Load() => Stored;

    public void Save(AppSettings settings)
    {
        Stored = settings;
        SaveCount++;
    }
}

/// <summary>Legendas em memória, indexadas pelo caminho do áudio.</summary>
internal sealed class InMemorySubtitleStore : ISubtitleStore
{
    public Dictionary<string, Transcript> Saved { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool FailOnSave { get; set; }

    public string GetSubtitlePath(string audioFilePath) => Path.ChangeExtension(audioFilePath, ".subtitle.txt");

    public bool Exists(string audioFilePath) => Saved.ContainsKey(audioFilePath);

    public Task<ParseResult?> LoadAsync(string audioFilePath, CancellationToken cancellationToken) =>
        Task.FromResult(Saved.TryGetValue(audioFilePath, out var transcript)
            ? new ParseResult(transcript, [])
            : null);

    public Task SaveAsync(string audioFilePath, Transcript transcript, CancellationToken cancellationToken)
    {
        if (FailOnSave)
        {
            throw new IOException("Disk full");
        }

        Saved[audioFilePath] = transcript;
        return Task.CompletedTask;
    }

    public bool FailOnDelete { get; set; }

    public bool Delete(string audioFilePath) =>
        FailOnDelete ? throw new UnauthorizedAccessException("Read-only") : Saved.Remove(audioFilePath);
}

/// <summary>Registra os pedidos de energia ativos (adquiridos e ainda não descartados).</summary>
internal sealed class RecordingKeepAwake : IKeepAwake
{
    private readonly Lock _lock = new();
    private readonly List<KeepAwakeReason> _active = [];

    public IReadOnlyList<KeepAwakeReason> Active
    {
        get
        {
            lock (_lock)
            {
                return [.. _active];
            }
        }
    }

    public IDisposable Acquire(KeepAwakeReason reason)
    {
        lock (_lock)
        {
            _active.Add(reason);
        }

        return new Request(this, reason);
    }

    private sealed class Request(RecordingKeepAwake owner, KeepAwakeReason reason) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                lock (owner._lock)
                {
                    owner._active.Remove(reason);
                }
            }
        }
    }
}
