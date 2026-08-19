// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using FitbitMcp.Auth;
using ModelContextProtocol.Server;

namespace FitbitMcp.Tools;

[McpServerToolType]
public class SleepTools(GoogleHealthApi api)
{
    [McpServerTool(Name = "get_sleep_history")]
    [Description("Get sleep sessions recorded in Google Health for the month containing the given date, normalized " +
        "to a flat list of { date, minutesAsleep, minutesAwake, minutesInBed } entries - date is the civil date the " +
        "sleep session ended (i.e. the wake-up date). Google Health caps this at 25 sessions per call, most recent " +
        "first, so a month with more nights than that returns only the most recent 25.")]
    public async Task<string> GetSleepHistory(
        [Description("Any date within the target month, yyyy-MM-dd format; defaults to the current month if omitted")] string? date = null,
        CancellationToken cancellationToken = default)
    {
        var anyDayInMonth = date is not null
            ? DateOnly.Parse(date, CultureInfo.InvariantCulture)
            : DateOnly.FromDateTime(DateTime.UtcNow);
        var start = new DateOnly(anyDayInMonth.Year, anyDayInMonth.Month, 1);
        var end = start.AddMonths(1);

        var filter =
            $"sleep.interval.civil_end_time >= \"{start:yyyy-MM-dd}\" AND sleep.interval.civil_end_time < \"{end:yyyy-MM-dd}\"";

        var rawJson = await api.ListDataPointsAsync("sleep", filter, cancellationToken);
        return JsonSerializer.Serialize(ParseEntries(rawJson));
    }

    /// <summary>
    /// Parses a ListDataPointsResponse ({ dataPoints: [{ sleep: { interval, summary } }] }) - sleep is a session
    /// data type, not a rollup type, so it comes back as raw DataPoint entries via dataPoints.list rather than
    /// dataPoints:rollUp.
    /// </summary>
    internal static List<SleepNight> ParseEntries(string rawJson)
    {
        var entries = new List<SleepNight>();
        using var document = JsonDocument.Parse(rawJson);

        if (!document.RootElement.TryGetProperty("dataPoints", out var dataPoints))
        {
            return entries;
        }

        foreach (var dataPoint in dataPoints.EnumerateArray())
        {
            if (!dataPoint.TryGetProperty("sleep", out var sleep)
                || !sleep.TryGetProperty("interval", out var interval)
                || !interval.TryGetProperty("endTime", out var endTimeElement)
                || !DateTimeOffset.TryParse(endTimeElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var endTime)
                || !sleep.TryGetProperty("summary", out var summary))
            {
                continue;
            }

            var isoDate = DateOnly.FromDateTime(endTime.UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            entries.Add(new SleepNight(
                isoDate,
                TryReadMinutes(summary, "minutesAsleep"),
                TryReadMinutes(summary, "minutesAwake"),
                TryReadMinutes(summary, "minutesInSleepPeriod")));
        }

        return entries;
    }

    private static long? TryReadMinutes(JsonElement summary, string propertyName) =>
        summary.TryGetProperty(propertyName, out var element)
            && long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            ? minutes
            : null;
}

public sealed record SleepNight(string Date, long? MinutesAsleep, long? MinutesAwake, long? MinutesInBed);
