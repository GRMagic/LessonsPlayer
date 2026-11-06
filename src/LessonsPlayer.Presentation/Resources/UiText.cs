using System.Globalization;
using LessonsPlayer.Core.Audio;
using LessonsPlayer.Core.Subtitles;
using LessonsPlayer.Core.Transcription;

namespace LessonsPlayer.Presentation.Resources;

/// <summary>
/// Textos da interface (pt-BR) num só lugar. Os ViewModels usam os métodos de formatação e o XAML
/// usa as constantes via <c>{x:Static}</c>.
/// </summary>
public static class UiText
{
    public const string AppName = "LessonsPlayer";

    // --- Barra de ferramentas ---
    public const string OpenFolder = "Abrir pasta";
    public const string OpenFile = "Abrir arquivo";
    public const string GenerateSubtitle = "Gerar legenda";
    public const string Cancel = "Cancelar";
    public const string Model = "Modelo";
    public const string Language = "Idioma";
    public const string UseGpu = "Usar GPU";
    public const string UseGpuHint = "Usa a placa de vídeo quando disponível (Vulkan); sem ela, usa a CPU.";
    public const string EnglishOnlyModelHint = "Os modelos .en transcrevem só inglês; o idioma não se aplica.";

    // --- Player ---
    public const string Play = "Tocar";
    public const string Pause = "Pausar";
    public const string PlayPauseHint = "Tocar / pausar (Espaço)";
    public const string PreviousHint = "Faixa anterior (Ctrl+←)";
    public const string NextHint = "Próxima faixa (Ctrl+→)";
    public const string VolumeHint = "Volume";

    // --- Playlist / legenda ---
    public const string PlaylistEmpty = "Abra uma pasta ou um arquivo de áudio\n(ou arraste para cá).";
    public const string HasSubtitleHint = "Tem legenda salva";
    public const string TranscriptEmpty = "Este áudio ainda não tem legenda.\nUse \"Gerar legenda\" para criar uma.";
    public const string NoTrack = "Nenhum áudio aberto.";
    public const string FollowAudio = "Seguir áudio";
    public const string Save = "Salvar";
    public const string Undo = "Desfazer";
    public const string Redo = "Refazer";

    // --- Menu de edição ---
    public const string EditWord = "Corrigir palavra (F2)";
    public const string EditSegment = "Editar frase… (Ctrl+E)";
    public const string SetStartHere = "Início = posição atual ( [ )";
    public const string SetEndHere = "Fim = posição atual ( ] )";
    public const string NudgeStartEarlier = "Início −50 ms (Alt+←)";
    public const string NudgeStartLater = "Início +50 ms (Alt+→)";
    public const string NudgeEndEarlier = "Fim −50 ms (Alt+Shift+←)";
    public const string NudgeEndLater = "Fim +50 ms (Alt+Shift+→)";
    public const string AdjustTiming = "Ajustar tempo";
    public const string DeleteWord = "Excluir palavra (Del)";
    public const string SplitHere = "Dividir frase aqui (Ctrl+D)";
    public const string MergeWithPrevious = "Juntar com a frase anterior";
    public const string MergeWithNext = "Juntar com a próxima frase (Ctrl+J)";
    public const string EditSegmentTitle = "Editar frase";

    // --- Diálogos ---
    public const string ErrorTitle = "Não foi possível concluir";
    public const string OverwriteTitle = "Substituir legenda?";
    public const string SaveChangesTitle = "Salvar alterações?";
    public const string AudioFilesFilterName = "Arquivos de áudio";

    public const string LanguageAuto = "Detectar automaticamente";

    public const string UnsupportedAudioFile = "O arquivo não é um áudio suportado ou está danificado.";

    /// <summary>Motivo de uma falha para mostrar ao usuário (as mensagens do sistema vêm em inglês).</summary>
    public static string ErrorReason(Exception exception) => exception switch
    {
        UnsupportedAudioFileException => UnsupportedAudioFile,
        _ => exception.Message,
    };

    public static string WindowTitle(string? fileName, bool isDirty) =>
        fileName is null ? AppName : $"{(isDirty ? "• " : string.Empty)}{fileName} — {AppName}";

    public static string OverwriteMessage(string fileName) =>
        $"\"{fileName}\" já tem uma legenda. Gerar de novo substitui a legenda atual, incluindo as edições.";

    public static string SaveChangesMessage(string fileName) =>
        $"A legenda de \"{fileName}\" tem alterações não salvas.";

    public static string CannotOpen(string fileName, string reason) => $"Não foi possível abrir \"{fileName}\".\n\n{reason}";

    public static string CannotSave(string reason) => $"Não foi possível salvar a legenda.\n\n{reason}";

    public static string TranscriptionFailed(string reason) => $"A geração da legenda falhou.\n\n{reason}";

    public static string PlaybackFailed(string reason) => $"A reprodução foi interrompida.\n\n{reason}";

    public static string SubtitleWarnings(int count) =>
        count == 1 ? "1 linha da legenda não foi reconhecida e foi ignorada." : $"{count} linhas da legenda não foram reconhecidas e foram ignoradas.";

    public static string Saved(string path) => $"Legenda salva em {Path.GetFileName(path)}.";

    public static string Progress(TranscriptionProgress progress) => progress.Stage switch
    {
        TranscriptionStage.DownloadingModel when progress.Fraction < 1 =>
            string.Create(CultureInfo.CurrentCulture, $"Baixando modelo… {progress.Fraction:P0}"),
        TranscriptionStage.DownloadingModel => "Carregando modelo…",
        TranscriptionStage.DecodingAudio => "Lendo o áudio…",
        _ => string.Create(CultureInfo.CurrentCulture, $"Transcrevendo… {progress.Fraction:P0}"),
    };

    public static string TranscriptionFinished(string fileName, TimeSpan elapsed, int words) =>
        string.Create(CultureInfo.CurrentCulture, $"Legenda de \"{fileName}\" gerada em {elapsed:m\\:ss} ({words} palavras).");

    public static string NoSpeechFound(string fileName) =>
        $"Nenhuma fala reconhecida em \"{fileName}\"; a legenda não foi salva.";

    public const string TranscriptionCanceled = "Geração da legenda cancelada.";

    public static string WordTooltip(Word word) =>
        $"{Formatting.TimeFormat.Precise(word.Start)} → {Formatting.TimeFormat.Precise(word.End)}";

    public static string ModelName(WhisperModel model) => model.EnglishOnly
        ? model.Size switch
        {
            WhisperModelSize.Base => "Base.en — só inglês, rápido",
            WhisperModelSize.Small => "Small.en — só inglês, equilíbrio",
            WhisperModelSize.Medium => "Medium.en — só inglês, mais preciso",
            _ => model.Id,
        }
        : model.Size switch
    {
        WhisperModelSize.Tiny => "Tiny — o mais rápido",
        WhisperModelSize.Base => "Base — rápido",
        WhisperModelSize.Small => "Small — equilíbrio (recomendado)",
        WhisperModelSize.Medium => "Medium — mais preciso, mais lento",
        WhisperModelSize.LargeV3Turbo => "Large v3 Turbo — o mais preciso",
        _ => model.Id,
    };

    public static string ModelSize(WhisperModel model, bool isDownloaded) =>
        string.Create(CultureInfo.CurrentCulture,
            $"{model.SizeBytes / 1_000_000.0:N0} MB{(isDownloaded ? " · baixado" : string.Empty)}");

    public static string LanguageName(string code)
    {
        if (code == TranscriptionLanguages.Auto)
        {
            return LanguageAuto;
        }

        var name = CultureInfo.GetCultureInfo(code).NativeName;
        return name.Length == 0 ? code : char.ToUpper(name[0], CultureInfo.CurrentCulture) + name[1..];
    }
}
