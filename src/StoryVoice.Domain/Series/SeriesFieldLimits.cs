using System.Globalization;
using System.Text;

namespace StoryVoice.Domain.Series;

internal static class SeriesFieldLimits
{
    internal const int SeriesName = 200;
    internal const int CharacterName = 200;
    internal const int Alias = 200;
    internal const int VolumeLabel = 100;
    internal const int Provider = 50;
    internal const int Voice = 200;
    internal const int SynthesisParameter = 50;
    internal const int Notes = 2_000;
    internal const int MaximumSpeakerPauseMs = 60_000;
    internal const int MaximumSortOrder = 1_000_000;
}

internal static class SeriesValueValidator
{
    internal static string NormalizeIdentifier(
        string? value,
        int maximumLength,
        string parameterName)
    {
        var normalized = NormalizeAndValidate(value, maximumLength, parameterName);
        var builder = new StringBuilder(normalized.Length);
        var pendingSpace = false;
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(rune.ToString());
        }

        var result = builder.ToString();
        EnsureVisible(result, parameterName);
        EnsureMaximumLength(result, maximumLength, parameterName);
        return result;
    }

    internal static string NormalizePrintable(
        string? value,
        int maximumLength,
        string parameterName)
    {
        var normalized = NormalizeAndValidate(value, maximumLength, parameterName).Trim();
        EnsureVisible(normalized, parameterName);
        EnsureMaximumLength(normalized, maximumLength, parameterName);
        return normalized;
    }

    internal static string? NormalizeOptionalPrintable(
        string? value,
        int maximumLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return NormalizePrintable(value, maximumLength, parameterName);
    }

    private static string NormalizeAndValidate(
        string? value,
        int maximumLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("欄位不可為空白。", parameterName);
        }

        string normalized;
        try
        {
            normalized = value.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException("欄位包含無效的 Unicode 字元。", parameterName, exception);
        }

        foreach (var rune in normalized.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format)
            {
                throw new ArgumentException("欄位包含不允許的控制或格式字元。", parameterName);
            }
        }

        EnsureMaximumLength(normalized, maximumLength, parameterName);
        return normalized;
    }

    private static void EnsureVisible(string value, string parameterName)
    {
        var hasVisibleContent = value.EnumerateRunes().Any(rune =>
        {
            var category = Rune.GetUnicodeCategory(rune);
            return !Rune.IsWhiteSpace(rune)
                && category is not UnicodeCategory.NonSpacingMark
                && category is not UnicodeCategory.SpacingCombiningMark
                && category is not UnicodeCategory.EnclosingMark;
        });
        if (!hasVisibleContent)
        {
            throw new ArgumentException("欄位必須包含可見字元。", parameterName);
        }
    }

    private static void EnsureMaximumLength(
        string value,
        int maximumLength,
        string parameterName)
    {
        if (value.Length > maximumLength)
        {
            throw new ArgumentException($"欄位不可超過 {maximumLength} 個字元。", parameterName);
        }
    }
}
