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

        var rawJson = await api.GetWeightRollupAsync(start, end, cancellationToken);
        return JsonSerializer.Serialize(ParseEntries(rawJson));
    }

    /// <summary>
    /// Best-effort parse of the dataPoints:rollUp response into flat entries. Google Health API v4's
    /// exact bucket/dataset/point/value shape hasn't been exercised against a live account yet -
    /// adjust the property names below if they don't match what the real API returns.
    /// </summary>
    internal static List<WeightEntry> ParseEntries(string rawJson)
    {
        var entries = new List<WeightEntry>();
        using var document = JsonDocument.Parse(rawJson);

        if (!document.RootElement.TryGetProperty("bucket", out var buckets))
        {
            return entries;
        }

        foreach (var bucket in buckets.EnumerateArray())
        {
            if (!bucket.TryGetProperty("startTime", out var startTimeElement)
                || !DateTimeOffset.TryParse(startTimeElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime)
                || !bucket.TryGetProperty("dataset", out var datasets))
            {
                continue;
            }

            foreach (var dataset in datasets.EnumerateArray())
            {
                if (!dataset.TryGetProperty("point", out var points))
                {
                    continue;
                }

                foreach (var point in points.EnumerateArray())
                {
                    var weightKg = TryReadWeightKg(point);
                    if (weightKg is not null)
                    {
                        var isoDate = DateOnly.FromDateTime(startTime.UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        entries.Add(new WeightEntry(isoDate, weightKg.Value));
                    }
                }
            }
        }

        return entries;
    }

    private static double? TryReadWeightKg(JsonElement point)
    {
        if (!point.TryGetProperty("value", out var values) || values.GetArrayLength() == 0)
        {
            return null;
        }

        var value = values[0];
        if (value.TryGetProperty("fpVal", out var fpVal))
        {
            return fpVal.GetDouble();
        }

        if (value.TryGetProperty("floatValue", out var floatValue))
        {
            return floatValue.GetDouble();
        }

        return null;
    }
}

public sealed record WeightEntry(string Date, double WeightKg);
