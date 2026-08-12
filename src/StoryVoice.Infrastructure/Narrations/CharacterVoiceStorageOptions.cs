namespace StoryVoice.Infrastructure.Narrations;

public sealed class CharacterVoiceStorageOptions
{
    public const string SectionName = "CharacterVoiceStorage";

    public string RootPath { get; set; } = "storage/character-voices";
}
