using LessonsPlayer.Core.Search;
using static LessonsPlayer.Core.Tests.TestData;

namespace LessonsPlayer.Core.Tests.Search;

public sealed class SearchQueryTests
{
    [Theory]
    [InlineData("licao", "Lição 03 – Verbos")]
    [InlineData("LIÇÃO", "licao 03")]
    [InlineData("perfect present", "Lesson 5 - Present Perfect")]
    [InlineData("  verbos   03 ", "Lição 03 – Verbos irregulares")]
    [InlineData("it's", "It’s a beautiful day")]
    public void Matches_ignoring_accents_case_and_word_order(string query, string text) =>
        SearchQuery.Parse(query).Matches(text).ShouldBeTrue();

    [Theory]
    [InlineData("licao 04", "Lição 03 – Verbos")]
    [InlineData("past", "Lesson 5 - Present Perfect")]
    public void Every_word_must_appear(string query, string text) =>
        SearchQuery.Parse(query).Matches(text).ShouldBeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_query_matches_everything(string? query)
    {
        var parsed = SearchQuery.Parse(query);

        parsed.IsEmpty.ShouldBeTrue();
        parsed.Matches("qualquer coisa").ShouldBeTrue();
    }

    [Fact]
    public void Finds_the_segments_that_match_in_a_transcript()
    {
        var transcript = Tr(
            Seg(W(0, 400, "Good"), W(400, 900, "morning.")),
            Seg(W(1000, 1400, "The"), W(1400, 1800, "present"), W(1800, 2300, "perfect.")),
            Seg(W(3000, 3500, "Perfect"), W(3500, 4000, "pronunciation.")));

        var matches = SearchQuery.Parse("perfect").FindIn(transcript).ToList();

        matches.ShouldBe(
        [
            new SearchMatch(1, Ms(1000), "The present perfect."),
            new SearchMatch(2, Ms(3000), "Perfect pronunciation."),
        ]);
        SearchQuery.Parse("perfect").FindIn(transcript, limit: 1).ShouldHaveSingleItem().SegmentIndex.ShouldBe(1);
        SearchQuery.Empty.FindIn(transcript).ShouldBeEmpty("uma busca vazia não lista a legenda inteira");
    }
}
