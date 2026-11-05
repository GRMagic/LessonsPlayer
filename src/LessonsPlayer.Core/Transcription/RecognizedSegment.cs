using System.Collections.Immutable;

namespace LessonsPlayer.Core.Transcription;

/// <summary>
/// Token reconhecido pelo motor de transcrição (pedaço de palavra, pontuação ou token especial).
/// </summary>
/// <param name="Text">Texto do token; tokens que começam uma palavra trazem um espaço inicial.</param>
/// <param name="Start">Início estimado pelo decodificador (menos preciso que o alinhamento).</param>
/// <param name="End">Fim estimado pelo decodificador.</param>
/// <param name="AlignedEnd">
/// Fim do token segundo o alinhamento DTW, ou <c>null</c> se indisponível. O whisper.cpp grava no
/// <c>t_dtw</c> de cada token o instante em que o alinhamento passa para o token seguinte, ou seja,
/// o fim deste token, que coincide com o início do próximo.
/// </param>
public sealed record RecognizedToken(string Text, TimeSpan Start, TimeSpan End, TimeSpan? AlignedEnd);

/// <summary>Trecho reconhecido, independente da biblioteca de transcrição usada.</summary>
public sealed record RecognizedSegment(
    TimeSpan Start,
    TimeSpan End,
    string Text,
    ImmutableArray<RecognizedToken> Tokens,
    string? Language = null);
