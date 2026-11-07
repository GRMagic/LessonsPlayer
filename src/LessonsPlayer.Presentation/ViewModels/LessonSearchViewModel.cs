using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Search;
using LessonsPlayer.Core.Subtitles;
using LessonsPlayer.Presentation.Formatting;
using LessonsPlayer.Presentation.Resources;
using Microsoft.Extensions.Logging;

namespace LessonsPlayer.Presentation.ViewModels;

/// <summary>Um trecho de legenda que casa com a busca.</summary>
public sealed class SubtitleMatchViewModel(PlaylistItemViewModel lesson, SearchMatch match)
{
    public PlaylistItemViewModel Lesson { get; } = lesson;

    public SearchMatch Match { get; } = match;

    public string LessonTitle => Lesson.Title;

    public string StartText => TimeFormat.Compact(Match.Start);

    public string Text => Match.Text;

    /// <summary>Também é o nome lido pelos leitores de tela.</summary>
    public override string ToString() => $"{LessonTitle}, {StartText}: {Text}";
}

/// <summary>
/// Busca de aulas: filtra a playlist pelo nome e procura o texto nas legendas salvas das aulas da pasta. As
/// legendas lidas ficam em memória (centenas de aulas cabem com folga) e são relidas quando uma muda
/// (<see cref="Invalidate"/>). Cada tecla cancela a busca anterior nas legendas.
/// </summary>
public sealed partial class LessonSearchViewModel : ObservableObject, IDisposable
{
    /// <summary>Limite de trechos mostrados: uma palavra comum ("the") casaria com quase tudo.</summary>
    public const int MaxSubtitleMatches = 200;

    private readonly PlaylistViewModel _playlist;
    private readonly ISubtitleStore _subtitles;
    private readonly ILogger<LessonSearchViewModel> _logger;
    private readonly ConcurrentDictionary<string, Transcript?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _search;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuery))]
    private string _query = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubtitleMatchesHeader))]
    private bool _isSearchingSubtitles;

    public LessonSearchViewModel(PlaylistViewModel playlist, ISubtitleStore subtitles, ILogger<LessonSearchViewModel> logger)
    {
        _playlist = playlist;
        _subtitles = subtitles;
        _logger = logger;
        _playlist.Reloaded += (_, _) =>
        {
            _cache.Clear();
            Refresh();
        };
    }

    public ObservableCollection<SubtitleMatchViewModel> SubtitleMatches { get; } = [];

    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);

    public string SubtitleMatchesHeader => IsSearchingSubtitles
        ? UiText.SearchingSubtitles
        : UiText.SubtitleMatches(SubtitleMatches.Count, SubtitleMatches.Count >= MaxSubtitleMatches);

    /// <summary>A busca nas legendas em andamento (ou a última). Os testes esperam por ela.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>A legenda desta aula mudou (salva, gerada ou excluída): a próxima busca relê o arquivo.</summary>
    public void Invalidate(string audioFilePath)
    {
        _cache.TryRemove(audioFilePath, out _);
        if (HasQuery)
        {
            Refresh();
        }
    }

    public void Dispose()
    {
        _search?.Cancel();
        _search?.Dispose();
        _search = null;
    }

    [RelayCommand]
    private void Clear() => Query = string.Empty;

    partial void OnQueryChanged(string value) => Refresh();

    private void Refresh()
    {
        var query = SearchQuery.Parse(Query);
        _playlist.Filter(query);

        _search?.Cancel();
        _search?.Dispose();
        _search = new CancellationTokenSource();
        Completion = SearchSubtitlesAsync(query, _search.Token);
    }

    private async Task SearchSubtitlesAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        SubtitleMatches.Clear();
        IsSearchingSubtitles = !query.IsEmpty;
        OnPropertyChanged(nameof(SubtitleMatchesHeader));
        if (query.IsEmpty)
        {
            return;
        }

        try
        {
            foreach (var lesson in _playlist.Items.Where(i => i.HasSubtitle).ToList())
            {
                var transcript = await LoadAsync(lesson.FilePath, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (transcript is null)
                {
                    continue;
                }

                foreach (var match in query.FindIn(transcript, MaxSubtitleMatches - SubtitleMatches.Count))
                {
                    SubtitleMatches.Add(new SubtitleMatchViewModel(lesson, match));
                }

                if (SubtitleMatches.Count >= MaxSubtitleMatches)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return; // outra tecla: a busca nova cuida da lista
        }

        IsSearchingSubtitles = false;
        OnPropertyChanged(nameof(SubtitleMatchesHeader));
    }

    private async Task<Transcript?> LoadAsync(string audioFilePath, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(audioFilePath, out var cached))
        {
            return cached;
        }

        Transcript? transcript = null;
        try
        {
            // Ler e interpretar o arquivo fora da UI: na primeira busca, são todas as legendas da pasta.
            var result = await Task.Run(() => _subtitles.LoadAsync(audioFilePath, cancellationToken), cancellationToken);
            transcript = result?.Transcript;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogLoadFailed(_logger, ex, audioFilePath); // uma legenda ilegível não impede a busca nas outras
        }

        _cache[audioFilePath] = transcript;
        return transcript;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Busca: não foi possível ler a legenda de {AudioFilePath}")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception, string audioFilePath);
}
