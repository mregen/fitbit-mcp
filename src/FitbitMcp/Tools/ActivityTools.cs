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
    [Description("Get daily step count, total calories burned, active minutes, distance, and active zone minutes " +
        "from Google Health (e.g. from a Fitbit tracker) for a date range, normalized to a flat list of " +
        "{ date, steps, totalCalories, activeMinutes, distanceKm, activeZoneMinutes } entries. Defaults to the last " +
        "14 days if no range is given - Google Health caps calories/active-minutes queries at a 14-day range.")]
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
        var distanceTask = api.GetRollupAsync("distance", start, end, cancellationToken);
        var activeZoneMinutesTask = api.GetRollupAsync("active-zone-minutes", start, end, cancellationToken);
        await Task.WhenAll(stepsTask, caloriesTask, activeMinutesTask, distanceTask, activeZoneMinutesTask);

        var merged = MergeEntries(await stepsTask, await caloriesTask, await activeMinutesTask, await distanceTask, await activeZoneMinutesTask);
        return JsonSerializer.Serialize(merged);
    }

    /// <summary>
    /// Merges five separate rollup responses (steps/total-calories/active-minutes/distance/active-zone-minutes each
    /// require their own dataPoints:rollUp call - Google Health has no multi-type rollup request) into one per-day
    /// summary. active-minutes and active-zone-minutes are each themselves a breakdown (by activity level, and by
    /// heart-rate zone respectively); both are summed into a single daily total, since a simple daily total is what's
    /// useful for an LLM summary. Distance comes back in millimeters and is converted to km.
    /// </summary>
    internal static List<ActivityDay> MergeEntries(string stepsJson, string caloriesJson, string activeMinutesJson, string distanceJson, string activeZoneMinutesJson)
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

        foreach (var (date, value) in RollupParsing.Enumerate(distanceJson, "distance"))
        {
            if (value.TryGetProperty("millimetersSum", out var millimetersSum)
                && long.TryParse(millimetersSum.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var millimeters))
            {
                byDate[date] = GetOrCreate(byDate, date) with { DistanceKm = millimeters / 1_000_000.0 };
            }
        }

        foreach (var (date, value) in RollupParsing.Enumerate(activeZoneMinutesJson, "activeZoneMinutes"))
        {
            long total = 0;
            var any = false;
            foreach (var fieldName in new[] { "sumInPeakHeartZone", "sumInFatBurnHeartZone", "sumInCardioHeartZone" })
            {
                if (value.TryGetProperty(fieldName, out var sum)
                    && long.TryParse(sum.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
                {
                    total += minutes;
                    any = true;
                }
            }

            if (any)
            {
                byDate[date] = GetOrCreate(byDate, date) with { ActiveZoneMinutes = total };
            }
        }

        return byDate.Values.ToList();
    }

    private static ActivityDay GetOrCreate(SortedDictionary<string, ActivityDay> byDate, string date) =>
        byDate.TryGetValue(date, out var existing) ? existing : new ActivityDay(date, null, null, null, null, null);
}

public sealed record ActivityDay(string Date, long? Steps, double? TotalCalories, long? ActiveMinutes, double? DistanceKm, long? ActiveZoneMinutes);
