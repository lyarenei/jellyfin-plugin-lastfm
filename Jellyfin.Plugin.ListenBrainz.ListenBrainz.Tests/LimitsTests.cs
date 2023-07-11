using System;
using Jellyfin.Plugin.ListenBrainz.ListenBrainz.Exceptions;
using Jellyfin.Plugin.Listenbrainz.ListenBrainz.Resources;
using Xunit;

namespace Jellyfin.Plugin.ListenBrainz.ListenBrainz.Tests;

public class LimitsTests
{
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(30, 40, false)]
    [InlineData(30, 90, true)]
    [InlineData(4 * TimeSpan.TicksPerMinute, TimeSpan.TicksPerHour, false)]
    public void ListenBrainzLimits_EvaluateSubmitConditions(long position, long runtime, bool throws)
    {
        if (throws)
        {
            Assert.Throws<ListenBrainzException>(() => Limits.AssertSubmitConditions(position, runtime));
        }
        else
        {
            Limits.AssertSubmitConditions(position, runtime);
        }
    }
}
