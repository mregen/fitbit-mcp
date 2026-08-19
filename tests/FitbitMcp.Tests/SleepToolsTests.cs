// SPDX-License-Identifier: MIT

using FitbitMcp.Tools;

namespace FitbitMcp.Tests;

[TestFixture]
public class SleepToolsTests
{
    [Test]
    public void ParseEntries_FlattensDataPoints_ToSleepNights()
    {
        const string rawJson = """
        {
          "dataPoints": [
            {
              "sleep": {
                "interval": { "startTime": "2026-08-01T23:10:00Z", "endTime": "2026-08-02T06:45:00Z" },
                "summary": { "minutesAsleep": "410", "minutesAwake": "15", "minutesInSleepPeriod": "455" }
              }
            },
            {
              "sleep": {
                "interval": { "startTime": "2026-08-02T23:30:00Z", "endTime": "2026-08-03T07:00:00Z" },
                "summary": { "minutesAsleep": "425", "minutesAwake": "10", "minutesInSleepPeriod": "450" }
              }
            }
          ]
        }
        """;

        var entries = SleepTools.ParseEntries(rawJson);

        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries[0], Is.EqualTo(new SleepNight("2026-08-02", 410, 15, 455)));
        Assert.That(entries[1], Is.EqualTo(new SleepNight("2026-08-03", 425, 10, 450)));
    }

    [Test]
    public void ParseEntries_SkipsDataPointsWithoutSummary()
    {
        const string rawJson = """
        { "dataPoints": [ { "sleep": { "interval": { "startTime": "2026-08-01T23:00:00Z", "endTime": "2026-08-02T06:00:00Z" } } } ] }
        """;

        var entries = SleepTools.ParseEntries(rawJson);

        Assert.That(entries, Is.Empty);
    }

    [Test]
    public void ParseEntries_ReturnsEmptyList_WhenNoDataPointsProperty()
    {
        var entries = SleepTools.ParseEntries("{}");

        Assert.That(entries, Is.Empty);
    }
}
