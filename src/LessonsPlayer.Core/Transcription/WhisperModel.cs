namespace LessonsPlayer.Core.Transcription;

/// <summary>Arquitetura do modelo Whisper (define, por exemplo, as cabeças de alinhamento do DTW).</summary>
public enum WhisperModelSize
{
    Tiny,
    Base,
    Small,
    Medium,
    LargeV3Turbo,
}

/// <summary>Um modelo Whisper no formato ggml disponível para download.</summary>
/// <param name="Id">Identificador estável, gravado nas configurações e na legenda (ex.: <c>small-q5_1</c>).</param>
/// <param name="Size">Arquitetura do modelo.</param>
/// <param name="FileName">Nome do arquivo no repositório de modelos.</param>
/// <param name="SizeBytes">Tamanho do arquivo, para exibir e validar o download.</param>
/// <param name="EnglishOnly">Modelo treinado só em inglês (<c>.en</c>): melhor em inglês, não transcreve outros idiomas.</param>
public sealed record WhisperModel(string Id, WhisperModelSize Size, string FileName, long SizeBytes, bool EnglishOnly = false);

/// <summary>Catálogo dos modelos suportados (quantizados: menores e mais rápidos, com pouca perda de qualidade).</summary>
public static class WhisperModels
{
    public static WhisperModel Tiny { get; } = new("tiny-q5_1", WhisperModelSize.Tiny, "ggml-tiny-q5_1.bin", 32_152_673);

    public static WhisperModel Base { get; } = new("base-q5_1", WhisperModelSize.Base, "ggml-base-q5_1.bin", 59_707_625);

    public static WhisperModel BaseEnglish { get; } =
        new("base.en-q5_1", WhisperModelSize.Base, "ggml-base.en-q5_1.bin", 59_721_011, EnglishOnly: true);

    public static WhisperModel Small { get; } = new("small-q5_1", WhisperModelSize.Small, "ggml-small-q5_1.bin", 190_085_487);

    public static WhisperModel SmallEnglish { get; } =
        new("small.en-q5_1", WhisperModelSize.Small, "ggml-small.en-q5_1.bin", 190_098_681, EnglishOnly: true);

    public static WhisperModel Medium { get; } = new("medium-q5_0", WhisperModelSize.Medium, "ggml-medium-q5_0.bin", 539_212_467);

    public static WhisperModel MediumEnglish { get; } =
        new("medium.en-q5_0", WhisperModelSize.Medium, "ggml-medium.en-q5_0.bin", 539_225_533, EnglishOnly: true);

    public static WhisperModel LargeV3Turbo { get; } =
        new("large-v3-turbo-q5_0", WhisperModelSize.LargeV3Turbo, "ggml-large-v3-turbo-q5_0.bin", 574_041_195);

    /// <summary>Bom equilíbrio entre qualidade e velocidade em CPU, em qualquer idioma.</summary>
    public static WhisperModel Default => Small;

    public static IReadOnlyList<WhisperModel> All { get; } =
        [Tiny, Base, BaseEnglish, Small, SmallEnglish, Medium, MediumEnglish, LargeV3Turbo];

    public static WhisperModel FindOrDefault(string? id) =>
        All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;
}
