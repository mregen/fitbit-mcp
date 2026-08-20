// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using FitbitMcp.Auth;
using ModelContextProtocol.Server;

namespace FitbitMcp.Tools;

[McpServerToolType]
public class ExerciseTools(GoogleHealthApi api)
{
    [McpServerTool(Name = "get_exercise_history")]
    [Description("Get individual exercise/workout sessions recorded in Google Health for the month containing the " +
        "given date (e.g. a run, bike ride, or gym session), normalized to a flat list of { date, displayName, " +
        "exerciseType, durationMinutes, distanceKm, caloriesKcal, averageHeartRateBpm, averagePaceMinPerKm, " +
        "elevationGainMeters, steps, hasGps } entries - date is the civil date the session started. This does not " +
        "include raw GPS route/waypoint data - only whether the session had GPS tracking (hasGps) - since Google " +
        "Health's exercise data type carries summary metrics, not routes. Google Health caps this at 25 sessions " +
        "per call, most recent first, so a month with more sessions than that returns only the most recent 25.")]
    public async Task<string> GetExerciseHistory(
        [Description("Any date within the target month, yyyy-MM-dd format; defaults to the current month if omitted")] string? date = null,
        CancellationToken cancellationToken = default)
    {
        var anyDayInMonth = date is not null
            ? DateOnly.Parse(date, CultureInfo.InvariantCulture)
            : DateOnly.FromDateTime(DateTime.UtcNow);
        var start = new DateOnly(anyDayInMonth.Year, anyDayInMonth.Month, 1);
        var end = start.AddMonths(1);

        // Unlike sleep, exercise's dataPoints.list filter only supports interval.civil_start_time, not
        // civil_end_time - confirmed via the live discovery doc's filter parameter documentation, which
        // explicitly scopes civil_end_time filtering to "Sleep specific".
        var filter =
            $"exercise.interval.civil_start_time >= \"{start:yyyy-MM-dd}\" AND exercise.interval.civil_start_time < \"{end:yyyy-MM-dd}\"";

        var rawJson = await api.ListDataPointsAsync("exercise", filter, cancellationToken);
        return JsonSerializer.Serialize(ParseEntries(rawJson));
    }

    /// <summary>
    /// Parses a ListDataPointsResponse ({ dataPoints: [{ exercise: { interval, displayName, exerciseType,
    /// activeDuration, metricsSummary, exerciseMetadata } }] }) - exercise is a session data type, not a rollup
    /// type, so it comes back as raw DataPoint entries via dataPoints.list rather than dataPoints:rollUp.
    /// </summary>
    internal static List<ExerciseSession> ParseEntries(string rawJson)
    {
        var entries = new List<ExerciseSession>();
        using var document = JsonDocument.Parse(rawJson);

        if (!document.RootElement.TryGetProperty("dataPoints", out var dataPoints))
        {
            return entries;
        }

        foreach (var dataPoint in dataPoints.EnumerateArray())
        {
            if (!dataPoint.TryGetProperty("exercise", out var exercise)
                || !exercise.TryGetProperty("interval", out var interval)
                || !interval.TryGetProperty("startTime", out var startTimeElement)
                || !DateTimeOffset.TryParse(startTimeElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime))
            {
                continue;
            }

            var isoDate = DateOnly.FromDateTime(startTime.UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var displayName = exercise.TryGetProperty("displayName", out var displayNameElement) ? displayNameElement.GetString() : null;
            var exerciseType = exercise.TryGetProperty("exerciseType", out var exerciseTypeElement) ? exerciseTypeElement.GetString() : null;

            double? durationMinutes = exercise.TryGetProperty("activeDuration", out var activeDuration)
                && TryParseDurationMinutes(activeDuration.GetString(), out var activeDurationMinutes)
                    ? activeDurationMinutes
                    : DurationBetween(interval);

            double? distanceKm = null;
            double? caloriesKcal = null;
            long? averageHeartRateBpm = null;
            double? averagePaceMinPerKm = null;
            double? elevationGainMeters = null;
            long? steps = null;

            if (exercise.TryGetProperty("metricsSummary", out var metrics))
            {
                if (metrics.TryGetProperty("distanceMillimeters", out var distanceMillimeters))
                {
                    distanceKm = distanceMillimeters.GetDouble() / 1_000_000.0;
                }

                if (metrics.TryGetProperty("caloriesKcal", out var kcal))
                {
                    caloriesKcal = kcal.GetDouble();
                }

                if (metrics.TryGetProperty("averageHeartRateBeatsPerMinute", out var avgHr)
                    && long.TryParse(avgHr.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bpm))
                {
                    averageHeartRateBpm = bpm;
                }

                if (metrics.TryGetProperty("averagePaceSecondsPerMeter", out var paceSecondsPerMeter))
                {
                    // seconds/meter * 1000 m/km / 60 s/min = minutes/km
                    averagePaceMinPerKm = paceSecondsPerMeter.GetDouble() * 1000.0 / 60.0;
                }

                if (metrics.TryGetProperty("elevationGainMillimeters", out var elevationGainMillimeters))
                {
                    elevationGainMeters = elevationGainMillimeters.GetDouble() / 1000.0;
                }

                if (metrics.TryGetProperty("steps", out var stepsElement)
                    && long.TryParse(stepsElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var stepCount))
                {
                    steps = stepCount;
                }
            }

            bool? hasGps = exercise.TryGetProperty("exerciseMetadata", out var metadata)
                && metadata.TryGetProperty("hasGps", out var hasGpsElement)
                && hasGpsElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? hasGpsElement.GetBoolean()
                    : null;

            entries.Add(new ExerciseSession(
                isoDate, displayName, exerciseType, durationMinutes, distanceKm, caloriesKcal,
                averageHeartRateBpm, averagePaceMinPerKm, elevationGainMeters, steps, hasGps));
        }

        return entries;
    }

    private static double? DurationBetween(JsonElement interval) =>
        interval.TryGetProperty("startTime", out var startElement)
        && interval.TryGetProperty("endTime", out var endElement)
        && DateTimeOffset.TryParse(startElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
        && DateTimeOffset.TryParse(endElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
            ? (end - start).TotalMinutes
            : null;

    /// <summary>
    /// Parses a protobuf "google-duration" string (e.g. "3600s" or "45.5s") into whole minutes.
    /// </summary>
    private static bool TryParseDurationMinutes(string? duration, out double minutes)
    {
        minutes = 0;
        if (duration is null || !duration.EndsWith('s'))
        {
            return false;
        }

        if (!double.TryParse(duration.AsSpan(0, duration.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        minutes = seconds / 60.0;
        return true;
    }
}

public sealed record ExerciseSession(
    string Date,
    string? DisplayName,
    string? ExerciseType,
    double? DurationMinutes,
    double? DistanceKm,
    double? CaloriesKcal,
    long? AverageHeartRateBpm,
    double? AveragePaceMinPerKm,
    double? ElevationGainMeters,
    long? Steps,
    bool? HasGps);
