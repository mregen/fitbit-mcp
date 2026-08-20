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
        "to a flat list of { date, minutesAsleep, minutesAwake, minutesInBed, isMainSleep } entries - date is the " +
        "civil date the sleep session ended (i.e. the wake-up date). By default only each day's main sleep is " +
        "returned (naps excluded) so one entry per date is the common case; pass includeNaps to get every session, " +
        "which can add a second same-date entry (isMainSleep: false) on days with both a nap and a main sleep. " +
        "Google Health caps this at 25 sessions per call, most recent first, so a month with more nights than that " +
        "returns only the most recent 25.")]
    public async Task<string> GetSleepHistory(
        [Description("Any date within the target month, yyyy-MM-dd format; defaults to the current month if omitted")] string? date = null,
        [Description("Include naps alongside each day's main sleep; default false returns only the main sleep per day")] bool includeNaps = false,
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
        var entries = ParseEntries(rawJson);
        if (!includeNaps)
        {
            entries = entries.Where(entry => entry.IsMainSleep != false).ToList();
        }

        return JsonSerializer.Serialize(entries);
    }

    /// <summary>
    /// Parses a ListDataPointsResponse ({ dataPoints: [{ sleep: { interval, summary, metadata } }] }) - sleep is a
    /// session data type, not a rollup type, so it comes back as raw DataPoint entries via dataPoints.list rather
    /// than dataPoints:rollUp. metadata.mainSleep/metadata.nap (SleepMetadata, confirmed via the live discovery doc)
    /// resolve the ambiguity from #17, where a day with both a nap and a main sleep returned two same-date entries
    /// with nothing distinguishing them.
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
                TryReadMinutes(summary, "minutesInSleepPeriod"),
                TryReadIsMainSleep(sleep)));
        }

        return entries;
    }

    private static long? TryReadMinutes(JsonElement summary, string propertyName) =>
        summary.TryGetProperty(propertyName, out var element)
            && long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            ? minutes
            : null;

    /// <summary>
    /// mainSleep is only reliably populated once a stages-processing pass has run (metadata.stagesStatus), so a
    /// missing field is treated as unknown (null) rather than assumed false - falling back to metadata.nap (the
    /// inverse signal, present even without stages) when mainSleep itself isn't present.
    /// </summary>
    private static bool? TryReadIsMainSleep(JsonElement sleep)
    {
        if (!sleep.TryGetProperty("metadata", out var metadata))
        {
            return null;
        }

        if (metadata.TryGetProperty("mainSleep", out var mainSleep) && mainSleep.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return mainSleep.GetBoolean();
        }

        if (metadata.TryGetProperty("nap", out var nap) && nap.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return !nap.GetBoolean();
        }

        return null;
    }
}

public sealed record SleepNight(string Date, long? MinutesAsleep, long? MinutesAwake, long? MinutesInBed, bool? IsMainSleep);
