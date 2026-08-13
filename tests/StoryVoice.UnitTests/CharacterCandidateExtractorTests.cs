using StoryVoice.Application.Narrations.SpeechPlanning;
using StoryVoice.Domain.Books;

namespace StoryVoice.UnitTests;

public sealed class CharacterCandidateExtractorTests
{
    [Fact]
    public void Names_next_to_a_reporting_verb_are_ranked_by_occurrence_with_a_dialogue_sample()
    {
        var book = Book.Create(Guid.NewGuid(), "候選角色測試", "作者", "zh-TW", "story.txt");
        book.AddChapter(
            1,
            "第一章",
            "小明說：「你好。」\n小華問道：「怎麼了？」\n小明說：「還好嗎？」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        Assert.Equal(2, candidates.Count);
        Assert.Equal("小明", candidates[0].Name);
        Assert.Equal(2, candidates[0].OccurrenceCount);
        Assert.Equal("「你好。」", candidates[0].SampleDialogue);
        Assert.Equal("第一章", candidates[0].SampleChapterTitle);
        Assert.Equal("小華", candidates[1].Name);
        Assert.Equal(1, candidates[1].OccurrenceCount);
        Assert.Equal("「怎麼了？」", candidates[1].SampleDialogue);
    }

    [Fact]
    public void Single_character_third_person_pronouns_never_surface_as_candidates()
    {
        var book = Book.Create(Guid.NewGuid(), "代名詞測試", "作者", "zh-TW", "story.txt");
        book.AddChapter(1, "第一章", "「小心！」他說。\n「別過去。」她又喊道。");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        Assert.Empty(candidates);
    }

    [Fact]
    public void Ordinary_prose_containing_reporting_verb_characters_never_surfaces_as_a_candidate()
    {
        // Regression: scanning whole narrator paragraphs (instead of only the connector text right
        // next to a quote) previously turned everyday vocabulary that happens to contain a
        // reporting-verb character — 不知道／應該說／回答不出— into bogus high-frequency "candidates".
        var book = Book.Create(Guid.NewGuid(), "敘述文字測試", "作者", "zh-TW", "story.txt");
        book.AddChapter(
            1,
            "第一章",
            "學長也不知道該怎麼回答，這件事其實應該說清楚，可是他一直答不出來。\n" +
            "過了很久，學長說：「對不起，是我不好。」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        var candidate = Assert.Single(candidates);
        Assert.Equal("學長", candidate.Name);
        Assert.Equal(1, candidate.OccurrenceCount);
    }

    [Fact]
    public void Only_the_boundary_nearest_match_counts_when_a_connector_has_an_earlier_unrelated_phrase()
    {
        var book = Book.Create(Guid.NewGuid(), "邊界測試", "作者", "zh-TW", "story.txt");
        book.AddChapter(
            1,
            "第一章",
            "「你要走了？」他心想這應該說得通，過了一會兒，小華問道：「真的嗎？」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        var candidate = Assert.Single(candidates);
        Assert.Equal("小華", candidate.Name);
    }

    [Fact]
    public void Latin_names_are_recognized_alongside_chinese_names()
    {
        var book = Book.Create(Guid.NewGuid(), "翻譯小說測試", "作者", "zh-TW", "story.txt");
        book.AddChapter(1, "第一章", "Alice說：「Hello。」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        var candidate = Assert.Single(candidates);
        Assert.Equal("Alice", candidate.Name);
    }

    [Fact]
    public void Extraction_is_stable_across_multiple_chapters_in_reading_order()
    {
        var book = Book.Create(Guid.NewGuid(), "跨章節測試", "作者", "zh-TW", "story.txt");
        book.AddChapter(1, "第一章", "小明說：「你好。」");
        book.AddChapter(2, "第二章", "小華問道：「你要走了？」\n小華問道：「真的嗎？」\n小華問道：「你確定？」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        Assert.Equal(2, candidates.Count);
        Assert.Equal("小華", candidates[0].Name);
        Assert.Equal(3, candidates[0].OccurrenceCount);
        Assert.Equal("小明", candidates[1].Name);
        Assert.Equal(1, candidates[1].OccurrenceCount);
        Assert.Equal("第一章", candidates[1].SampleChapterTitle);
    }
}
