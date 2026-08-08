namespace StoryVoice.Domain.Books;

public enum BookStatus
{
    Uploaded,
    Parsing,
    Analyzing,
    Casting,
    GeneratingAudio,
    Ready,
    Failed
}
