using StoryVoice.Application.Narrations;

namespace StoryVoice.UnitTests;

public sealed class NarrationSourceTests
{
    [Fact]
    public void Source_text_and_hash_are_deterministic_and_ordered()
    {
        var second = new NarrationChapterSource(Guid.NewGuid(), 2, "第二章", "第二段正文。");
        var first = new NarrationChapterSource(Guid.NewGuid(), 1, "第一章", "第一段正文。");

        var a = NarrationSource.Create([second, first]);
        var b = NarrationSource.Create([first, second]);

        Assert.Equal(a.SourceHash, b.SourceHash);
        Assert.Equal(a.Text, b.Text);
        Assert.Contains("第一章", a.Text);
        Assert.True(a.Text.IndexOf("第一章", StringComparison.Ordinal) < a.Text.IndexOf("第二章", StringComparison.Ordinal));
    }

    [Fact]
    public void Blank_chapters_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => NarrationSource.Create([
            new NarrationChapterSource(Guid.NewGuid(), 1, "空章", "   ")
        ]));
    }
}
