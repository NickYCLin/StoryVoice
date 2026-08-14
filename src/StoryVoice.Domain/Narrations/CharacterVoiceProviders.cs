namespace StoryVoice.Domain.Narrations;

/// <summary>
/// The <c>VoiceProvider</c> string values <see cref="StoryVoice.Domain.Series.SeriesCharacter"/> and
/// <see cref="StoryVoice.Domain.Series.StorySeries"/>'s narrator fields can carry, shared across the
/// Infrastructure (series/cast validation) and Worker (turn resolution, synthesis dispatch) projects
/// so the provider name is never duplicated as a bare string literal in more than one place.
/// </summary>
public static class CharacterVoiceProviders
{
    public const string Edge = "edge";

    public const string VoAi = "voai";

    /// <summary>The provider whose <see cref="StoryVoice.Domain.Narrations.CharacterVoiceProfile"/>-backed
    /// synthesis can also fall back to plain Edge voices turn-by-turn — see
    /// <c>ThreeWaVoxCpm2NarrationProvider</c> in the Worker project.</summary>
    public const string ThreeWaVoxCpm2 = "3wa-voxcpm2";
}
