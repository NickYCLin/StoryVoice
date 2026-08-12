using System.Globalization;

namespace StoryVoice.Worker;

/// <summary>
/// Combines an edge-tts style signed delta string (e.g. "+3%", "-15Hz") with a second delta of the
/// same unit into one delta string, so a character's fixed base rate/pitch/volume and a per-turn
/// emotion adjustment can be layered without either side needing to know about the other's format.
/// </summary>
public static class SynthesisParameterMath
{
    public static string CombinePercent(string basePercent, string deltaPercent) =>
        Combine(basePercent, deltaPercent, "%");

    public static string CombineHz(string baseHz, string deltaHz) =>
        Combine(baseHz, deltaHz, "Hz");

    private static string Combine(string baseValue, string deltaValue, string unit)
    {
        var combined = ParseSigned(baseValue, unit) + ParseSigned(deltaValue, unit);
        return combined >= 0
            ? $"+{combined.ToString(CultureInfo.InvariantCulture)}{unit}"
            : $"{combined.ToString(CultureInfo.InvariantCulture)}{unit}";
    }

    private static int ParseSigned(string value, string unit)
    {
        ArgumentNullException.ThrowIfNull(value);
        var trimmed = value.Trim();
        if (trimmed.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^unit.Length];
        }

        return int.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }
}
