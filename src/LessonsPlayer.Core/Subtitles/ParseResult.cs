using System.Collections.Immutable;

namespace LessonsPlayer.Core.Subtitles;

/// <summary>Tipo de problema encontrado ao ler um arquivo de legenda.</summary>
public enum ParseWarningKind
{
    /// <summary>Linha que não é comentário, cabeçalho de segmento nem palavra.</summary>
    UnrecognizedLine,

    /// <summary>Linha de palavra antes de qualquer cabeçalho de segmento.</summary>
    WordOutsideSegment,

    /// <summary>Segmento sem palavras e sem texto.</summary>
    EmptySegment,

    /// <summary>Intervalo com fim antes do início (foi corrigido).</summary>
    InvalidTimeRange,
}

/// <summary>Um problema não fatal encontrado na leitura, com a linha (base 1) onde ocorreu.</summary>
public readonly record struct ParseWarning(int LineNumber, ParseWarningKind Kind);

/// <summary>Resultado da leitura: a legenda obtida e os avisos sobre linhas ignoradas ou corrigidas.</summary>
public sealed record ParseResult(Transcript Transcript, ImmutableArray<ParseWarning> Warnings)
{
    public bool HasWarnings => !Warnings.IsDefaultOrEmpty;
}
