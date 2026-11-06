using System.Collections.Immutable;

namespace LessonsPlayer.Core.Transcription;

/// <summary>Intervalo do áudio em que o detector de voz encontrou fala.</summary>
public readonly record struct SpeechRegion(TimeSpan Start, TimeSpan End);

/// <summary>
/// Onde há fala no áudio, segundo um detector de atividade de voz (VAD). As regiões ficam em ordem de início,
/// sem sobreposição: regiões que se tocam ou se sobrepõem são fundidas, e as vazias são descartadas.
/// </summary>
public sealed class SpeechActivity
{
    public SpeechActivity(IEnumerable<SpeechRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);

        var merged = new List<SpeechRegion>();
        foreach (var region in regions.Where(r => r.End > r.Start).OrderBy(r => r.Start))
        {
            if (merged.Count > 0 && region.Start <= merged[^1].End)
            {
                merged[^1] = merged[^1] with { End = region.End > merged[^1].End ? region.End : merged[^1].End };
            }
            else
            {
                merged.Add(region);
            }
        }

        Regions = [.. merged];
        TotalSpeech = merged.Aggregate(TimeSpan.Zero, (sum, r) => sum + (r.End - r.Start));
    }

    public ImmutableArray<SpeechRegion> Regions { get; }

    /// <summary>Soma da duração das regiões com fala.</summary>
    public TimeSpan TotalSpeech { get; }

    /// <summary>Indica se o detector encontrou alguma fala.</summary>
    public bool HasSpeech => !Regions.IsEmpty;

    /// <summary>
    /// Indica se <paramref name="time"/> está numa região de fala ampliada em <paramref name="margin"/> para os dois lados.
    /// </summary>
    public bool IsNearSpeech(TimeSpan time, TimeSpan margin)
    {
        var index = FirstRegionEndingAfter(time - margin);
        return index < Regions.Length && Regions[index].Start - margin <= time;
    }

    /// <summary>
    /// Ponto de onde a transcrição deve seguir a partir de <paramref name="position"/>: a própria posição, se ela
    /// está numa região de fala ampliada em <paramref name="margin"/>; senão, o início da próxima fala menos a margem.
    /// Retorna <c>null</c> quando não há mais fala depois da posição.
    /// </summary>
    public TimeSpan? NextSpeechFrom(TimeSpan position, TimeSpan margin)
    {
        var index = FirstRegionEndingAfter(position - margin);
        if (index == Regions.Length)
        {
            return null;
        }

        var start = Regions[index].Start - margin;
        return position >= start ? position : start;
    }

    /// <summary>
    /// Fim da fala contínua a partir de <paramref name="position"/>: a região que contém a posição (ou a próxima)
    /// emendada com as seguintes enquanto a pausa entre elas for menor que <paramref name="maxPause"/>. A busca
    /// para em <paramref name="limit"/>. Retorna <c>null</c> se não há fala depois da posição.
    /// </summary>
    public TimeSpan? EndOfSpeechFrom(TimeSpan position, TimeSpan maxPause, TimeSpan limit)
    {
        var index = FirstRegionEndingAfter(position);
        if (index == Regions.Length)
        {
            return null;
        }

        var end = Regions[index].End;
        for (var i = index + 1; i < Regions.Length && end < limit && Regions[i].Start - end < maxPause; i++)
        {
            end = Regions[i].End;
        }

        return end < limit ? end : limit;
    }

    /// <summary>Quanto de fala há entre <paramref name="start"/> e <paramref name="end"/>.</summary>
    public TimeSpan SpeechBetween(TimeSpan start, TimeSpan end)
    {
        var total = TimeSpan.Zero;
        for (var i = FirstRegionEndingAfter(start); i < Regions.Length && Regions[i].Start < end; i++)
        {
            var from = Regions[i].Start > start ? Regions[i].Start : start;
            var to = Regions[i].End < end ? Regions[i].End : end;
            total += to - from;
        }

        return total;
    }

    /// <summary>Índice da primeira região que termina depois de <paramref name="time"/> (busca binária).</summary>
    private int FirstRegionEndingAfter(TimeSpan time)
    {
        int low = 0, high = Regions.Length;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (Regions[middle].End > time)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return low;
    }
}
