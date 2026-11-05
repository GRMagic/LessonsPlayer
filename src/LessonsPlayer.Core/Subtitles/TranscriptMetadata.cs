namespace LessonsPlayer.Core.Subtitles;

/// <summary>Informações sobre a origem da legenda, gravadas no cabeçalho do arquivo.</summary>
public sealed record TranscriptMetadata(
    string? AudioFileName = null,
    string? Model = null,
    string? Language = null,
    DateTimeOffset? CreatedAt = null)
{
    public static TranscriptMetadata Empty { get; } = new();
}
