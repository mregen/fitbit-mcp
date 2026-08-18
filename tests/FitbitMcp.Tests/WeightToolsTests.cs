// SPDX-License-Identifier: MIT

using FitbitMcp.Tools;

namespace FitbitMcp.Tests;

[TestFixture]
public class WeightToolsTests
{
    [Test]
    public void ParseEntries_FlattensRollupDataPoints_ToWeightEntries()
    {
        const string rawJson = """
        {
          "rollupDataPoints": [
            {
              "startTime": "2026-08-01T00:00:00Z",
              "endTime": "2026-08-02T00:00:00Z",
              "weight": { "weightGramsAvg": 82300.0 }
            },
            {
              "startTime": "2026-08-02T00:00:00Z",
              "endTime": "2026-08-03T00:00:00Z",
              "weight": { "weightGramsAvg": 82100.0 }
            }
          ]
        }
        """;

        var entries = WeightTools.ParseEntries(rawJson);

        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries[0], Is.EqualTo(new WeightEntry("2026-08-01", 82.3)));
        Assert.That(entries[1], Is.EqualTo(new WeightEntry("2026-08-02", 82.1)));
    }

    [Test]
    public void ParseEntries_SkipsWindowsWithoutWeight()
    {
        const string rawJson = """
        { "rollupDataPoints": [ { "startTime": "2026-08-01T00:00:00Z", "endTime": "2026-08-02T00:00:00Z" } ] }
        """;

        var entries = WeightTools.ParseEntries(rawJson);

        Assert.That(entries, Is.Empty);
    }

    [Test]
    public void ParseEntries_ReturnsEmptyList_WhenNoRollupDataPointsProperty()
    {
        var entries = WeightTools.ParseEntries("{}");

        Assert.That(entries, Is.Empty);
    }
}
