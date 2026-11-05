using System.Collections.Immutable;

namespace LessonsPlayer.Core.Transcription;

/// <summary>
/// Token reconhecido pelo motor de transcrição (pedaço de palavra, pontuação ou token especial).
/// </summary>
/// <param name="Text">Texto do token; tokens que começam uma palavra trazem um espaço inicial.</param>
/// <param name="Start">Início estimado pelo decodificador.</param>
/// <param name="End">Fim estimado pelo decodificador.</param>
/// <param name="AlignedStart">Início obtido pelo alinhamento DTW, mais preciso; <c>null</c> se indisponível.</param>
public sealed record RecognizedToken(string Text, TimeSpan Start, TimeSpan End, TimeSpan? AlignedStart);

/// <summary>Trecho reconhecido, independente da biblioteca de transcrição usada.</summary>
public sealed record RecognizedSegment(
    TimeSpan Start,
    TimeSpan End,
    string Text,
    ImmutableArray<RecognizedToken> Tokens,
    string? Language = null);
