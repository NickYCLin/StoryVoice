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
            "小明說：「你好。」\n小華問道：「怎麼了？」\n小明說：「還好嗎？」\n小華問道：「真的嗎？」\n小明說：「早安。」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        Assert.Equal(2, candidates.Count);
        Assert.Equal("小明", candidates[0].Name);
        Assert.Equal(3, candidates[0].OccurrenceCount);
        Assert.Equal("「你好。」", candidates[0].SampleDialogue);
        Assert.Equal("第一章", candidates[0].SampleChapterTitle);
        Assert.Equal("小華", candidates[1].Name);
        Assert.Equal(2, candidates[1].OccurrenceCount);
        Assert.Equal("「怎麼了？」", candidates[1].SampleDialogue);
    }

    [Fact]
    public void A_name_that_only_appears_once_does_not_surface_even_though_it_matches()
    {
        // A real character gets named next to their dialogue repeatedly; a name mentioned exactly
        // once is treated as too weak a signal to surface, even though it otherwise matches the
        // reporting-clause pattern cleanly — this is what keeps one-off regex noise off the list.
        var book = Book.Create(Guid.NewGuid(), "單次出現測試", "作者", "zh-TW", "story.txt");
        book.AddChapter(1, "第一章", "小明說：「你好。」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        Assert.Empty(candidates);
    }

    [Fact]
    public void Single_character_third_person_pronouns_never_surface_as_candidates()
    {
        var book = Book.Create(Guid.NewGuid(), "代名詞測試", "作者", "zh-TW", "story.txt");
        book.AddChapter(
            1,
            "第一章",
            "「小心！」他說。\n「別過去。」她又喊道。\n「快走！」他又說。\n「別回頭。」她再喊道。");

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
            "過了很久，學長說：「對不起，是我不好。」\n" +
            "學長皺著眉，這件事其實不該這麼複雜，可是又答不出來。\n" +
            "沉默了半晌，學長說：「算了，這不是重點。」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        var candidate = Assert.Single(candidates);
        Assert.Equal("學長", candidate.Name);
        Assert.Equal(2, candidate.OccurrenceCount);
    }

    [Fact]
    public void Only_the_boundary_nearest_match_counts_when_a_connector_has_an_earlier_unrelated_phrase()
    {
        var book = Book.Create(Guid.NewGuid(), "邊界測試", "作者", "zh-TW", "story.txt");
        book.AddChapter(
            1,
            "第一章",
            "「你要走了？」他心想這應該說得通，過了一會兒，小華問道：「真的嗎？」\n" +
            "「你確定？」他心想這應該說得通，過了一會兒，小華問道：「當然。」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        var candidate = Assert.Single(candidates);
        Assert.Equal("小華", candidate.Name);
        Assert.Equal(2, candidate.OccurrenceCount);
    }

    [Fact]
    public void A_sole_title_bearing_actor_between_two_dialogues_surfaces_while_a_grammar_token_does_not()
    {
        // Regression: a dialogue bridge can identify its actor without a reporting verb. Here the
        // same title-bearing actor performs the only action between both pairs of quoted lines, so
        // 「幸運同學」 is a useful candidate. Conversely, 「繼續說」 is ordinary grammar, not a name;
        // repeating it must never turn 「繼續」 into a candidate.
        var book = Book.Create(Guid.NewGuid(), "對白橋接角色測試", "作者", "zh-TW", "story.txt");
        book.AddChapter(
            1,
            "第一章",
            "「這樣喔，我聽說中縣有間學校工科感覺還不錯。」幸運同學乾脆把椅子轉過來，拿了原子筆就在我的單子空白處畫圈圈，「如果你也申請能過，我們還可以再當三年同學哩。」\n" +
            "「你先忙。」繼續說了一會兒，「嗯。」\n" +
            "「我走了。」繼續說了一會兒，「路上小心。」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        var candidate = Assert.Single(candidates);
        Assert.Equal("幸運同學", candidate.Name);
        Assert.Equal(2, candidate.OccurrenceCount);
        Assert.DoesNotContain(candidates, item => item.Name == "繼續");
    }

    [Fact]
    public void A_bridge_with_multiple_title_bearing_actors_remains_unknown()
    {
        // The bridge identifies a speaker only when it has one actor. Naming 幸運同學 and a bare
        // 老師 still describes an interaction, not enough evidence that either adjacent quote belongs
        // to one of them, so surfacing the named actor would be a false attribution hint.
        var book = Book.Create(Guid.NewGuid(), "多人對白橋接測試", "作者", "zh-TW", "story.txt");
        book.AddChapter(
            1,
            "第一章",
            "「你看這個。」幸運同學把紙遞給老師，「我晚點再來。」\n" +
            "「這題不會。」幸運同學看向老師，「明天再問。」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        Assert.Empty(candidates);
    }

    [Fact]
    public void A_bridge_with_two_named_people_sharing_the_same_title_remains_unknown()
    {
        // The second person has the same title suffix, so counting distinct suffix text would miss
        // the ambiguity. Both people are explicitly named and either could own either quote.
        var book = Book.Create(Guid.NewGuid(), "同稱謂多人對白橋接測試", "作者", "zh-TW", "story.txt");
        book.AddChapter(
            1,
            "第一章",
            "「你看這個。」幸運同學把紙遞給小美同學，「我晚點再來。」\n" +
            "「這題不會。」幸運同學看向小美同學，「明天再問。」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        Assert.Empty(candidates);
    }

    [Fact]
    public void Common_interrogatives_and_demonstratives_never_surface_as_candidates()
    {
        // Regression: "說什麼" ("say what") sits directly next to the quote boundary as ordinary
        // grammar, not buried mid-paragraph, so boundary-anchoring alone doesn't filter it out —
        // "什麼" itself has to be excluded as a whole word.
        var book = Book.Create(Guid.NewGuid(), "疑問詞測試", "作者", "zh-TW", "story.txt");
        book.AddChapter(
            1,
            "第一章",
            "他不知道該說什麼。「你到底要幹嘛？」\n她也不知道該說什麼。「這樣真的好嗎？」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        Assert.Empty(candidates);
    }

    [Fact]
    public void Falls_back_to_an_earlier_real_name_when_the_boundary_nearest_match_is_a_blocked_word()
    {
        var book = Book.Create(Guid.NewGuid(), "回退測試", "作者", "zh-TW", "story.txt");
        book.AddChapter(
            1,
            "第一章",
            "小明說道，這樣說好嗎，「你確定？」\n小明說道，這樣說好嗎，「別鬧了。」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        var candidate = Assert.Single(candidates);
        Assert.Equal("小明", candidate.Name);
        Assert.Equal(2, candidate.OccurrenceCount);
    }

    [Fact]
    public void Latin_names_are_recognized_alongside_chinese_names()
    {
        var book = Book.Create(Guid.NewGuid(), "翻譯小說測試", "作者", "zh-TW", "story.txt");
        book.AddChapter(1, "第一章", "Alice說：「Hello。」\nAlice說：「Nice to meet you。」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        var candidate = Assert.Single(candidates);
        Assert.Equal("Alice", candidate.Name);
    }

    [Fact]
    public void Extraction_is_stable_across_multiple_chapters_in_reading_order()
    {
        var book = Book.Create(Guid.NewGuid(), "跨章節測試", "作者", "zh-TW", "story.txt");
        book.AddChapter(1, "第一章", "小明說：「你好。」");
        book.AddChapter(
            2,
            "第二章",
            "小明說：「再見。」\n小華問道：「你要走了？」\n小華問道：「真的嗎？」\n小華問道：「你確定？」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        Assert.Equal(2, candidates.Count);
        Assert.Equal("小華", candidates[0].Name);
        Assert.Equal(3, candidates[0].OccurrenceCount);
        Assert.Equal("小明", candidates[1].Name);
        Assert.Equal(2, candidates[1].OccurrenceCount);
        Assert.Equal("第一章", candidates[1].SampleChapterTitle);
    }
}
