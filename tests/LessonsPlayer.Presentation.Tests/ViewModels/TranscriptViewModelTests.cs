using LessonsPlayer.Core.Subtitles;
using LessonsPlayer.Presentation.Services;
using LessonsPlayer.Presentation.Tests.Fakes;
using LessonsPlayer.Presentation.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static LessonsPlayer.Presentation.Tests.Samples;

namespace LessonsPlayer.Presentation.Tests.ViewModels;

public sealed class TranscriptViewModelTests
{
    private const string AudioPath = @"C:\aulas\aula01.mp3";

    private readonly InMemorySubtitleStore _store = new();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly TranscriptViewModel _vm;

    public TranscriptViewModelTests()
    {
        _vm = new TranscriptViewModel(_store, _dialogs, NullLogger<TranscriptViewModel>.Instance);
        _vm.Load(AudioPath, TwoSegments());
    }

    private WordViewModel WordAt(int segment, int word) => _vm.Segments[segment].Words[word];

    [Fact]
    public void Load_creates_segments_and_words()
    {
        _vm.Segments.Select(s => s.Model.Text).ShouldBe(["Olá pessoal", "bem vindos"]);
        _vm.HasTranscript.ShouldBeTrue();
        _vm.EmptyMessage.ShouldBeNull();
        _vm.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void Without_transcript_shows_empty_message()
    {
        _vm.Load(AudioPath, null);

        _vm.HasTranscript.ShouldBeFalse();
        _vm.EmptyMessage.ShouldNotBeNull();
    }

    [Fact]
    public void UpdatePosition_marks_current_and_spoken_words()
    {
        _vm.UpdatePosition(Ms(1700));

        _vm.ActiveSegment.ShouldBe(_vm.Segments[1]);
        _vm.Segments[1].IsActive.ShouldBeTrue();
        WordAt(1, 0).State.ShouldBe(WordState.Current);
        WordAt(1, 1).State.ShouldBe(WordState.Normal);

        _vm.UpdatePosition(Ms(2100));
        WordAt(1, 0).State.ShouldBe(WordState.Spoken);
        WordAt(1, 1).State.ShouldBe(WordState.Current);
    }

    [Fact]
    public void Moving_to_another_segment_clears_the_previous_one()
    {
        _vm.UpdatePosition(Ms(500));
        _vm.UpdatePosition(Ms(1600));

        _vm.Segments[0].IsActive.ShouldBeFalse();
        _vm.Segments[0].Words.ShouldAllBe(w => w.State == WordState.Normal);
    }

    [Fact]
    public void In_a_pause_after_a_word_it_is_spoken_not_current()
    {
        _vm.UpdatePosition(Ms(1200)); // entre os trechos

        _vm.ActiveSegment.ShouldBe(_vm.Segments[0]);
        WordAt(0, 1).State.ShouldBe(WordState.Spoken);
    }

    [Fact]
    public void Before_first_word_nothing_is_active()
    {
        _vm.Load(AudioPath, SingleWord(500, 900, "oi"));

        _vm.UpdatePosition(Ms(100));

        _vm.ActiveSegment.ShouldBeNull();
    }

    [Fact]
    public void ClickWord_selects_and_requests_seek_to_word_start()
    {
        TimeSpan? requested = null;
        _vm.SeekRequested += (_, position) => requested = position;

        _vm.ClickWordCommand.Execute(WordAt(1, 1));

        requested.ShouldBe(Ms(2000));
        _vm.SelectedWord.ShouldBe(WordAt(1, 1));
        WordAt(1, 1).IsSelected.ShouldBeTrue();
    }

    [Fact]
    public void Inline_edit_replaces_word_and_marks_dirty()
    {
        var changed = 0;
        _vm.Changed += (_, _) => changed++;

        _vm.BeginEditWordCommand.Execute(WordAt(0, 1));
        WordAt(0, 1).IsEditing.ShouldBeTrue();
        WordAt(0, 1).EditText = "galera";
        _vm.CommitEditWordCommand.Execute(WordAt(0, 1));

        _vm.Segments[0].Model.Text.ShouldBe("Olá galera");
        _vm.IsDirty.ShouldBeTrue();
        _vm.SelectedWord!.Text.ShouldBe("galera");
        changed.ShouldBe(1);
    }

    [Fact]
    public void Cancel_edit_keeps_word()
    {
        _vm.BeginEditWordCommand.Execute(WordAt(0, 1));
        WordAt(0, 1).EditText = "xyz";
        _vm.CancelEditWordCommand.Execute(WordAt(0, 1));
        _vm.CommitEditWordCommand.Execute(WordAt(0, 1)); // perder o foco depois de cancelar não aplica nada

        _vm.Segments[0].Model.Text.ShouldBe("Olá pessoal");
        _vm.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void Edits_reuse_view_models_of_unchanged_segments()
    {
        var untouched = _vm.Segments[1];

        _vm.DeleteWordCommand.Execute(WordAt(0, 0));

        _vm.Segments[1].ShouldBeSameAs(untouched);
        _vm.Segments[0].Model.Text.ShouldBe("pessoal");
    }

    [Fact]
    public void EditSegment_uses_dialog_text()
    {
        _dialogs.EditText(default!, default!).ReturnsForAnyArgs("bem-vindos à aula");

        _vm.EditSegmentCommand.Execute(WordAt(1, 0));

        _vm.Segments[1].Model.Text.ShouldBe("bem-vindos à aula");
    }

    [Fact]
    public void EditSegment_cancelled_changes_nothing()
    {
        _dialogs.EditText(default!, default!).ReturnsForAnyArgs((string?)null);

        _vm.EditSegmentCommand.Execute(WordAt(1, 0));

        _vm.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void SetStartHere_uses_current_position()
    {
        _vm.UpdatePosition(Ms(1550));

        _vm.SetStartHereCommand.Execute(WordAt(1, 0));

        _vm.Segments[1].Model.Words[0].Start.ShouldBe(Ms(1550));
    }

    [Fact]
    public void Commands_without_parameter_act_on_selected_word()
    {
        _vm.SelectWordCommand.Execute(WordAt(1, 1));

        _vm.NudgeEndLaterCommand.Execute(null);

        _vm.Segments[1].Model.Words[1].End.ShouldBe(Ms(2550));
    }

    [Fact]
    public void Split_and_merge_keep_selection_on_the_same_word()
    {
        _vm.SplitHereCommand.Execute(WordAt(1, 1));
        _vm.Segments.Count.ShouldBe(3);
        _vm.SelectedWord!.Text.ShouldBe("vindos");

        _vm.MergeWithPreviousCommand.Execute(null);
        _vm.Segments.Count.ShouldBe(2);
        _vm.SelectedWord!.Text.ShouldBe("vindos");
        _vm.SelectedWord.Location.ShouldBe(new WordLocation(1, 1));
    }

    [Fact]
    public void Undo_and_redo_restore_the_text()
    {
        _vm.DeleteWordCommand.Execute(WordAt(0, 0));

        _vm.UndoCommand.Execute(null);
        _vm.Segments[0].Model.Text.ShouldBe("Olá pessoal");
        _vm.IsDirty.ShouldBeFalse();
        _vm.RedoCommand.CanExecute(null).ShouldBeTrue();

        _vm.RedoCommand.Execute(null);
        _vm.Segments[0].Model.Text.ShouldBe("pessoal");
    }

    [Fact]
    public async Task SaveAsync_writes_and_clears_dirty_flag()
    {
        _vm.DeleteWordCommand.Execute(WordAt(0, 0));

        (await _vm.SaveAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        _store.Saved[AudioPath].Segments[0].Text.ShouldBe("pessoal");
        _vm.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public async Task SaveAsync_failure_shows_error_and_keeps_dirty()
    {
        _store.FailOnSave = true;
        _vm.DeleteWordCommand.Execute(WordAt(0, 0));

        (await _vm.SaveAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();

        _dialogs.ReceivedWithAnyArgs(1).ShowError(default!, default!);
        _vm.IsDirty.ShouldBeTrue();
    }
}
