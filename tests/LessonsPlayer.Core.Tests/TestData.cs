using LessonsPlayer.Core.Subtitles;

namespace LessonsPlayer.Core.Tests;

/// <summary>Atalhos para montar legendas nos testes (tempos em milissegundos).</summary>
internal static class TestData
{
    public static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    public static Word W(int startMs, int endMs, string text) => new(Ms(startMs), Ms(endMs), text);

    public static Segment Seg(params Word[] words) => new(words);

    public static Transcript Tr(params Segment[] segments) => new(TranscriptMetadata.Empty, segments);

    /// <summary>Legenda com dois trechos: "Olá pessoal" (0–1000) e "bem vindos" (1500–2500).</summary>
    public static Transcript Sample() => Tr(
        Seg(W(0, 400, "Olá"), W(400, 1000, "pessoal")),
        Seg(W(1500, 2000, "bem"), W(2000, 2500, "vindos")));
}
