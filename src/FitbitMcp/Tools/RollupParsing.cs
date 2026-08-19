// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text.Json;

namespace FitbitMcp.Tools;

/// <summary>
/// Shared parsing for a RollUpDataPointsResponse ({ rollupDataPoints: [{ startTime, &lt;valueField&gt;: {...} }] })
/// - the shape every rollup-based Google Health data type shares. Data-type-specific tools extract
/// their own typed fields from the yielded JsonElement.
/// </summary>
internal static class RollupParsing
{
    public static IEnumerable<(string Date, JsonElement Value)> Enumerate(string rawJson, string valueFieldName)
    {
        using var document = JsonDocument.Parse(rawJson);

        if (!document.RootElement.TryGetProperty("rollupDataPoints", out var rollupDataPoints))
        {
            yield break;
        }

        foreach (var dataPoint in rollupDataPoints.EnumerateArray())
        {
            if (!dataPoint.TryGetProperty("startTime", out var startTimeElement)
                || !DateTimeOffset.TryParse(startTimeElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime)
                || !dataPoint.TryGetProperty(valueFieldName, out var value))
            {
                continue;
            }

            var isoDate = DateOnly.FromDateTime(startTime.UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            yield return (isoDate, value.Clone());
        }
    }
}
