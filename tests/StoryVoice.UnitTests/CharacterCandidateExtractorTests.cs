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
            "我叫王小明。\n我叫李小華。\n「王小明，等等。」\n「李小華，先看這裡。」\n王小明說：「你好。」\n李小華問道：「怎麼了？」\n王小明說：「還好嗎？」\n李小華問道：「真的嗎？」\n王小明說：「早安。」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        Assert.Equal(2, candidates.Count);
        Assert.Equal("王小明", candidates[0].Name);
        Assert.Equal(3, candidates[0].OccurrenceCount);
        Assert.Equal("「你好。」", candidates[0].SampleDialogue);
        Assert.Equal("第一章", candidates[0].SampleChapterTitle);
        Assert.Equal("李小華", candidates[1].Name);
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
        book.AddChapter(1, "第一章", "我叫王小明。\n王小明說：「你好。」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        Assert.Empty(candidates);
    }

    [Fact]
    public void Complete_chapter_context_surfaces_a_descriptive_speaker_and_first_person_narrator()
    {
        var book = Book.Create(Guid.NewGuid(), "完整章節語境測試", "作者", "zh-TW", "story.txt");
        book.AddChapter(
            1,
            "第三話 學長與土著",
            "「你昏醒了？」死神轉過頭來，口氣非常之不好的對著我問。" +
            "連忙用力點頭，「我在陰間嗎？」我想，這地方怎麼看都不像人間。" +
            "眼前的漂亮死神不知道該怎麼辦。紅紅的眼睛瞪了我一眼，居然有點冷笑的，" +
            "「如果你要當這裡是陰間也無所謂。」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        Assert.Collection(
            candidates,
            first =>
            {
                Assert.Equal("死神", first.Name);
                Assert.Equal(2, first.OccurrenceCount);
                Assert.Equal(CharacterCandidateKind.NamedSpeaker, first.Kind);
                Assert.Equal("第三話 學長與土著", first.SampleChapterTitle);
            },
            second =>
            {
                Assert.Equal("第一人稱敘事者（我）", second.Name);
                Assert.Equal(1, second.OccurrenceCount);
                Assert.Equal(CharacterCandidateKind.FirstPersonNarrator, second.Kind);
            });
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
    public void Ordinary_prose_and_bare_titles_never_surface_as_candidates()
    {
        // Generic roles such as 學長 are not names. The candidate list is for a human to confirm
        // people, so it must prefer omitting an unnamed speaker over filling the UI with roles or
        // prose that happens to contain reporting-verb characters.
        var book = Book.Create(Guid.NewGuid(), "敘述文字測試", "作者", "zh-TW", "story.txt");
        book.AddChapter(
            1,
            "第一章",
            "學長也不知道該怎麼回答，這件事其實應該說清楚，可是他一直答不出來。\n" +
            "過了很久，學長說：「對不起，是我不好。」\n" +
            "學長皺著眉，這件事其實不該這麼複雜，可是又答不出來。\n" +
            "沉默了半晌，學長說：「算了，這不是重點。」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        Assert.Empty(candidates);
    }

    [Fact]
    public void Homographic_prose_and_generic_people_never_surface_as_candidates()
    {
        // These are the structural false positives found in the actual candidate screen:
        // 天知道 was split as 天知 + 道, 說實話 was read backwards as 實話, and a role phrase such
        // as 等到學長說 was swallowed as a name. Repeat each shape so the minimum-count filter
        // cannot hide a broken matcher.
        var book = Book.Create(Guid.NewGuid(), "同音詞與泛稱測試", "作者", "zh-TW", "story.txt");
        book.AddChapter(
            1,
            "第一章",
            "「前句。」天知道，「後句。」\n" +
            "「前句。」天知道，「後句。」\n" +
            "「前句。」說實話，「後句。」\n" +
            "「前句。」說實話，「後句。」\n" +
            "「前句。」等到學長說，「後句。」\n" +
            "「前句。」等到學長說，「後句。」\n" +
            "「前句。」男生說，「後句。」\n" +
            "「前句。」男生說，「後句。」\n" +
            "「前句。」說小心，「後句。」\n" +
            "「前句。」說小心，「後句。」\n" +
            "「小心，快跑。」\n" +
            "小心說：「前句。」\n" +
            "小心說：「後句。」\n" +
            "「慢慢，快一點。」\n" +
            "「慢慢，別停。」\n" +
            "「慢慢，繼續走。」\n" +
            "慢慢這樣說：「前句。」\n" +
            "慢慢這樣說：「後句。」\n" +
            "白色說：「前句。」\n" +
            "白色說：「後句。」\n" +
            "「前句。」經知道，「後句。」\n" +
            "「前句。」經知道，「後句。」\n" +
            "出口說：「前句。」\n" +
            "出口說：「後句。」\n" +
            "出口處說：「前句。」\n" +
            "出口處說：「後句。」\n" +
            "成績單說：「前句。」\n" +
            "成績單說：「後句。」\n" +
            "恐怖說：「前句。」\n" +
            "恐怖說：「後句。」\n" +
            "所謂說：「前句。」\n" +
            "所謂說：「後句。」\n" +
            "時候說：「前句。」\n" +
            "時候說：「後句。」\n" +
            "話題說：「前句。」\n" +
            "話題說：「後句。」\n" +
            "小學生說：「前句。」\n" +
            "小學生說：「後句。」\n" +
            "「高中同學，請先等等。」\n" +
            "高中同學說：「前句。」\n" +
            "高中同學說：「後句。」\n" +
            "高中生說：「前句。」\n" +
            "高中生說：「後句。」\n" +
            "「慢慢，快一點。」\n" +
            "慢慢這樣說：「前句。」");
        book.AddChapter(
            2,
            "第二章",
            "「慢慢，別停。」\n" +
            "「慢慢，繼續走。」\n" +
            "慢慢這樣說：「後句。」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        Assert.Empty(candidates);
    }

    [Fact]
    public void Conventional_names_and_named_titles_survive_the_precision_filter_while_unverified_aliases_remain_out()
    {
        var book = Book.Create(Guid.NewGuid(), "正向人名證據測試", "作者", "zh-TW", "story.txt");
        book.AddChapter(
            1,
            "第一章",
            "我叫千冬歲。\n" +
            "千冬歲說：「先走。」\n" +
            "千冬歲說：「等等我。」\n" +
            "「喵喵，先過來。」\n" +
            "喵喵這樣問道：「要吃飯嗎？」\n" +
            "「這樣喔。」幸運同學把椅子轉過來，「那就這麼辦。」");
        book.AddChapter(
            2,
            "第二章",
            "「喵喵，等等我。」\n" +
            "「喵喵，先坐下。」\n" +
            "喵喵則問道：「還是要喝茶？」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        Assert.Collection(
            candidates,
            candidate =>
            {
                Assert.Equal("千冬歲", candidate.Name);
                Assert.Equal(2, candidate.OccurrenceCount);
            },
            candidate =>
            {
                Assert.Equal("幸運同學", candidate.Name);
                Assert.Equal(2, candidate.OccurrenceCount);
            });
        Assert.DoesNotContain(candidates, candidate => candidate.Name == "喵喵");
    }

    [Fact]
    public void Only_the_boundary_nearest_match_counts_when_a_connector_has_an_earlier_unrelated_phrase()
    {
        var book = Book.Create(Guid.NewGuid(), "邊界測試", "作者", "zh-TW", "story.txt");
        book.AddChapter(
            1,
            "第一章",
            "我叫李小華。\n" +
            "「李小華，先聽我說。」\n" +
            "「你要走了？」他心想這應該說得通，過了一會兒，李小華問道：「真的嗎？」\n" +
            "「你確定？」他心想這應該說得通，過了一會兒，李小華問道：「當然。」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        var candidate = Assert.Single(candidates);
        Assert.Equal("李小華", candidate.Name);
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
            "我叫王小明。\n" +
            "「王小明，先等等。」\n" +
            "王小明說道，這樣說好嗎，「你確定？」\n王小明說道，這樣說好嗎，「別鬧了。」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        var candidate = Assert.Single(candidates);
        Assert.Equal("王小明", candidate.Name);
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
        book.AddChapter(1, "第一章", "「王小明，等等。」\n「李小華，先看這裡。」\n王小明說：「你好。」");
        book.AddChapter(
            2,
            "第二章",
            "我叫王小明。\n我叫李小華。\n" +
            "王小明說：「再見。」\n李小華問道：「你要走了？」\n李小華問道：「真的嗎？」\n李小華問道：「你確定？」");

        var candidates = CharacterCandidateExtractor.Extract(book.Chapters);

        Assert.Equal(2, candidates.Count);
        Assert.Equal("李小華", candidates[0].Name);
        Assert.Equal(3, candidates[0].OccurrenceCount);
        Assert.Equal("王小明", candidates[1].Name);
        Assert.Equal(2, candidates[1].OccurrenceCount);
        Assert.Equal("第一章", candidates[1].SampleChapterTitle);
    }
}
