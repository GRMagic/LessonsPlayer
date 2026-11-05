using LessonsPlayer.Core.Playlist;

namespace LessonsPlayer.Core.Tests.Playlist;

public sealed class NaturalStringComparerTests
{
    [Theory]
    [InlineData("aula2", "aula10", -1)]
    [InlineData("aula10", "aula2", 1)]
    [InlineData("aula02", "aula10", -1)]
    [InlineData("a", "a1", -1)]
    [InlineData("parte 9b", "parte 10a", -1)]
    [InlineData("B", "a", 1)]
    [InlineData("é", "f", -1)]
    public void Compares_digit_runs_by_numeric_value(string x, string y, int expectedSign) =>
        Math.Sign(NaturalStringComparer.Instance.Compare(x, y)).ShouldBe(expectedSign);

    [Fact]
    public void Equal_ignoring_case_still_has_deterministic_order()
    {
        var comparer = NaturalStringComparer.Instance;

        comparer.Compare("Aula 1", "aula 1").ShouldNotBe(0);
        comparer.Compare("Aula 1", "aula 1").ShouldBe(-comparer.Compare("aula 1", "Aula 1"));
    }

    [Fact]
    public void Handles_nulls()
    {
        NaturalStringComparer.Instance.Compare(null, "a").ShouldBe(-1);
        NaturalStringComparer.Instance.Compare("a", null).ShouldBe(1);
        NaturalStringComparer.Instance.Compare(null, null).ShouldBe(0);
    }
}
