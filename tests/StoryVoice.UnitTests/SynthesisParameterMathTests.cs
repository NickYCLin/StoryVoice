using StoryVoice.Worker;

namespace StoryVoice.UnitTests;

public sealed class SynthesisParameterMathTests
{
    [Theory]
    [InlineData("+3%", "+8%", "+11%")]
    [InlineData("-5%", "+10%", "+5%")]
    [InlineData("+0%", "+0%", "+0%")]
    [InlineData("-10%", "-8%", "-18%")]
    public void CombinePercent_sums_signed_percentages(string basePercent, string deltaPercent, string expected)
    {
        Assert.Equal(expected, SynthesisParameterMath.CombinePercent(basePercent, deltaPercent));
    }

    [Theory]
    [InlineData("+20Hz", "-5Hz", "+15Hz")]
    [InlineData("-15Hz", "+10Hz", "-5Hz")]
    [InlineData("+0Hz", "+0Hz", "+0Hz")]
    public void CombineHz_sums_signed_hertz_deltas(string baseHz, string deltaHz, string expected)
    {
        Assert.Equal(expected, SynthesisParameterMath.CombineHz(baseHz, deltaHz));
    }
}
