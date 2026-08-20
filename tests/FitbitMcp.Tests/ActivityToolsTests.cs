// SPDX-License-Identifier: MIT

using FitbitMcp.Tools;

namespace FitbitMcp.Tests;

[TestFixture]
public class ActivityToolsTests
{
    [Test]
    public void MergeEntries_CombinesStepsCaloriesActiveMinutesDistanceAndActiveZoneMinutes_ByDate()
    {
        const string stepsJson = """
        { "rollupDataPoints": [
            { "startTime": "2026-08-01T00:00:00Z", "steps": { "countSum": "8342" } },
            { "startTime": "2026-08-02T00:00:00Z", "steps": { "countSum": "10501" } }
        ] }
        """;
        const string caloriesJson = """
        { "rollupDataPoints": [
            { "startTime": "2026-08-01T00:00:00Z", "totalCalories": { "kcalSum": 2450.5 } },
            { "startTime": "2026-08-02T00:00:00Z", "totalCalories": { "kcalSum": 2680.0 } }
        ] }
        """;
        const string activeMinutesJson = """
        { "rollupDataPoints": [
            { "startTime": "2026-08-01T00:00:00Z", "activeMinutes": { "activeMinutesRollupByActivityLevel": [
                { "activityLevel": "LIGHT", "activeMinutesSum": "30" },
                { "activityLevel": "MODERATE", "activeMinutesSum": "15" }
            ] } }
        ] }
        """;
        const string distanceJson = """
        { "rollupDataPoints": [
            { "startTime": "2026-08-01T00:00:00Z", "distance": { "millimetersSum": "6200000" } }
        ] }
        """;
        const string activeZoneMinutesJson = """
        { "rollupDataPoints": [
            { "startTime": "2026-08-01T00:00:00Z", "activeZoneMinutes": {
                "sumInPeakHeartZone": "5", "sumInFatBurnHeartZone": "20", "sumInCardioHeartZone": "10"
            } }
        ] }
        """;

        var entries = ActivityTools.MergeEntries(
            stepsJson, caloriesJson, activeMinutesJson, distanceJson, activeZoneMinutesJson,
            "{}", "{}", "{}", "{}");

        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries[0], Is.EqualTo(new ActivityDay("2026-08-01", 8342, 2450.5, 45, 6.2, 35, null, null, null, null)));
        Assert.That(entries[1], Is.EqualTo(new ActivityDay("2026-08-02", 10501, 2680.0, null, null, null, null, null, null, null)));
    }

    [Test]
    public void MergeEntries_CombinesFloorsActiveEnergySedentaryPeriodAndCaloriesInHeartRateZone_ByDate()
    {
        const string floorsJson = """
        { "rollupDataPoints": [ { "startTime": "2026-08-01T00:00:00Z", "floors": { "countSum": "9" } } ] }
        """;
        const string activeEnergyBurnedJson = """
        { "rollupDataPoints": [ { "startTime": "2026-08-01T00:00:00Z", "activeEnergyBurned": { "kcalSum": 540.25 } } ] }
        """;
        const string sedentaryPeriodJson = """
        { "rollupDataPoints": [ { "startTime": "2026-08-01T00:00:00Z", "sedentaryPeriod": { "durationSum": "3630s" } } ] }
        """;
        const string caloriesInHeartRateZoneJson = """
        { "rollupDataPoints": [ { "startTime": "2026-08-01T00:00:00Z", "caloriesInHeartRateZone": { "caloriesInHeartRateZones": [
            { "zoneType": "FAT_BURN", "kcal": 120.5 },
            { "zoneType": "CARDIO", "kcal": 60.0 }
        ] } } ] }
        """;

        var entries = ActivityTools.MergeEntries(
            "{}", "{}", "{}", "{}", "{}",
            floorsJson, activeEnergyBurnedJson, sedentaryPeriodJson, caloriesInHeartRateZoneJson);

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0], Is.EqualTo(new ActivityDay("2026-08-01", null, null, null, null, null, 9, 540.25, 60.5, 180.5)));
    }

    [Test]
    public void MergeEntries_ReturnsEmptyList_WhenAllInputsEmpty()
    {
        var entries = ActivityTools.MergeEntries("{}", "{}", "{}", "{}", "{}", "{}", "{}", "{}", "{}");

        Assert.That(entries, Is.Empty);
    }
}
