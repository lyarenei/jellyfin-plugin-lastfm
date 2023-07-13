using ListenBrainzPlugin.MusicBrainzApi.Extensions;
using Xunit;

namespace ListenBrainzPlugin.MusicBrainzApi.Tests;

public class ExtensionsTests
{
    [Theory]
    [InlineData("kebabCase", "kebab-case")]
    [InlineData("Kebabcase", "kebabcase")]
    [InlineData("KebAbcAse", "keb-abc-ase")]
    [InlineData("KebabcasE", "kebabcas-e")]
    public void Extensions_ConvertToKebabCase(string input, string expected)
    {
        Assert.Equal(expected, input.ToKebabCase());
    }
}
