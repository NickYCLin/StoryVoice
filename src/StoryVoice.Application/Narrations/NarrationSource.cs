using System.Security.Cryptography;
using System.Text;

namespace StoryVoice.Application.Narrations;

public sealed record NarrationChapterSource(Guid Id, int SortOrder, string Title, string Text);

public sealed record NarrationSourceDocument(string SourceHash, string Text);

public static class NarrationSource
{
    public static NarrationSourceDocument Create(IEnumerable<NarrationChapterSource> chapters)
    {
        var ordered = chapters
            .OrderBy(chapter => chapter.SortOrder)
            .ThenBy(chapter => chapter.Id)
            .ToArray();
        if (ordered.Length == 0 || ordered.Any(chapter => string.IsNullOrWhiteSpace(chapter.Text)))
        {
            throw new ArgumentException("朗讀來源必須包含至少一個非空白章節。", nameof(chapters));
        }

        var canonical = new StringBuilder();
        var narration = new StringBuilder();
        foreach (var chapter in ordered)
        {
            var title = chapter.Title?.Trim() ?? string.Empty;
            var text = chapter.Text.Trim();
            canonical.Append(chapter.Id.ToString("N"))
                .Append('\n').Append(chapter.SortOrder)
                .Append('\n').Append(title)
                .Append('\n').Append(text)
                .Append("\n\u001e\n");
            if (title.Length > 0)
            {
                narration.Append(title).Append("。\n");
            }

            narration.Append(text).Append("\n\n");
        }

        var sourceHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
        return new NarrationSourceDocument(sourceHash, narration.ToString().Trim());
    }
}
