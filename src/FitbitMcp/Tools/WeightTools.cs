// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using FitbitMcp.Auth;
using ModelContextProtocol.Server;

namespace FitbitMcp.Tools;

[McpServerToolType]
public class WeightTools(GoogleHealthApi api)
{
    [McpServerTool(Name = "get_weight_history")]
    [Description("Get body-weight entries recorded in Google Health (e.g. from a Fitbit Aria scale) for the month " +
        "containing the given date, normalized to a flat list of { date, weightKg } entries.")]
    public async Task<string> GetWeightHistory(
        [Description("Any date within the target month, yyyy-MM-dd format; defaults to the current month if omitted")] string? date = null,
        CancellationToken cancellationToken = default)
    {
        var anyDayInMonth = date is not null
            ? DateOnly.Parse(date, CultureInfo.InvariantCulture)
            : DateOnly.FromDateTime(DateTime.UtcNow);
        var start = new DateOnly(anyDayInMonth.Year, anyDayInMonth.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);

        var rawJson = await api.GetRollupAsync("weight", start, end, cancellationToken);
        return JsonSerializer.Serialize(ParseEntries(rawJson));
    }

    /// <summary>
    /// Parses a RollUpDataPointsResponse (https://health.googleapis.com/$discovery/rest?version=v4) into
    /// flat entries: rollupDataPoints[].startTime + rollupDataPoints[].weight.weightGramsAvg, converted
    /// from grams to kilograms. Confirmed against the live discovery document; not yet exercised against
    /// a real authenticated response.
    /// </summary>
    internal static List<WeightEntry> ParseEntries(string rawJson)
    {
        var entries = new List<WeightEntry>();
        using var document = JsonDocument.Parse(rawJson);

        if (!document.RootElement.TryGetProperty("rollupDataPoints", out var rollupDataPoints))
        {
            return entries;
        }

        foreach (var dataPoint in rollupDataPoints.EnumerateArray())
        {
            if (!dataPoint.TryGetProperty("startTime", out var startTimeElement)
                || !DateTimeOffset.TryParse(startTimeElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime)
                || !dataPoint.TryGetProperty("weight", out var weight)
                || !weight.TryGetProperty("weightGramsAvg", out var weightGramsAvg))
            {
                continue;
            }

            var isoDate = DateOnly.FromDateTime(startTime.UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            entries.Add(new WeightEntry(isoDate, weightGramsAvg.GetDouble() / 1000));
        }

        return entries;
    }
}

public sealed record WeightEntry(string Date, double WeightKg);
