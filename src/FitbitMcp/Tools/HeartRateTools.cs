// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using FitbitMcp.Auth;
using ModelContextProtocol.Server;

namespace FitbitMcp.Tools;

[McpServerToolType]
public class HeartRateTools(GoogleHealthApi api)
{
    [McpServerTool(Name = "get_heart_rate_history")]
    [Description("Get daily average/min/max heart rate (beats per minute) from Google Health for a date range, " +
        "normalized to a flat list of { date, avgBpm, minBpm, maxBpm } entries. Defaults to the last 14 days if no " +
        "range is given - Google Health caps heart-rate queries at a 14-day range.")]
    public async Task<string> GetHeartRateHistory(
        [Description("Start date, yyyy-MM-dd format; defaults to 13 days before end date if omitted")] string? startDate = null,
        [Description("End date, yyyy-MM-dd format; defaults to today if omitted")] string? endDate = null,
        CancellationToken cancellationToken = default)
    {
        var end = endDate is not null ? DateOnly.Parse(endDate, CultureInfo.InvariantCulture) : DateOnly.FromDateTime(DateTime.UtcNow);
        var start = startDate is not null ? DateOnly.Parse(startDate, CultureInfo.InvariantCulture) : end.AddDays(-13);

        var rawJson = await api.GetRollupAsync("heart-rate", start, end, cancellationToken);
        return JsonSerializer.Serialize(ParseEntries(rawJson));
    }

    internal static List<HeartRateDay> ParseEntries(string rawJson)
    {
        var entries = new List<HeartRateDay>();

        foreach (var (date, value) in RollupParsing.Enumerate(rawJson, "heartRate"))
        {
            var avg = value.TryGetProperty("beatsPerMinuteAvg", out var avgEl) ? avgEl.GetDouble() : (double?)null;
            var min = value.TryGetProperty("beatsPerMinuteMin", out var minEl) ? minEl.GetDouble() : (double?)null;
            var max = value.TryGetProperty("beatsPerMinuteMax", out var maxEl) ? maxEl.GetDouble() : (double?)null;
            entries.Add(new HeartRateDay(date, avg, min, max));
        }

        return entries;
    }
}

public sealed record HeartRateDay(string Date, double? AvgBpm, double? MinBpm, double? MaxBpm);
