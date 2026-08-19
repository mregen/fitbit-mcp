// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using FitbitMcp.Auth;
using ModelContextProtocol.Server;

namespace FitbitMcp.Tools;

[McpServerToolType]
public class BodyFatTools(GoogleHealthApi api)
{
    [McpServerTool(Name = "get_body_fat_history")]
    [Description("Get body-fat percentage entries recorded in Google Health for the month containing the given date, " +
        "normalized to a flat list of { date, bodyFatPercent } entries. Same OAuth scope as get_weight_history " +
        "(health_metrics_and_measurements.readonly) - no separate authorization needed.")]
    public async Task<string> GetBodyFatHistory(
        [Description("Any date within the target month, yyyy-MM-dd format; defaults to the current month if omitted")] string? date = null,
        CancellationToken cancellationToken = default)
    {
        var anyDayInMonth = date is not null
            ? DateOnly.Parse(date, CultureInfo.InvariantCulture)
            : DateOnly.FromDateTime(DateTime.UtcNow);
        var start = new DateOnly(anyDayInMonth.Year, anyDayInMonth.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);

        var rawJson = await api.GetRollupAsync("body-fat", start, end, cancellationToken);
        return JsonSerializer.Serialize(ParseEntries(rawJson));
    }

    /// <summary>
    /// Parses the "bodyFat" rollup value (BodyFatRollupValue.bodyFatPercentageAvg, already a percentage -
    /// no unit conversion needed, unlike weight's grams-to-kg) - confirmed against the live discovery
    /// document; not yet exercised against a real authenticated response.
    /// </summary>
    internal static List<BodyFatEntry> ParseEntries(string rawJson)
    {
        var entries = new List<BodyFatEntry>();

        foreach (var (date, value) in RollupParsing.Enumerate(rawJson, "bodyFat"))
        {
            if (!value.TryGetProperty("bodyFatPercentageAvg", out var percentElement))
            {
                continue;
            }

            entries.Add(new BodyFatEntry(date, percentElement.GetDouble()));
        }

        return entries;
    }
}

public sealed record BodyFatEntry(string Date, double BodyFatPercent);
