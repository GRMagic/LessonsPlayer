using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LessonsPlayer.Core.Subtitles;

/// <summary>
/// Lê e grava o formato de legenda próprio do LessonsPlayer (texto puro, editável no Bloco de Notas):
/// <code>
/// # LessonsPlayer legenda v1
/// # audio: aula01.mp3
/// # modelo: small-q5_1
/// # idioma: pt
/// # gerado: 2026-11-04T10:15:00-03:00
///
/// [00:00:01.240 --> 00:00:04.880] Olá pessoal, bem-vindos.
/// 00:00:01.240 00:00:01.610 Olá
/// 00:00:01.610 00:00:02.150 pessoal,
/// 00:00:02.150 00:00:04.880 bem-vindos.
/// </code>
/// As linhas de palavra são a fonte da verdade. Um segmento escrito à mão, sem linhas de palavra,
/// tem o tempo distribuído entre as palavras do texto do cabeçalho.
/// </summary>
public static partial class SubtitleTxtFormat
{
    internal const string Signature = "# LessonsPlayer legenda v1";

    private const string NewLine = "\r\n";
    private const string KeyAudio = "audio";
    private const string KeyModel = "modelo";
    private const string KeyLanguage = "idioma";
    private const string KeyCreated = "gerado";

    public static ParseResult Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var warnings = ImmutableArray.CreateBuilder<ParseWarning>();
        var segments = new List<Segment>();
        var metadata = TranscriptMetadata.Empty;
        PendingSegment? pending = null;

        var lines = content.ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == '#')
            {
                metadata = ApplyMetadata(metadata, line);
                continue;
            }

            if (SegmentHeaderRegex().Match(line) is { Success: true } header)
            {
                pending?.Flush(segments, warnings);
                pending = null;

                if (TryParseTime(header.Groups["start"].Value, out var start)
                    && TryParseTime(header.Groups["end"].Value, out var end))
                {
                    pending = new PendingSegment(lineNumber, start, end, header.Groups["text"].Value);
                }
                else
                {
                    warnings.Add(new ParseWarning(lineNumber, ParseWarningKind.UnrecognizedLine));
                }

                continue;
            }

            if (WordLineRegex().Match(line) is { Success: true } wordLine
                && TryParseTime(wordLine.Groups["start"].Value, out var wordStart)
                && TryParseTime(wordLine.Groups["end"].Value, out var wordEnd))
            {
                if (pending is null)
                {
                    warnings.Add(new ParseWarning(lineNumber, ParseWarningKind.WordOutsideSegment));
                }
                else
                {
                    pending.AddWord(lineNumber, wordStart, wordEnd, wordLine.Groups["text"].Value, warnings);
                }

                continue;
            }

            warnings.Add(new ParseWarning(lineNumber, ParseWarningKind.UnrecognizedLine));
        }

        pending?.Flush(segments, warnings);

        // OrderBy é estável: segmentos com o mesmo início mantêm a ordem do arquivo.
        var transcript = new Transcript(metadata, segments.OrderBy(s => s.Start));
        return new ParseResult(transcript, warnings.ToImmutable());
    }

    public static string Serialize(Transcript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        var sb = new StringBuilder();
        sb.Append(Signature).Append(NewLine);

        var meta = transcript.Metadata;
        AppendMetadata(sb, KeyAudio, meta.AudioFileName);
        AppendMetadata(sb, KeyModel, meta.Model);
        AppendMetadata(sb, KeyLanguage, meta.Language);
        AppendMetadata(sb, KeyCreated, meta.CreatedAt?.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture));

        foreach (var segment in transcript.Segments)
        {
            sb.Append(NewLine);
            sb.Append('[').Append(FormatTime(segment.Start)).Append(" --> ").Append(FormatTime(segment.End)).Append("] ")
              .Append(segment.Text).Append(NewLine);

            foreach (var word in segment.Words)
            {
                sb.Append(FormatTime(word.Start)).Append(' ').Append(FormatTime(word.End)).Append(' ')
                  .Append(word.Text).Append(NewLine);
            }
        }

        return sb.ToString();
    }

    /// <summary>Formata como <c>hh:mm:ss.fff</c> (milissegundos arredondados).</summary>
    public static string FormatTime(TimeSpan time)
    {
        var totalMs = (long)Math.Round(Math.Max(0, time.TotalMilliseconds), MidpointRounding.AwayFromZero);
        var hours = totalMs / 3_600_000;
        var minutes = totalMs / 60_000 % 60;
        var seconds = totalMs / 1000 % 60;
        var ms = totalMs % 1000;
        return string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes:00}:{seconds:00}.{ms:000}");
    }

    /// <summary>Aceita <c>hh:mm:ss.fff</c>, <c>mm:ss.fff</c> ou <c>mm:ss</c>, com ponto ou vírgula decimal.</summary>
    public static bool TryParseTime(string text, out TimeSpan time)
    {
        time = TimeSpan.Zero;
        var match = TimeRegex().Match(text.Trim());
        if (!match.Success)
        {
            return false;
        }

        var hours = match.Groups["h"].Success ? int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture) : 0;
        var minutes = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);
        var fraction = match.Groups["f"].Success
            ? int.Parse(match.Groups["f"].Value.PadRight(3, '0'), CultureInfo.InvariantCulture)
            : 0;

        if (minutes >= 60 || seconds >= 60)
        {
            return false;
        }

        time = new TimeSpan(0, hours, minutes, seconds, fraction);
        return true;
    }

    private static TranscriptMetadata ApplyMetadata(TranscriptMetadata metadata, string line)
    {
        var match = MetadataRegex().Match(line);
        if (!match.Success)
        {
            return metadata; // comentário livre (ex.: a assinatura do formato)
        }

        var value = match.Groups["value"].Value.Trim();
        if (value.Length == 0)
        {
            return metadata;
        }

        return match.Groups["key"].Value.Trim().ToLowerInvariant() switch
        {
            KeyAudio => metadata with { AudioFileName = value },
            KeyModel or "model" => metadata with { Model = value },
            KeyLanguage or "language" => metadata with { Language = value },
            KeyCreated or "created" when DateTimeOffset.TryParse(
                value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var created)
                => metadata with { CreatedAt = created },
            _ => metadata,
        };
    }

    private static void AppendMetadata(StringBuilder sb, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            sb.Append("# ").Append(key).Append(": ").Append(value.ReplaceLineEndings(" ")).Append(NewLine);
        }
    }

    [GeneratedRegex(@"^\[\s*(?<start>[^\]]+?)\s*-->\s*(?<end>[^\]]+?)\s*\]\s*(?<text>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex SegmentHeaderRegex();

    [GeneratedRegex(@"^(?<start>\S+)\s+(?<end>\S+)\s+(?<text>\S.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex WordLineRegex();

    [GeneratedRegex(@"^#\s*(?<key>[^:]+?)\s*:(?<value>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex MetadataRegex();

    [GeneratedRegex(@"^(?:(?<h>\d+):)?(?<m>\d{1,2}):(?<s>\d{1,2})(?:[.,](?<f>\d{1,3}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex TimeRegex();

    /// <summary>Segmento sendo montado durante a leitura.</summary>
    private sealed class PendingSegment(int lineNumber, TimeSpan start, TimeSpan end, string headerText)
    {
        private readonly List<Word> _words = [];

        public void AddWord(
            int wordLineNumber, TimeSpan wordStart, TimeSpan wordEnd, string text,
            ImmutableArray<ParseWarning>.Builder warnings)
        {
            if (wordEnd < wordStart)
            {
                warnings.Add(new ParseWarning(wordLineNumber, ParseWarningKind.InvalidTimeRange));
                wordEnd = wordStart;
            }

            // Uma linha com mais de uma palavra é dividida, repartindo o tempo dela.
            _words.AddRange(TimeDistribution.Distribute(wordStart, wordEnd, TextTokens.Split(text)));
        }

        public void Flush(List<Segment> segments, ImmutableArray<ParseWarning>.Builder warnings)
        {
            if (_words.Count > 0)
            {
                segments.Add(new Segment(_words.OrderBy(w => w.Start)));
                return;
            }

            var tokens = TextTokens.Split(headerText);
            if (tokens.Length == 0)
            {
                warnings.Add(new ParseWarning(lineNumber, ParseWarningKind.EmptySegment));
                return;
            }

            var segmentEnd = end;
            if (segmentEnd < start)
            {
                warnings.Add(new ParseWarning(lineNumber, ParseWarningKind.InvalidTimeRange));
                segmentEnd = start;
            }

            segments.Add(new Segment(TimeDistribution.Distribute(start, segmentEnd, tokens)));
        }
    }
}
