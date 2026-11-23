using Openspace.Application.Voice;

namespace Openspace.Application.Tests;

public sealed class VoiceLanguagesTests
{
    [Theory]
    [InlineData("it", "it")]
    [InlineData(" EN ", "en")]
    [InlineData("ja", "ja")]
    [InlineData("fr", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Only_the_three_languages_Shiori_speaks_can_be_forced(string? asked, string? chosen) =>
        Assert.Equal(chosen, Languages.Choose(asked));
}
