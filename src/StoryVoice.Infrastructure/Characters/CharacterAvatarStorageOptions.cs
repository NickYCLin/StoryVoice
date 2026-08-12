namespace StoryVoice.Infrastructure.Characters;

public sealed class CharacterAvatarStorageOptions
{
    public const string SectionName = "CharacterAvatarStorage";

    public string RootPath { get; set; } = "storage/character-avatars";
}
