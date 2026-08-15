namespace StoryVoice.Worker;

/// <summary>
/// One synthesized turn in a multi-character narration job: either the series narrator or a
/// specific character's fixed voice fingerprint. <see cref="PauseBeforeMs"/> is the only pause
/// field — the silence that should play immediately before this turn's audio — so consecutive
/// turns never double-count a gap between them.
/// </summary>
public sealed record NarrationTurn(
    string Text,
    string Voice,
    string Rate,
    string Pitch,
    string Volume,
    int PauseBeforeMs);

/// <summary>
/// The immutable provider identity captured with a cast revision. Keeping this beside the turns
/// lets a version-pinned provider reject stale or mixed cast snapshots before it makes any
/// synthesis request.
/// </summary>
public sealed record NarrationProviderContract(string ProviderName, string ProviderVersion);

public sealed record MultiVoiceNarrationRequest(
    IReadOnlyList<NarrationTurn> Turns,
    NarrationProviderContract? NarratorProvider = null,
    IReadOnlyList<NarrationProviderContract>? CharacterProviders = null);
