using Tiel.Web.Services;

namespace Tiel.Web.Tests.Services;

public sealed class TitleServiceTests
{
    [Theory]
    [InlineData("Trip to Lisbon", "Trip to Lisbon")]
    [InlineData("Title: Trip to Lisbon", "Trip to Lisbon")]
    [InlineData("**Title:** \"Trip to Lisbon.\"", "Trip to Lisbon")]
    [InlineData("\n\n  “Trip   to\tLisbon!”  \nSecond line", "Trip to Lisbon")]
    [InlineData("'Don't forget the milk'", "Don't forget the milk")]
    [InlineData("title: Planning a trip...", "Planning a trip")]
    [InlineData("   \n  ", "")]
    [InlineData("`Reverse String Function`", "Reverse String Function")]
    [InlineData("```python\ndef reverse(s):\n    return s[::-1]\n```", "")]
    public void Cleans_the_model_output(string raw, string expected)
    {
        Assert.Equal(expected, TitleService.Clean(raw));
    }

    [Fact]
    public void Caps_the_title_at_60_characters()
    {
        var title = TitleService.Clean(string.Join(' ', Enumerable.Repeat("word", 30)));

        Assert.True(title.Length <= 60);
        Assert.StartsWith("word word", title, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Short question", "Short question")]
    [InlineData("Line one\nline two", "Line one line two")]
    [InlineData("01234567890123456789012345678901234567890123456789", "01234567890123456789012345678901234567890123456789")]
    [InlineData("01234567890123456789012345678901234567890123456789X", "01234567890123456789012345678901234567890123456789…")]
    public void The_fallback_is_the_first_50_characters_with_an_ellipsis_when_cut(string message, string expected)
    {
        Assert.Equal(expected, TitleService.Fallback(message));
    }
}
