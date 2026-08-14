namespace StoryVoice.Domain.Insights;

/// <summary>
/// Owner-scoped, review-only local LLM output. Candidate JSON intentionally contains no source
/// chapter text or dialogue excerpts and has no relationship to Series/cast/voice assignment data.
/// </summary>
public sealed class BookLocalLlmCharacterAnalysis
{
    private BookLocalLlmCharacterAnalysis()
    {
    }

    private BookLocalLlmCharacterAnalysis(
        Guid ownerId,
        Guid bookId,
        Guid contentBookId,
        string model,
        string promptVersion,
        string sourceHash,
        string candidatesJson)
    {
        OwnerId = ownerId;
        BookId = bookId;
        Replace(contentBookId, model, promptVersion, sourceHash, candidatesJson);
    }

    public Guid BookId { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid ContentBookId { get; private set; }
    public string Generator { get; private set; } = "local-ollama";
    public string Model { get; private set; } = string.Empty;
    public string PromptVersion { get; private set; } = string.Empty;
    public string SourceHash { get; private set; } = string.Empty;
    public string CandidatesJson { get; private set; } = "[]";
    public DateTimeOffset GeneratedAt { get; private set; }

    public static BookLocalLlmCharacterAnalysis Create(
        Guid ownerId,
        Guid bookId,
        Guid contentBookId,
        string model,
        string promptVersion,
        string sourceHash,
        string candidatesJson)
    {
        if (ownerId == Guid.Empty || bookId == Guid.Empty)
        {
            throw new ArgumentException("角色分析的擁有者與書籍識別碼不可為空白。");
        }

        return new BookLocalLlmCharacterAnalysis(
            ownerId,
            bookId,
            contentBookId,
            model,
            promptVersion,
            sourceHash,
            candidatesJson);
    }

    public void Replace(
        Guid contentBookId,
        string model,
        string promptVersion,
        string sourceHash,
        string candidatesJson)
    {
        if (contentBookId == Guid.Empty)
        {
            throw new ArgumentException("角色分析的正文書籍識別碼不可為空白。", nameof(contentBookId));
        }

        ContentBookId = contentBookId;
        Model = Require(model, nameof(model), 160);
        PromptVersion = Require(promptVersion, nameof(promptVersion), 80);
        SourceHash = Require(sourceHash, nameof(sourceHash), 128);
        CandidatesJson = Require(candidatesJson, nameof(candidatesJson), 64_000);
        GeneratedAt = DateTimeOffset.UtcNow;
    }

    private static string Require(string value, string parameterName, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length < 1 || normalized.Length > maximumLength)
        {
            throw new ArgumentException("角色分析欄位長度無效。", parameterName);
        }

        return normalized;
    }
}
