// SPDX-License-Identifier: MIT

using FitbitMcp.Tools;

namespace FitbitMcp.Tests;

[TestFixture]
public class ExerciseToolsTests
{
    [Test]
    public void ParseEntries_FlattensDataPoints_ToExerciseSessions()
    {
        const string rawJson = """
        {
          "dataPoints": [
            {
              "name": "users/me/dataTypes/exercise/dataPoints/2026443605080188808",
              "exercise": {
                "displayName": "Running",
                "exerciseType": "RUNNING",
                "interval": { "startTime": "2026-08-15T06:30:00Z", "endTime": "2026-08-15T07:15:00Z" },
                "activeDuration": "2700s",
                "metricsSummary": {
                  "distanceMillimeters": 8500000,
                  "caloriesKcal": 620.5,
                  "averageHeartRateBeatsPerMinute": "148",
                  "averagePaceSecondsPerMeter": 0.3176,
                  "elevationGainMillimeters": 45000,
                  "steps": "9800"
                },
                "exerciseMetadata": { "hasGps": true }
              }
            }
          ]
        }
        """;

        var entries = ExerciseTools.ParseEntries(rawJson);

        Assert.That(entries, Has.Count.EqualTo(1));
        var entry = entries[0];
        Assert.That(entry.Date, Is.EqualTo("2026-08-15"));
        Assert.That(entry.ExerciseId, Is.EqualTo("2026443605080188808"));
        Assert.That(entry.DisplayName, Is.EqualTo("Running"));
        Assert.That(entry.ExerciseType, Is.EqualTo("RUNNING"));
        Assert.That(entry.DurationMinutes, Is.EqualTo(45.0));
        Assert.That(entry.DistanceKm, Is.EqualTo(8.5));
        Assert.That(entry.CaloriesKcal, Is.EqualTo(620.5));
        Assert.That(entry.AverageHeartRateBpm, Is.EqualTo(148));
        Assert.That(entry.AveragePaceMinPerKm!.Value, Is.EqualTo(5.293333).Within(0.0001));
        Assert.That(entry.ElevationGainMeters, Is.EqualTo(45.0));
        Assert.That(entry.Steps, Is.EqualTo(9800));
        Assert.That(entry.HasGps, Is.True);
    }

    [Test]
    public void ParseEntries_FallsBackToIntervalSpan_WhenActiveDurationAbsent()
    {
        const string rawJson = """
        {
          "dataPoints": [
            {
              "exercise": {
                "displayName": "Free Weights",
                "exerciseType": "FREE_WEIGHTS",
                "interval": { "startTime": "2026-08-10T18:00:00Z", "endTime": "2026-08-10T18:50:00Z" }
              }
            }
          ]
        }
        """;

        var entries = ExerciseTools.ParseEntries(rawJson);

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].DurationMinutes, Is.EqualTo(50.0));
        Assert.That(entries[0].DistanceKm, Is.Null);
        Assert.That(entries[0].HasGps, Is.Null);
    }

    [Test]
    public void ParseEntries_SkipsDataPointsWithoutInterval()
    {
        const string rawJson = """
        { "dataPoints": [ { "exercise": { "displayName": "Running" } } ] }
        """;

        var entries = ExerciseTools.ParseEntries(rawJson);

        Assert.That(entries, Is.Empty);
    }

    [Test]
    public void ParseEntries_ReturnsEmptyList_WhenNoDataPointsProperty()
    {
        var entries = ExerciseTools.ParseEntries("{}");

        Assert.That(entries, Is.Empty);
    }
}
