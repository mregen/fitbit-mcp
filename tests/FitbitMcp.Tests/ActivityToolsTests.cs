// SPDX-License-Identifier: MIT

using FitbitMcp.Tools;

namespace FitbitMcp.Tests;

[TestFixture]
public class ActivityToolsTests
{
    [Test]
    public void MergeEntries_CombinesStepsCaloriesAndActiveMinutes_ByDate()
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

        var entries = ActivityTools.MergeEntries(stepsJson, caloriesJson, activeMinutesJson);

        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries[0], Is.EqualTo(new ActivityDay("2026-08-01", 8342, 2450.5, 45)));
        Assert.That(entries[1], Is.EqualTo(new ActivityDay("2026-08-02", 10501, 2680.0, null)));
    }

    [Test]
    public void MergeEntries_ReturnsEmptyList_WhenAllInputsEmpty()
    {
        var entries = ActivityTools.MergeEntries("{}", "{}", "{}");

        Assert.That(entries, Is.Empty);
    }
}
