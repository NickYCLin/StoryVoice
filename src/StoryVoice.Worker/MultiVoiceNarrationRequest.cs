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
    int PauseBeforeMs);

public sealed record MultiVoiceNarrationRequest(IReadOnlyList<NarrationTurn> Turns);
