namespace StoryVoice.Application.Narrations.SpeechPlanning;

public enum SpeakerAttributionOutcome
{
    Confirmed,
    Suggested,
    Unknown
}

public enum SpeakerAttributionDecisionSource
{
    Rule,
    LocalModel
}

/// <summary>
/// A character identity the attribution provider is allowed to attribute segments to.
/// Both fields must already be normalized the same way as <c>SeriesIdentityNormalizer</c>.
/// </summary>
public sealed record KnownCharacterIdentity(
    Guid CharacterId,
    string NormalizedCanonicalName,
    IReadOnlyList<string> NormalizedAliases);

/// <summary>
/// One segment's text, resolved by the caller from chapter offsets. Never persisted by the
/// attribution provider itself — only <see cref="SpeakerAttributionResult"/> (offsets/IDs/reason
/// codes, no text) is expected to be logged or stored downstream.
/// </summary>
public sealed record SpeechSegmentAttributionInput(
    int Index,
    SpeechSegmentKind Kind,
    string Text);

public sealed record SpeakerAttributionRequest(
    IReadOnlyList<KnownCharacterIdentity> KnownCharacters,
    IReadOnlyList<SpeechSegmentAttributionInput> Segments,
    Guid? PointOfViewCharacterId = null);

public sealed record SpeakerAttributionResult(
    int SegmentIndex,
    Guid? CharacterId,
    SpeakerAttributionOutcome Outcome,
    int Confidence,
    SpeakerAttributionDecisionSource Source,
    string ReasonCode);

/// <summary>
/// Attributes a speaker to each Dialogue segment. Implementations must only ever return a
/// <c>CharacterId</c> that appears in <see cref="SpeakerAttributionRequest.KnownCharacters"/>,
/// and must only score <see cref="SpeechSegmentKind.Dialogue"/> segments (Narrator segments are
/// never attributed to a character). Implementations must not invent character names — an
/// unmatched or ambiguous segment must resolve to <see cref="SpeakerAttributionOutcome.Unknown"/>
/// or <see cref="SpeakerAttributionOutcome.Suggested"/>, never a fabricated identity.
/// </summary>
public interface ISpeakerAttributionProvider
{
    Task<IReadOnlyList<SpeakerAttributionResult>> AttributeAsync(
        SpeakerAttributionRequest request,
        CancellationToken cancellationToken);
}
