using LessonsPlayer.Core.Subtitles;
using LessonsPlayer.Infrastructure.Storage;

namespace LessonsPlayer.Integration.Tests;

/// <summary>Legenda gravada e apagada no disco, ao lado do áudio (pasta temporária).</summary>
[Trait("Category", "Integration")]
public sealed class TxtSubtitleStoreTests : IDisposable
{
    private readonly string _workDirectory = Directory.CreateTempSubdirectory("lessonsplayer-tests-").FullName;
    private readonly TxtSubtitleStore _store = new();

    public void Dispose() => Directory.Delete(_workDirectory, recursive: true);

    [Fact]
    public async Task Delete_removes_only_the_subtitle_and_reports_whether_it_existed()
    {
        var audio = Path.Combine(_workDirectory, "aula1.mp3");
        await File.WriteAllBytesAsync(audio, [1, 2, 3], TestContext.Current.CancellationToken);
        var transcript = new Transcript(
            TranscriptMetadata.Empty, [new Segment([new Word(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Olá.")])]);
        await _store.SaveAsync(audio, transcript, TestContext.Current.CancellationToken);
        _store.Exists(audio).ShouldBeTrue();

        _store.Delete(audio).ShouldBeTrue();

        _store.Exists(audio).ShouldBeFalse();
        File.Exists(audio).ShouldBeTrue("o áudio fica");
        _store.Delete(audio).ShouldBeFalse("já não existia");
    }

    [Fact]
    public async Task Delete_of_a_read_only_subtitle_fails_without_removing_it()
    {
        var audio = Path.Combine(_workDirectory, "aula2.mp3");
        var subtitle = _store.GetSubtitlePath(audio);
        await File.WriteAllTextAsync(subtitle, "# LessonsPlayer subtitle v1\n", TestContext.Current.CancellationToken);
        File.SetAttributes(subtitle, FileAttributes.ReadOnly);
        try
        {
            Should.Throw<UnauthorizedAccessException>(() => _store.Delete(audio));
            File.Exists(subtitle).ShouldBeTrue();
        }
        finally
        {
            File.SetAttributes(subtitle, FileAttributes.Normal);
        }
    }
}
