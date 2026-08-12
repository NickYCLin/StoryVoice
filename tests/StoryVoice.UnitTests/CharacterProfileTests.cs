using StoryVoice.Domain.Characters;

namespace StoryVoice.UnitTests;

public sealed class CharacterProfileTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static CharacterProfile CreateProfile() =>
        CharacterProfile.Create(
            Guid.NewGuid(),
            OwnerId,
            "小羽",
            avatarRelativePath: null,
            age: "16",
            gender: "女",
            birthday: "2009-11-23",
            personality: "溫柔、細心",
            catchphrase: "沒問題的，我會努力的！",
            background: "住在海邊小鎮的高中生。",
            speakingStyle: "語氣輕柔",
            Now);

    [Fact]
    public void Create_starts_active_with_all_bio_fields()
    {
        var profile = CreateProfile();

        Assert.True(profile.IsActive);
        Assert.Equal("小羽", profile.CanonicalName);
        Assert.Equal("16", profile.Age);
        Assert.Equal("女", profile.Gender);
        Assert.Null(profile.AvatarRelativePath);
    }

    [Fact]
    public void Deactivate_then_activate_round_trips_and_touches_updated_at()
    {
        var profile = CreateProfile();

        profile.Deactivate(Now.AddMinutes(1));
        Assert.False(profile.IsActive);

        profile.Activate(Now.AddMinutes(2));
        Assert.True(profile.IsActive);
        Assert.Equal(Now.AddMinutes(2), profile.UpdatedAt);
    }

    [Fact]
    public void Update_replaces_bio_fields_and_allows_clearing_optional_ones()
    {
        var profile = CreateProfile();

        profile.Update(
            "小羽",
            age: "17",
            gender: "女",
            birthday: "2009-11-23",
            personality: null,
            catchphrase: null,
            background: null,
            speakingStyle: null,
            Now.AddMinutes(1));

        Assert.Equal("17", profile.Age);
        Assert.Null(profile.Personality);
        Assert.Null(profile.Catchphrase);
    }

    [Fact]
    public void SetAvatar_stores_the_relative_path_and_can_clear_it()
    {
        var profile = CreateProfile();

        profile.SetAvatar("2026/08/avatar.png", Now.AddMinutes(1));
        Assert.Equal("2026/08/avatar.png", profile.AvatarRelativePath);

        profile.SetAvatar(null, Now.AddMinutes(2));
        Assert.Null(profile.AvatarRelativePath);
    }

    [Fact]
    public void Create_rejects_a_blank_canonical_name()
    {
        Assert.Throws<ArgumentException>(() => CharacterProfile.Create(
            Guid.NewGuid(), OwnerId, "   ", null, null, null, null, null, null, null, null, Now));
    }
}
