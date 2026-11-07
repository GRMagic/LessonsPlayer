using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Presentation.Services;
using LessonsPlayer.Presentation.Tests.Fakes;
using LessonsPlayer.Presentation.ViewModels;
using static LessonsPlayer.Presentation.Tests.Samples;

namespace LessonsPlayer.Presentation.Tests.ViewModels;

public sealed class PlayerViewModelTests
{
    private readonly FakeAudioPlayer _player = new();
    private readonly InMemorySettingsStore _settingsStore = new();
    private readonly PlayerViewModel _vm;

    public PlayerViewModelTests()
    {
        _vm = new PlayerViewModel(_player, new ImmediateDispatcher(), new AppSettingsService(_settingsStore));
        _vm.Load("a.mp3");
    }

    [Fact]
    public void Load_exposes_duration_and_enables_commands()
    {
        _vm.HasMedia.ShouldBeTrue();
        _vm.DurationSeconds.ShouldBe(180);
        _vm.DurationText.ShouldBe("3:00");
        _vm.PlayPauseCommand.CanExecute(null).ShouldBeTrue();
    }

    /// <summary>Regressão: sem faixa (ex.: depois de falhar ao abrir), o marcador da barra ia para o fim.</summary>
    [Fact]
    public void Slider_maximum_is_the_duration_and_never_zero()
    {
        _vm.SliderMaximum.ShouldBe(180);

        _vm.Unload();

        _vm.DurationSeconds.ShouldBe(0);
        _vm.SliderMaximum.ShouldBe(1, "com mínimo e máximo iguais o WPF desenha o marcador no fim da barra");
    }

    [Fact]
    public void Refresh_updates_slider_without_seeking()
    {
        _player.Position = Ms(65_000);

        _vm.Refresh();

        _vm.PositionSeconds.ShouldBe(65);
        _vm.PositionText.ShouldBe("1:05");
        _player.Seeks.ShouldBeEmpty();
    }

    [Fact]
    public void Changing_slider_value_seeks()
    {
        _vm.PositionSeconds = 30;

        _player.Seeks.ShouldBe([Ms(30_000)]);
    }

    [Fact]
    public void While_scrubbing_seek_happens_only_on_release()
    {
        _vm.IsScrubbing = true;
        _vm.PositionSeconds = 10;
        _vm.PositionSeconds = 20;
        _player.Position = Ms(1000);
        _vm.Refresh();
        _vm.PositionSeconds.ShouldBe(20); // o timer não sobrescreve enquanto arrasta

        _vm.IsScrubbing = false;

        _player.Seeks.ShouldBe([Ms(20_000)]);
    }

    [Fact]
    public void PlayPause_toggles_and_reflects_state()
    {
        _vm.PlayPauseCommand.Execute(null);
        _player.State.ShouldBe(PlaybackState.Playing);
        _vm.IsPlaying.ShouldBeTrue();

        _vm.PlayPauseCommand.Execute(null);
        _player.State.ShouldBe(PlaybackState.Paused);
        _vm.IsPlaying.ShouldBeFalse();
    }

    [Fact]
    public void Skip_commands_move_five_seconds_from_current_position()
    {
        _player.Position = Ms(10_000);
        _vm.Refresh();

        _vm.SkipForwardCommand.Execute(null);
        _vm.SkipBackCommand.Execute(null);

        _player.Seeks.ShouldBe([Ms(15_000), Ms(10_000)]);
    }

    [Fact]
    public void Volume_is_applied_and_kept_in_settings()
    {
        var settings = new AppSettingsService(_settingsStore);
        var vm = new PlayerViewModel(_player, new ImmediateDispatcher(), settings);

        vm.Volume = 0.3;

        _player.Volume.ShouldBe(0.3f);
        settings.Current.Volume.ShouldBe(0.3f);
    }

    [Fact]
    public void Autoplay_is_off_by_default_and_remembered()
    {
        _vm.AutoAdvance.ShouldBeFalse();

        _vm.AutoAdvance = true;

        _settingsStore.Stored.AutoAdvance.ShouldBeTrue();
        using var player = new FakeAudioPlayer();
        new PlayerViewModel(player, new ImmediateDispatcher(), new AppSettingsService(_settingsStore)).AutoAdvance.ShouldBeTrue();
    }

    [Fact]
    public void Completed_event_is_forwarded()
    {
        var completed = false;
        _vm.PlaybackCompleted += (_, _) => completed = true;

        _player.SimulateCompleted();

        completed.ShouldBeTrue();
    }
}
