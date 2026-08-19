// SPDX-License-Identifier: MIT

using FitbitMcp.Tools;

namespace FitbitMcp.Tests;

[TestFixture]
public class BodyFatToolsTests
{
    [Test]
    public void ParseEntries_FlattensRollupDataPoints_ToBodyFatEntries()
    {
        const string rawJson = """
        {
          "rollupDataPoints": [
            {
              "startTime": "2026-08-01T00:00:00Z",
              "endTime": "2026-08-02T00:00:00Z",
              "bodyFat": { "bodyFatPercentageAvg": 23.4 }
            },
            {
              "startTime": "2026-08-02T00:00:00Z",
              "endTime": "2026-08-03T00:00:00Z",
              "bodyFat": { "bodyFatPercentageAvg": 23.1 }
            }
          ]
        }
        """;

        var entries = BodyFatTools.ParseEntries(rawJson);

        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries[0], Is.EqualTo(new BodyFatEntry("2026-08-01", 23.4)));
        Assert.That(entries[1], Is.EqualTo(new BodyFatEntry("2026-08-02", 23.1)));
    }

    [Test]
    public void ParseEntries_SkipsWindowsWithoutBodyFat()
    {
        const string rawJson = """
        { "rollupDataPoints": [ { "startTime": "2026-08-01T00:00:00Z", "endTime": "2026-08-02T00:00:00Z" } ] }
        """;

        var entries = BodyFatTools.ParseEntries(rawJson);

        Assert.That(entries, Is.Empty);
    }

    [Test]
    public void ParseEntries_ReturnsEmptyList_WhenNoRollupDataPointsProperty()
    {
        var entries = BodyFatTools.ParseEntries("{}");

        Assert.That(entries, Is.Empty);
    }
}
