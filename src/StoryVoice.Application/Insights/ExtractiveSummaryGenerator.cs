using System.Security.Cryptography;
using System.Text;
using StoryVoice.Domain.Books;

namespace StoryVoice.Application.Insights;

public sealed record ExtractiveSummaryDraft(
    string SourceHash,
    IReadOnlyList<SummaryExcerptResponse> Excerpts);

public static class ExtractiveSummaryGenerator
{
    public const string Generator = "builtin-leading-sentences";
    public const string Version = "v1";
    private const int MaximumExcerpts = 8;
    private const int MaximumExcerptLength = 400;

    public static ExtractiveSummaryDraft Generate(IEnumerable<Chapter> chapters)
    {
        var ordered = chapters.OrderBy(chapter => chapter.SortOrder).ToArray();
        var excerpts = ordered
            .Select(TakeLeadingSentence)
            .Where(excerpt => excerpt is not null)
            .Take(MaximumExcerpts)
            .Cast<SummaryExcerptResponse>()
            .ToArray();

        if (excerpts.Length == 0)
        {
            throw new BookTextUnavailableException("這本書沒有可用於摘要的合法正文。");
        }

        return new ExtractiveSummaryDraft(ComputeSourceHash(ordered), excerpts);
    }

    private static SummaryExcerptResponse? TakeLeadingSentence(Chapter chapter)
    {
        var text = chapter.OriginalText;
        var start = 0;
        while (start < text.Length && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        if (start == text.Length)
        {
            return null;
        }

        var end = start;
        while (end < text.Length && end - start < MaximumExcerptLength)
        {
            var current = text[end++];
            if (current is '。' or '！' or '？' or '.' or '!' or '?' or '\n' or '\r')
            {
                break;
            }
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        var excerpt = text.Substring(start, end - start);
        return new SummaryExcerptResponse(
            chapter.Id,
            chapter.Title,
            start,
            excerpt.Length,
            excerpt);
    }

    private static string ComputeSourceHash(IEnumerable<Chapter> chapters)
    {
        var source = new StringBuilder();
        foreach (var chapter in chapters)
        {
            source.Append(chapter.Id.ToString("N"))
                .Append(':')
                .Append(chapter.SortOrder)
                .Append(':')
                .Append(chapter.OriginalText.Length)
                .Append(':')
                .Append(chapter.OriginalText)
                .Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.ToString())))
            .ToLowerInvariant();
    }
}
