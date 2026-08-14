namespace StoryVoice.Application.Library;

public sealed record LibraryBookStatusResponse(
    Guid BookId,
    string Title,
    string SourceType,
    bool OfficialReaderAvailable,
    bool? OfficialTtsAvailable,
    bool AuthorizedTextAvailable,
    int ReadingNoteCount,
    string? StoryVoiceNarrationStatus,
    bool StoryVoiceNarrationMatchesAuthorizedText,
    string State,
    string? BlockedReason);
