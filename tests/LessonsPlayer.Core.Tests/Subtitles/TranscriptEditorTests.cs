using LessonsPlayer.Core.Subtitles;
using static LessonsPlayer.Core.Tests.TestData;

namespace LessonsPlayer.Core.Tests.Subtitles;

public sealed class TranscriptEditorTests
{
    private readonly TranscriptEditor _editor = new(Sample());

    private Transcript Current => _editor.Current;

    [Fact]
    public void ReplaceWordText_keeps_timing()
    {
        _editor.ReplaceWordText(new(0, 1), "galera").ShouldBeTrue();

        Current.Segments[0].Words[1].ShouldBe(W(400, 1000, "galera"));
        _editor.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public void ReplaceWordText_with_several_words_splits_the_interval()
    {
        _editor.ReplaceWordText(new(0, 1), "meu povo").ShouldBeTrue();

        Current.Segments[0].Words.Select(w => w.Text).ShouldBe(["Olá", "meu", "povo"]);
        Current.Segments[0].Words[1].ShouldBe(W(400, 657, "meu")); // 600 ms × 3/7
        Current.Segments[0].End.ShouldBe(Ms(1000));
    }

    [Fact]
    public void ReplaceWordText_with_empty_text_deletes_the_word()
    {
        _editor.ReplaceWordText(new(0, 0), " ").ShouldBeTrue();

        Current.Segments[0].Text.ShouldBe("pessoal");
    }

    [Fact]
    public void Edits_keep_untouched_segments_by_reference()
    {
        var secondSegment = Current.Segments[1];

        _editor.ReplaceWordText(new(0, 0), "Oi");

        Current.Segments[1].ShouldBeSameAs(secondSegment);
    }

    [Fact]
    public void ReplaceSegmentText_realigns_words()
    {
        _editor.ReplaceSegmentText(1, "bem-vindos à aula").ShouldBeTrue();

        var segment = Current.Segments[1];
        segment.Text.ShouldBe("bem-vindos à aula");
        segment.Start.ShouldBe(Ms(1500));
        segment.End.ShouldBe(Ms(2500));
    }

    [Fact]
    public void Deleting_the_last_word_removes_the_segment()
    {
        _editor.ReplaceSegmentText(0, "Olá").ShouldBeTrue();
        _editor.DeleteWord(new(0, 0)).ShouldBeTrue();

        Current.Segments.ShouldHaveSingleItem().Text.ShouldBe("bem vindos");
    }

    [Fact]
    public void SetWordStart_is_clamped_by_neighbors_and_trims_previous_word()
    {
        // Antes do início da palavra anterior: limitado a ele.
        _editor.SetWordStart(new(0, 1), Ms(-200)).ShouldBeTrue();
        Current.Segments[0].Words[1].Start.ShouldBe(Ms(0));
        Current.Segments[0].Words[0].End.ShouldBe(Ms(0));

        // Depois do início da próxima palavra (outro trecho): limitado a ele.
        _editor.SetWordStart(new(0, 1), Ms(1800)).ShouldBeTrue();
        Current.Segments[0].Words[1].Start.ShouldBe(Ms(1500));
        Current.Segments[0].Words[1].End.ShouldBe(Ms(1500));
    }

    [Fact]
    public void SetWordEnd_is_clamped_between_own_start_and_next_start()
    {
        // Já está no limite (início da próxima palavra): nada muda.
        _editor.SetWordEnd(new(0, 0), Ms(5000)).ShouldBeFalse();
        Current.Segments[0].Words[0].End.ShouldBe(Ms(400));

        _editor.SetWordEnd(new(0, 0), Ms(200)).ShouldBeTrue();
        Current.Segments[0].Words[0].End.ShouldBe(Ms(200));

        _editor.SetWordEnd(new(0, 0), Ms(-100)).ShouldBeTrue();
        Current.Segments[0].Words[0].End.ShouldBe(Ms(0));
    }

    [Fact]
    public void ShiftWordStart_moves_by_delta()
    {
        _editor.ShiftWordStart(new(1, 1), Ms(-50)).ShouldBeTrue();

        Current.Segments[1].Words[1].Start.ShouldBe(Ms(1950));
        Current.Segments[1].Words[0].End.ShouldBe(Ms(1950));
    }

    [Fact]
    public void SplitSegmentBefore_creates_two_segments()
    {
        _editor.SplitSegmentBefore(new(1, 1)).ShouldBeTrue();

        Current.Segments.Select(s => s.Text).ShouldBe(["Olá pessoal", "bem", "vindos"]);
    }

    [Fact]
    public void SplitSegmentBefore_first_word_does_nothing() =>
        _editor.SplitSegmentBefore(new(1, 0)).ShouldBeFalse();

    [Fact]
    public void MergeWithNext_and_MergeWithPrevious_join_segments()
    {
        _editor.MergeWithNext(0).ShouldBeTrue();
        Current.Segments.ShouldHaveSingleItem().Text.ShouldBe("Olá pessoal bem vindos");

        _editor.SplitSegmentBefore(new(0, 2));
        _editor.MergeWithPrevious(1).ShouldBeTrue();
        Current.Segments.ShouldHaveSingleItem();
    }

    [Fact]
    public void MergeWithNext_on_last_segment_does_nothing() =>
        _editor.MergeWithNext(1).ShouldBeFalse();

    [Fact]
    public void Invalid_locations_are_rejected_without_changes()
    {
        _editor.ReplaceWordText(new(9, 0), "x").ShouldBeFalse();
        _editor.DeleteWord(new(0, 9)).ShouldBeFalse();
        _editor.SetWordStart(new(-1, 0), Ms(0)).ShouldBeFalse();

        _editor.CanUndo.ShouldBeFalse();
        _editor.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void No_op_edits_do_not_create_history()
    {
        _editor.ReplaceWordText(new(0, 0), "Olá").ShouldBeFalse();
        _editor.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public void Undo_and_Redo_restore_states_and_dirty_flag()
    {
        var original = Current;
        _editor.DeleteWord(new(0, 0));
        var edited = Current;

        _editor.Undo().ShouldBeTrue();
        Current.ShouldBeSameAs(original);
        _editor.IsDirty.ShouldBeFalse();

        _editor.Redo().ShouldBeTrue();
        Current.ShouldBeSameAs(edited);
        _editor.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public void New_edit_clears_redo_history()
    {
        _editor.DeleteWord(new(0, 0));
        _editor.Undo();

        _editor.DeleteWord(new(1, 0));

        _editor.CanRedo.ShouldBeFalse();
    }

    [Fact]
    public void MarkSaved_clears_dirty_flag_and_raises_Changed()
    {
        var raised = 0;
        _editor.Changed += (_, _) => raised++;

        _editor.DeleteWord(new(0, 0));
        _editor.MarkSaved();

        _editor.IsDirty.ShouldBeFalse();
        raised.ShouldBe(2);
    }
}
