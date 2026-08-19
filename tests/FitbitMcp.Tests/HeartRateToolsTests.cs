// SPDX-License-Identifier: MIT

using FitbitMcp.Tools;

namespace FitbitMcp.Tests;

[TestFixture]
public class HeartRateToolsTests
{
    [Test]
    public void ParseEntries_FlattensRollupDataPoints_ToHeartRateEntries()
    {
        const string rawJson = """
        {
          "rollupDataPoints": [
            {
              "startTime": "2026-08-01T00:00:00Z",
              "heartRate": { "beatsPerMinuteAvg": 68.5, "beatsPerMinuteMin": 52.0, "beatsPerMinuteMax": 142.0 }
            },
            {
              "startTime": "2026-08-02T00:00:00Z",
              "heartRate": { "beatsPerMinuteAvg": 71.2, "beatsPerMinuteMin": 55.0, "beatsPerMinuteMax": 138.0 }
            }
          ]
        }
        """;

        var entries = HeartRateTools.ParseEntries(rawJson);

        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries[0], Is.EqualTo(new HeartRateDay("2026-08-01", 68.5, 52.0, 142.0)));
        Assert.That(entries[1], Is.EqualTo(new HeartRateDay("2026-08-02", 71.2, 55.0, 138.0)));
    }

    [Test]
    public void ParseEntries_ReturnsEmptyList_WhenNoRollupDataPointsProperty()
    {
        var entries = HeartRateTools.ParseEntries("{}");

        Assert.That(entries, Is.Empty);
    }
}
