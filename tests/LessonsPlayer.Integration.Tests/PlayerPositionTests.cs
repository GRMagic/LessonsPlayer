using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Infrastructure.Audio;
using LessonsPlayer.Integration.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace LessonsPlayer.Integration.Tests;

/// <summary>
/// Verifica no dispositivo de áudio real que a posição reportada acompanha o tempo de parede,
/// inclusive depois de seek e pause, que é o que mantém o karaokê sincronizado.
/// Toca com volume zero. É ignorado se não houver dispositivo de saída.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PlayerPositionTests : IDisposable
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(120);

    private readonly string _workDirectory = Directory.CreateTempSubdirectory("lessonsplayer-player-").FullName;
    private readonly NAudioPlayer _player = new(NullLogger<NAudioPlayer>.Instance) { Volume = 0 };

    public void Dispose()
    {
        _player.Dispose();
        Directory.Delete(_workDirectory, recursive: true);
    }

    [Fact]
    public async Task Position_follows_wall_clock_through_seek_and_pause()
    {
        var ct = TestContext.Current.CancellationToken;
        OpenSampleOrSkip();

        _player.Play();
        await Task.Delay(1000, ct);
        _player.Position.ShouldBe(TimeSpan.FromSeconds(1), Tolerance, "posição após 1 s tocando");

        _player.Seek(TimeSpan.FromSeconds(4));
        _player.Position.ShouldBe(TimeSpan.FromSeconds(4), Tolerance, "logo após o seek");
        await Task.Delay(800, ct);
        _player.Position.ShouldBe(TimeSpan.FromSeconds(4.8), Tolerance, "0,8 s depois do seek");

        // Ao pausar, o buffer do dispositivo (~80 ms) ainda termina de tocar; depois a posição para.
        var atPause = _player.Position;
        _player.Pause();
        await Task.Delay(250, ct);
        var settled = _player.Position;
        (settled - atPause).ShouldBeLessThan(TimeSpan.FromMilliseconds(150), "avanço depois da pausa limitado à latência");
        await Task.Delay(400, ct);
        _player.Position.ShouldBe(settled, TimeSpan.FromMilliseconds(20), "parado durante a pausa");

        _player.Seek(TimeSpan.FromSeconds(2));
        _player.Position.ShouldBe(TimeSpan.FromSeconds(2), Tolerance, "seek durante a pausa");
        _player.State.ShouldBe(PlaybackState.Paused);

        _player.Play();
        await Task.Delay(500, ct);
        _player.Position.ShouldBe(TimeSpan.FromSeconds(2.5), Tolerance, "retomou do ponto do seek");
    }

    [Fact]
    public async Task Reaching_the_end_raises_completed_and_reports_duration()
    {
        var ct = TestContext.Current.CancellationToken;
        OpenSampleOrSkip();
        var completed = new TaskCompletionSource();
        _player.PlaybackCompleted += (_, _) => completed.TrySetResult();

        _player.Seek(_player.Duration - TimeSpan.FromMilliseconds(500));
        _player.Play();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);

        _player.State.ShouldBe(PlaybackState.Stopped);
        _player.Position.ShouldBe(_player.Duration);
    }

    private void OpenSampleOrSkip()
    {
        var audio = SpeechAudio.TrySynthesize(
            "Este é um áudio de teste para medir a posição de reprodução do player com precisão.",
            "pt-BR",
            Path.Combine(_workDirectory, "sample.wav"));
        if (audio is null)
        {
            Assert.Skip("Nenhuma voz TTS pt-BR instalada.");
        }

        try
        {
            _player.Open(audio.FilePath);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            // Só a ausência de dispositivo (erro COM do WASAPI) vira "skip"; qualquer outra falha é bug.
            Assert.Skip($"Sem dispositivo de áudio: {ex.Message}");
        }

        _player.Duration.ShouldBeGreaterThan(TimeSpan.FromSeconds(5.5), "o áudio de teste precisa ter mais de 5,5 s");
    }
}
