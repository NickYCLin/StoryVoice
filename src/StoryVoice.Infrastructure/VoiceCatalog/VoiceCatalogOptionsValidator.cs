using Microsoft.Extensions.Options;

namespace StoryVoice.Infrastructure.VoiceCatalog;

internal sealed class VoiceCatalogOptionsValidator : IValidateOptions<VoiceCatalogOptions>
{
    private static readonly IReadOnlySet<string> Iso3166Alpha2CountryCodes =
        new HashSet<string>(
            ("AD AE AF AG AI AL AM AO AQ AR AS AT AU AW AX AZ BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BV BW BY BZ "
            + "CA CC CD CF CG CH CI CK CL CM CN CO CR CU CV CW CX CY CZ DE DJ DK DM DO DZ EC EE EG EH ER ES ET FI FJ FK FM FO FR "
            + "GA GB GD GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HM HN HR HT HU ID IE IL IM IN IO IQ IR IS IT JE JM "
            + "JO JP KE KG KH KI KM KN KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD ME MF MG MH MK ML MM MN MO MP MQ "
            + "MR MS MT MU MV MW MX MY MZ NA NC NE NF NG NI NL NO NP NR NU NZ OM PA PE PF PG PH PK PL PM PN PR PS PT PW PY QA RE "
            + "RO RS RU RW SA SB SC SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SV SX SY SZ TC TD TF TG TH TJ TK TL TM TN TO TR TT "
            + "TV TW TZ UA UG UM US UY UZ VA VC VE VG VI VN VU WF WS YE YT ZA ZM ZW")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries),
            StringComparer.Ordinal);

    public ValidateOptionsResult Validate(string? name, VoiceCatalogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (options.MaximumDemoBytes is < 64 * 1024 or > VoiceCatalogOptions.DefaultMaximumDemoBytes)
        {
            failures.Add("Public voice catalog demo limit must be between 64 KiB and 3 MiB.");
        }

        if (options.Entries is null)
        {
            failures.Add("Public voice catalog entries collection is required.");
        }
        else
        {
            foreach (var (alias, entry) in options.Entries)
            {
                if (!IsCanonicalAlias(alias) || entry is null || !IsValidEntry(entry))
                {
                    failures.Add($"Public voice catalog entry '{alias}' is invalid.");
                }
            }
        }

        if (options.Enabled && !IsValidPrivateAssetRoot(options.AssetRootPath))
        {
            failures.Add("Enabled public voice catalog requires a non-root private asset directory.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    internal static bool IsCanonicalAlias(string value) =>
        value is { Length: >= 1 and <= 64 }
        && value[0] is >= 'a' and <= 'z' or >= '0' and <= '9'
        && value[^1] is >= 'a' and <= 'z' or >= '0' and <= '9'
        && value.All(character => character is >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '-');

    private static bool IsValidEntry(VoiceCatalogEntryOptions entry) =>
        IsJsonPath(entry.SyntheticVoiceAuthorizationRelativePath)
        && IsCanonicalSha256(entry.SyntheticVoiceAuthorizationSha256)
        && IsValidRelativePath(entry.GenerationManifestRelativePath)
        && IsValidRelativePath(entry.TermsSnapshotRelativePath)
        && IsWavePath(entry.DemoAudioRelativePath)
        && HasDistinctEvidencePaths(entry);

    private static bool HasDistinctEvidencePaths(VoiceCatalogEntryOptions entry)
    {
        var paths = new List<string>
        {
            entry.SyntheticVoiceAuthorizationRelativePath,
            entry.DemoAudioRelativePath,
            entry.GenerationManifestRelativePath,
            entry.TermsSnapshotRelativePath,
        };

        return paths.Distinct(StringComparer.Ordinal).Count() == paths.Count;
    }

    internal static bool IsCanonicalGrantIdentifier(string value) =>
        value is { Length: >= 1 and <= 128 }
        && value[0] is >= 'a' and <= 'z' or >= '0' and <= '9'
        && value.All(character => character is >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '.' or '_' or ':' or '-');

    internal static bool IsCountryCode(string value) =>
        value is { Length: 2 }
        && Iso3166Alpha2CountryCodes.Contains(value);

    private static bool IsJsonPath(string value) =>
        IsValidRelativePath(value)
        && string.Equals(Path.GetExtension(value), ".json", StringComparison.OrdinalIgnoreCase);

    private static bool IsWavePath(string value) =>
        IsValidRelativePath(value)
        && string.Equals(Path.GetExtension(value), ".wav", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidRelativePath(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && !Path.IsPathRooted(value)
        && value.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .All(segment => segment is not ("" or "." or ".."));

    internal static bool IsCanonicalSha256(string value) =>
        value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsValidPrivateAssetRoot(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(value);
            var pathRoot = Path.GetPathRoot(fullPath);
            return !string.IsNullOrWhiteSpace(pathRoot)
                && !string.Equals(
                    fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    pathRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or NotSupportedException)
        {
            return false;
        }
    }
}
