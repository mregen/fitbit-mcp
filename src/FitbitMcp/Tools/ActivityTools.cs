// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using FitbitMcp.Auth;
using ModelContextProtocol.Server;

namespace FitbitMcp.Tools;

[McpServerToolType]
public class ActivityTools(GoogleHealthApi api)
{
    [McpServerTool(Name = "get_activity_summary")]
    [Description("Get daily step count, total calories burned, and active minutes from Google Health (e.g. from a " +
        "Fitbit tracker) for a date range, normalized to a flat list of { date, steps, totalCalories, activeMinutes } " +
        "entries. Defaults to the last 14 days if no range is given - Google Health caps calories/active-minutes " +
        "queries at a 14-day range.")]
    public async Task<string> GetActivitySummary(
        [Description("Start date, yyyy-MM-dd format; defaults to 13 days before end date if omitted")] string? startDate = null,
        [Description("End date, yyyy-MM-dd format; defaults to today if omitted")] string? endDate = null,
        CancellationToken cancellationToken = default)
    {
        var end = endDate is not null ? DateOnly.Parse(endDate, CultureInfo.InvariantCulture) : DateOnly.FromDateTime(DateTime.UtcNow);
        var start = startDate is not null ? DateOnly.Parse(startDate, CultureInfo.InvariantCulture) : end.AddDays(-13);

        var stepsTask = api.GetRollupAsync("steps", start, end, cancellationToken);
        var caloriesTask = api.GetRollupAsync("total-calories", start, end, cancellationToken);
        var activeMinutesTask = api.GetRollupAsync("active-minutes", start, end, cancellationToken);
        await Task.WhenAll(stepsTask, caloriesTask, activeMinutesTask);

        var merged = MergeEntries(await stepsTask, await caloriesTask, await activeMinutesTask);
        return JsonSerializer.Serialize(merged);
    }

    /// <summary>
    /// Merges three separate rollup responses (steps/total-calories/active-minutes each require their own
    /// dataPoints:rollUp call - Google Health has no multi-type rollup request) into one per-day summary.
    /// active-minutes is itself a breakdown by activity level (LIGHT/MODERATE/VIGOROUS); this sums all
    /// levels into a single total, since a simple daily total is what's useful for an LLM summary.
    /// </summary>
    internal static List<ActivityDay> MergeEntries(string stepsJson, string caloriesJson, string activeMinutesJson)
    {
        var byDate = new SortedDictionary<string, ActivityDay>(StringComparer.Ordinal);

        foreach (var (date, value) in RollupParsing.Enumerate(stepsJson, "steps"))
        {
            if (value.TryGetProperty("countSum", out var countSum)
                && long.TryParse(countSum.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var steps))
            {
                byDate[date] = GetOrCreate(byDate, date) with { Steps = steps };
            }
        }

        foreach (var (date, value) in RollupParsing.Enumerate(caloriesJson, "totalCalories"))
        {
            if (value.TryGetProperty("kcalSum", out var kcalSum))
            {
                byDate[date] = GetOrCreate(byDate, date) with { TotalCalories = kcalSum.GetDouble() };
            }
        }

        foreach (var (date, value) in RollupParsing.Enumerate(activeMinutesJson, "activeMinutes"))
        {
            var total = 0L;
            if (value.TryGetProperty("activeMinutesRollupByActivityLevel", out var byLevel))
            {
                foreach (var level in byLevel.EnumerateArray())
                {
                    if (level.TryGetProperty("activeMinutesSum", out var sum)
                        && long.TryParse(sum.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
                    {
                        total += minutes;
                    }
                }
            }

            byDate[date] = GetOrCreate(byDate, date) with { ActiveMinutes = total };
        }

        return byDate.Values.ToList();
    }

    private static ActivityDay GetOrCreate(SortedDictionary<string, ActivityDay> byDate, string date) =>
        byDate.TryGetValue(date, out var existing) ? existing : new ActivityDay(date, null, null, null);
}

public sealed record ActivityDay(string Date, long? Steps, double? TotalCalories, long? ActiveMinutes);
