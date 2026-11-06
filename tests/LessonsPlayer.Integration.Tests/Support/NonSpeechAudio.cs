namespace LessonsPlayer.Integration.Tests.Support;

/// <summary>Tipo de trecho sem fala usado para provocar texto inventado pelo Whisper.</summary>
public enum NonSpeechKind
{
    /// <summary>Silêncio digital (amostras zeradas).</summary>
    Silence,

    /// <summary>Ruído rosa, como chiado de gravação ou de ventilador.</summary>
    Noise,

    /// <summary>Música instrumental simples: acordes, melodia, baixo e chimbal.</summary>
    Music,
}

/// <summary>
/// Gera trechos sem fala, sempre iguais para a mesma semente (o teste precisa ser reproduzível).
/// As amostras são mono, na taxa pedida.
/// </summary>
internal static class NonSpeechAudio
{
    public static float[] Create(NonSpeechKind kind, TimeSpan duration, int sampleRate, float level = 0.1f, int seed = 1)
    {
        var count = (int)(duration.TotalSeconds * sampleRate);
        return kind switch
        {
            NonSpeechKind.Silence => new float[count],
            NonSpeechKind.Noise => PinkNoise(count, level, seed),
            NonSpeechKind.Music => Music(count, sampleRate, level, seed),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    /// <summary>Ruído rosa aproximado (filtro de Paul Kellet, versão econômica).</summary>
    private static float[] PinkNoise(int count, float level, int seed)
    {
#pragma warning disable CA5394 // aleatoriedade só para gerar áudio de teste reproduzível
        var random = new Random(seed);
        var samples = new float[count];
        double b0 = 0, b1 = 0, b2 = 0;
        for (var i = 0; i < count; i++)
        {
            var white = (random.NextDouble() * 2) - 1;
            b0 = (0.99765 * b0) + (white * 0.0990460);
            b1 = (0.96300 * b1) + (white * 0.2965164);
            b2 = (0.57000 * b2) + (white * 1.0526913);
            samples[i] = (float)((b0 + b1 + b2 + (white * 0.1848)) * level * 0.25);
        }

        return samples;
#pragma warning restore CA5394
    }

    /// <summary>Progressão I–V–vi–IV em colcheias a 120 bpm, com harmônicos, melodia aleatória, baixo e chimbal.</summary>
    private static float[] Music(int count, int sampleRate, float level, int seed)
    {
#pragma warning disable CA5394 // aleatoriedade só para gerar áudio de teste reproduzível
        var random = new Random(seed);
        double[][] chords =
        [
            [261.63, 329.63, 392.00], // dó maior
            [196.00, 246.94, 293.66], // sol maior
            [220.00, 261.63, 329.63], // lá menor
            [174.61, 220.00, 261.63], // fá maior
        ];

        var samples = new float[count];
        var beatSamples = sampleRate / 4;
        for (var beat = 0; beat * beatSamples < count; beat++)
        {
            var chord = chords[beat / 8 % chords.Length];
            var melody = chord[random.Next(chord.Length)] * (random.Next(2) == 0 ? 2 : 1);
            var bass = chord[0] / 2;
            var hiHat = beat % 2 == 0;
            for (var k = 0; k < beatSamples && (beat * beatSamples) + k < count; k++)
            {
                var t = (double)k / sampleRate;
                var value = 0.0;
                foreach (var frequency in chord)
                {
                    value += 0.25 * (Math.Sin(2 * Math.PI * frequency * t) + (0.3 * Math.Sin(4 * Math.PI * frequency * t)));
                }

                value += 0.5 * Math.Exp(-t * 6) * (Math.Sin(2 * Math.PI * melody * t)
                    + (0.5 * Math.Sin(4 * Math.PI * melody * t)) + (0.25 * Math.Sin(6 * Math.PI * melody * t)));
                value += 0.4 * Math.Sin(2 * Math.PI * bass * t);
                if (hiHat)
                {
                    value += 0.3 * Math.Exp(-t * 40) * ((random.NextDouble() * 2) - 1);
                }

                samples[(beat * beatSamples) + k] = (float)(level * value / 2);
            }
        }

        return samples;
#pragma warning restore CA5394
    }
}
