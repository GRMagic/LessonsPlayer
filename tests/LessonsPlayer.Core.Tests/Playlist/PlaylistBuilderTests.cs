using LessonsPlayer.Core.Playlist;

namespace LessonsPlayer.Core.Tests.Playlist;

public sealed class PlaylistBuilderTests
{
    [Fact]
    public void Filters_supported_files_and_sorts_naturally()
    {
        var result = PlaylistBuilder.Build(
        [
            @"C:\aulas\aula10.mp3",
            @"C:\aulas\notas.txt",
            @"C:\aulas\aula2.MP3",
            @"C:\aulas\aula1.wav",
            @"C:\aulas\aula1.legenda.txt",
            @"C:\aulas\Aula3.m4a",
        ]);

        result.Select(e => e.FileName).ShouldBe(["aula1.wav", "aula2.MP3", "Aula3.m4a", "aula10.mp3"]);
    }

    [Fact]
    public void Removes_duplicates_ignoring_case() =>
        PlaylistBuilder.Build([@"C:\a.mp3", @"c:\A.MP3"]).ShouldHaveSingleItem();

    [Theory]
    [InlineData("x.flac", true)]
    [InlineData("x.wma", true)]
    [InlineData("x.ogg", false)]
    [InlineData("", false)]
    public void IsSupported_checks_extension(string path, bool expected) =>
        PlaylistBuilder.IsSupported(path).ShouldBe(expected);
}
