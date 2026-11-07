using System.Globalization;
using LessonsPlayer.Core.Audio;
using LessonsPlayer.Core.Subtitles;
using LessonsPlayer.Core.Transcription;

namespace LessonsPlayer.Presentation.Resources;

/// <summary>
/// Textos da interface (em inglês) num só lugar. Os ViewModels usam os métodos de formatação e o XAML
/// usa as constantes via <c>{x:Static}</c>.
/// </summary>
public static class UiText
{
    /// <summary>
    /// Cultura dos números e porcentagens: a da interface, e não a do Windows (num Windows em português,
    /// "1.234 MB" e "45 %" apareceriam no meio do texto em inglês).
    /// </summary>
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    public const string AppName = "LessonsPlayer";

    // --- Barra de ferramentas ---
    public const string OpenFolder = "Open folder";
    public const string OpenFile = "Open file";
    public const string GenerateSubtitle = "Generate subtitles";
    public const string Cancel = "Cancel";
    public const string Model = "Model";
    public const string Language = "Language";
    public const string EnglishOnlyModelHint = "The .en models only transcribe English; the language setting doesn't apply.";

    // --- Player ---
    public const string Play = "Play";
    public const string Pause = "Pause";
    public const string PlayPauseHint = "Play / pause (Space)";
    public const string PreviousHint = "Previous track (Ctrl+←)";
    public const string NextHint = "Next track (Ctrl+→)";
    public const string VolumeHint = "Volume";
    public const string PositionHint = "Position";
    public const string AutoAdvance = "Autoplay next";
    public const string AutoAdvanceHint = "When a lesson ends, start the next one in the playlist";

    // --- Playlist / legenda ---
    public const string PlaylistEmpty = "Open a folder or an audio file\n(or drag it here).";
    public const string HasSubtitleHint = "Has saved subtitles";
    public const string DeleteSubtitle = "Delete subtitles…";
    public const string TranscriptEmpty = "This audio has no subtitles yet.\nUse \"Generate subtitles\" to create them.";
    public const string NoTrack = "No audio open.";
    public const string FollowAudio = "Follow audio";
    public const string SeekToSegmentHint = "Go to the start of this sentence";

    // --- Busca ---
    public const string SearchPlaceholder = "Search lessons and subtitles";
    public const string SearchHint = "Search lesson names and what is said in the subtitles (Ctrl+F)";
    public const string ClearSearch = "Clear search (Esc)";
    public const string NoLessonNamesMatch = "No lesson names match.";
    public const string SearchingSubtitles = "Searching the subtitles…";

    public static string SubtitleMatches(int count, bool limited) => count switch
    {
        0 => "No matches in the subtitles.",
        1 => "1 match in the subtitles",
        _ when limited => string.Create(Culture, $"First {count:N0} matches in the subtitles"),
        _ => string.Create(Culture, $"{count:N0} matches in the subtitles"),
    };
    public const string Save = "Save";
    public const string Undo = "Undo";
    public const string Redo = "Redo";

    // --- Menu de edição ---
    public const string EditWord = "Edit word (F2)";
    public const string EditSegment = "Edit sentence… (Ctrl+E)";
    public const string SetStartHere = "Start = current position ( [ )";
    public const string SetEndHere = "End = current position ( ] )";
    public const string NudgeStartEarlier = "Start −50 ms (Alt+←)";
    public const string NudgeStartLater = "Start +50 ms (Alt+→)";
    public const string NudgeEndEarlier = "End −50 ms (Alt+Shift+←)";
    public const string NudgeEndLater = "End +50 ms (Alt+Shift+→)";
    public const string AdjustTiming = "Adjust timing";
    public const string DeleteWord = "Delete word (Del)";
    public const string SplitHere = "Split sentence here (Ctrl+D)";
    public const string MergeWithPrevious = "Merge with previous sentence";
    public const string MergeWithNext = "Merge with next sentence (Ctrl+J)";
    public const string EditSegmentTitle = "Edit sentence";

    // --- Diálogos ---
    public const string ErrorTitle = "Couldn't complete the action";
    public const string OverwriteTitle = "Replace subtitles?";
    public const string DeleteSubtitleTitle = "Delete subtitles?";
    public const string SaveChangesTitle = "Save changes?";
    public const string AudioFilesFilterName = "Audio files";

    // Botões com o verbo da ação, e não "Yes/No": a MessageBox do Windows usaria o idioma do sistema.
    public const string Ok = "OK";
    public const string Replace = "Replace";
    public const string Delete = "Delete";
    public const string DontSave = "Don't save";

    public const string LanguageAuto = "Auto-detect";

    public const string UnsupportedAudioFile = "The file isn't a supported audio format, or it's damaged.";

    public const string AccessDenied = "Windows didn't allow access to this file or folder.";
    public const string PathNotFound = "The file or folder no longer exists.";
    public const string FileInUse = "The file is being used by another program.";

    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);

    /// <summary>
    /// Motivo de uma falha para mostrar ao usuário. As mensagens do sistema citam arquivos internos (ex.: o
    /// <c>.tmp</c> da gravação), então os casos comuns têm texto próprio.
    /// </summary>
    public static string ErrorReason(Exception exception) => exception switch
    {
        UnsupportedAudioFileException => UnsupportedAudioFile,
        UnauthorizedAccessException or System.Security.SecurityException => AccessDenied,
        FileNotFoundException or DirectoryNotFoundException => PathNotFound,
        IOException { HResult: SharingViolation or LockViolation } => FileInUse,
        _ => exception.Message,
    };

    public static string GeneratedButNotSaved(string reason) =>
        $"The subtitles were generated, but they couldn't be saved next to the audio.\n\n{reason}\n\nThey stay open, marked as unsaved (•).";

    public static string WindowTitle(string? fileName, bool isDirty) =>
        fileName is null ? AppName : $"{(isDirty ? "• " : string.Empty)}{fileName} — {AppName}";

    public static string OverwriteMessage(string fileName) =>
        $"\"{fileName}\" already has subtitles. Generating them again replaces the current ones, including your edits.";

    public static string DeleteSubtitleMessage(string fileName, string subtitlePath, bool discardsChanges) =>
        $"The subtitles for \"{fileName}\" ({Path.GetFileName(subtitlePath)}) will be deleted. This can't be undone." +
        (discardsChanges ? "\n\nUnsaved changes will also be lost." : string.Empty);

    public static string SubtitleDeleted(string fileName) => $"Subtitles for {fileName} deleted.";

    public static string CannotDeleteSubtitle(string reason) => $"Couldn't delete the subtitles.\n\n{reason}";

    public static string SaveChangesMessage(string fileName) =>
        $"The subtitles for \"{fileName}\" have unsaved changes.";

    public static string CannotOpen(string fileName, string reason) => $"Couldn't open \"{fileName}\".\n\n{reason}";

    public static string CannotSave(string reason) => $"Couldn't save the subtitles.\n\n{reason}";

    public static string TranscriptionFailed(string reason) => $"Generating the subtitles failed.\n\n{reason}";

    public static string PlaybackFailed(string reason) => $"Playback stopped.\n\n{reason}";

    /// <summary>Resumo dos problemas da legenda lida, ou <c>null</c> se não houver nenhum.</summary>
    public static string? SubtitleWarnings(int ignoredLines, int correctedTimes)
    {
        var parts = new List<string>(2);
        if (ignoredLines > 0)
        {
            parts.Add(ignoredLines == 1
                ? "1 line of the subtitle file wasn't recognized and was ignored"
                : string.Create(Culture, $"{ignoredLines} lines of the subtitle file weren't recognized and were ignored"));
        }

        if (correctedTimes > 0)
        {
            parts.Add(correctedTimes == 1
                ? "1 time that ended before it started was fixed"
                : string.Create(Culture, $"{correctedTimes} times that ended before they started were fixed"));
        }

        return parts.Count == 0 ? null : string.Join("; ", parts) + ".";
    }

    public static string Saved(string path) => $"Subtitles saved to {Path.GetFileName(path)}.";

    public static string Progress(TranscriptionProgress progress) => progress.Stage switch
    {
        TranscriptionStage.DownloadingModel when progress.Fraction < 1 =>
            string.Create(Culture, $"Downloading model… {progress.Fraction:P0}"),
        TranscriptionStage.DownloadingModel => "Loading model…",
        TranscriptionStage.DecodingAudio => "Reading the audio…",
        _ => string.Create(Culture, $"Transcribing on {Device(progress.Gpu)}… {progress.Fraction:P0}"),
    };

    public static string TranscriptionFinished(string fileName, TimeSpan elapsed, int words, GpuDevice? gpu) =>
        string.Create(Culture, $"Subtitles for \"{fileName}\" generated on {Device(gpu)} in {elapsed:m\\:ss} ({words:N0} words).");

    /// <summary>Onde a transcrição roda: "the GPU (NVIDIA GeForce GTX 1650)" ou "the CPU".</summary>
    private static string Device(GpuDevice? gpu) => gpu is null ? "the CPU" : $"the GPU ({gpu.Name})";

    public static string NoSpeechFound(string fileName) =>
        $"No speech was recognized in \"{fileName}\"; the subtitles weren't saved.";

    public const string TranscriptionCanceled = "Subtitle generation canceled.";

    public static string WordTooltip(Word word) =>
        $"{Formatting.TimeFormat.Precise(word.Start)} → {Formatting.TimeFormat.Precise(word.End)}";

    public static string ModelName(WhisperModel model) => model.EnglishOnly
        ? model.Size switch
        {
            WhisperModelSize.Base => "Base.en — English only, fast",
            WhisperModelSize.Small => "Small.en — English only, balanced",
            WhisperModelSize.Medium => "Medium.en — English only, more accurate",
            _ => model.Id,
        }
        : model.Size switch
    {
        WhisperModelSize.Tiny => "Tiny — fastest",
        WhisperModelSize.Base => "Base — fast",
        WhisperModelSize.Small => "Small — balanced (recommended)",
        WhisperModelSize.Medium => "Medium — more accurate, slower",
        WhisperModelSize.LargeV3Turbo => "Large v3 Turbo — most accurate",
        _ => model.Id,
    };

    public static string ModelSize(WhisperModel model, bool isDownloaded) =>
        string.Create(Culture, $"{model.SizeBytes / 1_000_000.0:N0} MB{(isDownloaded ? " · downloaded" : string.Empty)}");

    /// <summary>Cada idioma pelo próprio nome ("Português", "English"), como nos seletores de idioma.</summary>
    public static string LanguageName(string code)
    {
        if (code == TranscriptionLanguages.Auto)
        {
            return LanguageAuto;
        }

        var culture = CultureInfo.GetCultureInfo(code);
        var name = culture.NativeName;
        return name.Length == 0 ? code : char.ToUpper(name[0], culture) + name[1..];
    }
}
