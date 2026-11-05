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
/// <c>t_dtw</c> do token i o instante em que o caminho do DTW entra no token i+1, ou seja, o fim do
/// token i. É um índice deslocado em relação ao <c>start_times</c> da implementação da OpenAI
/// (whisper/timing.py); ver <c>whisper_exp_compute_token_level_timestamps_dtw</c> em src/whisper.cpp,
/// onde <c>last_v</c> começa em 0 e a entrada na primeira linha é descartada. Resolução de 20 ms.
/// </param>
public sealed record RecognizedToken(string Text, TimeSpan Start, TimeSpan End, TimeSpan? AlignedEnd);

/// <summary>Trecho reconhecido, independente da biblioteca de transcrição usada.</summary>
public sealed record RecognizedSegment(
    TimeSpan Start,
    TimeSpan End,
    string Text,
    ImmutableArray<RecognizedToken> Tokens,
    string? Language = null);
