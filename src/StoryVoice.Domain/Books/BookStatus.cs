namespace StoryVoice.Domain.Books;

public enum BookStatus
{
    Linked,
    Uploaded,
    Parsing,
    Analyzing,
    Casting,
    GeneratingAudio,
    Ready,
    Failed
}
