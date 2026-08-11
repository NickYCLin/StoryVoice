namespace StoryVoice.Infrastructure.Narrations;

public sealed class NarrationAdmissionOptions
{
    public const string SectionName = "Narration";

    /// <summary>
    /// Gates the only admission paths that create new narration artifacts. Existing jobs stay
    /// runnable, readable, and cancellable while the gate is closed.
    /// </summary>
    public bool AdmissionEnabled { get; set; }
}
