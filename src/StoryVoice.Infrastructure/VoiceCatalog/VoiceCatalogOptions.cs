namespace StoryVoice.Infrastructure.VoiceCatalog;

public sealed class VoiceCatalogOptions
{
    public const string SectionName = "VoiceCatalog";
    public const int DefaultMaximumDemoBytes = 3 * 1024 * 1024;

    public bool Enabled { get; set; }

    public string AssetRootPath { get; set; } = "storage/voice-catalog-assets";

    public int MaximumDemoBytes { get; set; } = DefaultMaximumDemoBytes;

    public Dictionary<string, VoiceCatalogEntryOptions> Entries { get; set; } = [];
}

public sealed class VoiceCatalogEntryOptions
{
    public string SyntheticVoiceAuthorizationRelativePath { get; set; } = string.Empty;

    public string SyntheticVoiceAuthorizationSha256 { get; set; } = string.Empty;

    public string GenerationManifestRelativePath { get; set; } = string.Empty;

    public string TermsSnapshotRelativePath { get; set; } = string.Empty;

    public string DemoAudioRelativePath { get; set; } = string.Empty;
}
