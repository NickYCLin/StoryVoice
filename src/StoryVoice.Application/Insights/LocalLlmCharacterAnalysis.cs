using System.Security.Cryptography;
using System.Text;
using StoryVoice.Domain.Books;

namespace StoryVoice.Application.Insights;

/// <summary>
/// Versioned, full-chapter input for a local language model. The source text is sent only to the
/// configured local provider and is never returned by this contract or persisted as analysis output.
/// </summary>
public sealed record LocalLlmCharacterAnalysisRequest(
    string SourceHash,
    IReadOnlyList<LocalLlmCharacterAnalysisChapter> Chapters);

public sealed record LocalLlmCharacterAnalysisChapter(
    int ChapterNumber,
    string ChapterTitle,
    string Text);

public sealed record LocalLlmCharacterCandidate(
    string Name,
    string Confidence,
    int DialogueEvidenceCount);

public sealed record LocalLlmCharacterAnalysisCandidateResponse(
    string Name,
    string Confidence,
    int DialogueEvidenceCount,
    IReadOnlyList<int> EvidenceChapterNumbers);

public sealed record LocalLlmCharacterAnalysisResponse(
    Guid BookId,
    Guid ContentBookId,
    string Generator,
    string Model,
    string PromptVersion,
    string SourceHash,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<LocalLlmCharacterAnalysisCandidateResponse> Candidates);

public sealed record LocalLlmChapterCharacterAnalysis(
    int ChapterNumber,
    IReadOnlyList<LocalLlmCharacterCandidate> Candidates);

public sealed class LocalLlmCharacterAnalysisUnavailableException : Exception
{
    public const string StableCode = "local_llm_character_analysis_unavailable";

    public LocalLlmCharacterAnalysisUnavailableException()
        : base("本機 LLM 角色分析目前無法完成。")
    {
    }

    public LocalLlmCharacterAnalysisUnavailableException(Exception innerException)
        : base("本機 LLM 角色分析目前無法完成。", innerException)
    {
    }
}

public sealed class LocalLlmCharacterAnalysisInputTooLargeException : Exception
{
    public const string StableCode = "local_llm_character_analysis_input_too_large";

    public LocalLlmCharacterAnalysisInputTooLargeException()
        : base("本機 LLM 分析僅接受最多 30 章、全書 120,000 字且每章 6,000 字以內的正文；系統不會截斷內容。")
    {
    }
}

public sealed class LocalLlmCharacterAnalysisSourceChangedException : Exception
{
    public const string StableCode = "local_llm_character_analysis_source_changed";

    public LocalLlmCharacterAnalysisSourceChangedException()
        : base("正文在本機 LLM 分析期間已變更，舊結果已捨棄；請以目前正文重新分析。")
    {
    }
}

public interface ILocalLlmCharacterAnalysisProvider
{
    string Model { get; }

    Task<IReadOnlyList<LocalLlmChapterCharacterAnalysis>> AnalyzeAsync(
        LocalLlmCharacterAnalysisRequest request,
        CancellationToken cancellationToken);
}

public static class LocalLlmCharacterAnalysisSource
{
    public const string Generator = "local-ollama";
    public const string PromptVersion = "v1-full-chapter-context";
    private const int MaximumCandidates = 60;
    public const int MaximumChapterCount = 30;
    public const int MaximumBookCharacters = 120_000;
    public const int MaximumChapterCharacters = 6_000;

    public static LocalLlmCharacterAnalysisRequest Create(IEnumerable<Chapter> chapters)
    {
        var ordered = chapters
            .OrderBy(chapter => chapter.SortOrder)
            .Select(chapter => new LocalLlmCharacterAnalysisChapter(
                chapter.SortOrder,
                chapter.Title,
                chapter.OriginalText))
            .ToArray();
        if (ordered.Length == 0 || ordered.All(chapter => string.IsNullOrWhiteSpace(chapter.Text)))
        {
            throw new BookTextUnavailableException("這本書沒有可供本機模型完整分析的合法章節正文。");
        }

        if (ordered.Length > MaximumChapterCount
            || ordered.Sum(chapter => chapter.Text.Length) > MaximumBookCharacters
            || ordered.Any(chapter => chapter.Text.Length > MaximumChapterCharacters))
        {
            throw new LocalLlmCharacterAnalysisInputTooLargeException();
        }

        return new LocalLlmCharacterAnalysisRequest(ComputeSourceHash(ordered), ordered);
    }

    public static IReadOnlyList<LocalLlmCharacterAnalysisCandidateResponse> Merge(
        IEnumerable<(int ChapterNumber, IReadOnlyList<LocalLlmCharacterCandidate> Candidates)> chapterCandidates)
    {
        var merged = new Dictionary<string, CandidateAccumulator>(StringComparer.Ordinal);
        foreach (var (chapterNumber, candidates) in chapterCandidates)
        {
            foreach (var candidate in candidates)
            {
                var name = NormalizeName(candidate.Name);
                if (name.Length == 0 || candidate.DialogueEvidenceCount < 1)
                {
                    continue;
                }

                var confidence = NormalizeConfidence(candidate.Confidence);
                if (confidence is null)
                {
                    continue;
                }

                if (!merged.TryGetValue(name, out var accumulator))
                {
                    accumulator = new CandidateAccumulator(name, confidence);
                    merged.Add(name, accumulator);
                }

                accumulator.Confidence = StrongerConfidence(accumulator.Confidence, confidence);
                accumulator.DialogueEvidenceCount = checked(accumulator.DialogueEvidenceCount + candidate.DialogueEvidenceCount);
                accumulator.EvidenceChapterNumbers.Add(chapterNumber);
            }
        }

        return merged.Values
            .OrderByDescending(candidate => candidate.DialogueEvidenceCount)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .Take(MaximumCandidates)
            .Select(candidate => new LocalLlmCharacterAnalysisCandidateResponse(
                candidate.Name,
                candidate.Confidence,
                candidate.DialogueEvidenceCount,
                candidate.EvidenceChapterNumbers.Order().ToArray()))
            .ToArray();
    }

    public static string NormalizeName(string? value) => (value ?? string.Empty)
        .Normalize(NormalizationForm.FormKC)
        .Trim();

    public static string? NormalizeConfidence(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "high" => "high",
        "medium" => "medium",
        _ => null,
    };

    private static string StrongerConfidence(string current, string next) =>
        current == "high" || next != "high" ? current : next;

    private static string ComputeSourceHash(IEnumerable<LocalLlmCharacterAnalysisChapter> chapters)
    {
        var source = new StringBuilder();
        foreach (var chapter in chapters)
        {
            source.Append(chapter.ChapterNumber)
                .Append(':').Append(chapter.ChapterTitle.Length)
                .Append(':').Append(chapter.ChapterTitle)
                .Append(':').Append(chapter.Text.Length)
                .Append(':').Append(chapter.Text)
                .Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.ToString())))
            .ToLowerInvariant();
    }

    private sealed class CandidateAccumulator(string name, string confidence)
    {
        public string Name { get; } = name;
        public string Confidence { get; set; } = confidence;
        public int DialogueEvidenceCount { get; set; }
        public ISet<int> EvidenceChapterNumbers { get; } = new SortedSet<int>();
    }
}
