// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using FitbitMcp.Auth;
using ModelContextProtocol.Server;

namespace FitbitMcp.Tools;

[McpServerToolType]
public class RouteTools(GoogleHealthApi api)
{
    [McpServerTool(Name = "get_exercise_gps_route")]
    [Description("Get the GPS route recorded for one exercise session (from get_exercise_history's exerciseId, on " +
        "a session with hasGps true) as a list of { time, latitude, longitude, altitudeMeters, distanceMeters, " +
        "heartRateBpm } trackpoints in chronological order. Google Health only exposes GPS data through this " +
        "per-exercise TCX export, not as its own queryable data type. Long sessions can have thousands of raw " +
        "points; maxPoints evenly downsamples the route (always keeping the first and last point) to keep the " +
        "response a reasonable size for a route summary - omit it or pass a high value for the full-resolution track.")]
    public async Task<string> GetExerciseGpsRoute(
        [Description("The exerciseId from get_exercise_history")] string exerciseId,
        [Description("Maximum trackpoints to return; the route is evenly downsampled if it has more. Default 200")] int maxPoints = 200,
        CancellationToken cancellationToken = default)
    {
        var tcxXml = await api.ExportExerciseTcxAsync(exerciseId, cancellationToken: cancellationToken);
        var points = ParseTrackpoints(tcxXml);
        return JsonSerializer.Serialize(Downsample(points, maxPoints));
    }

    /// <summary>
    /// Parses a TCX (Training Center XML) document's Trackpoint elements - the format
    /// dataPoints.exportExerciseTcx returns (confirmed via the live discovery doc; there's no JSON GPS shape
    /// to work from instead). Namespace-tolerant: reads whatever default namespace the root element declares
    /// rather than hardcoding a TCX schema version, since Google doesn't document which version it emits.
    /// </summary>
    internal static List<Trackpoint> ParseTrackpoints(string tcxXml)
    {
        var points = new List<Trackpoint>();
        var document = XDocument.Parse(tcxXml);
        if (document.Root is null)
        {
            return points;
        }

        var ns = document.Root.Name.Namespace;

        foreach (var trackpoint in document.Descendants(ns + "Trackpoint"))
        {
            var time = trackpoint.Element(ns + "Time")?.Value;
            if (string.IsNullOrEmpty(time))
            {
                continue;
            }

            var position = trackpoint.Element(ns + "Position");
            double? latitude = ParseDouble(position?.Element(ns + "LatitudeDegrees")?.Value);
            double? longitude = ParseDouble(position?.Element(ns + "LongitudeDegrees")?.Value);
            double? altitudeMeters = ParseDouble(trackpoint.Element(ns + "AltitudeMeters")?.Value);
            double? distanceMeters = ParseDouble(trackpoint.Element(ns + "DistanceMeters")?.Value);
            long? heartRateBpm = ParseLong(trackpoint.Element(ns + "HeartRateBpm")?.Element(ns + "Value")?.Value);

            points.Add(new Trackpoint(time, latitude, longitude, altitudeMeters, distanceMeters, heartRateBpm));
        }

        return points;
    }

    /// <summary>
    /// Evenly downsamples to at most maxPoints, always keeping the first and last point so the route's
    /// start/end are never dropped.
    /// </summary>
    internal static List<Trackpoint> Downsample(List<Trackpoint> points, int maxPoints)
    {
        if (maxPoints <= 0 || points.Count <= maxPoints)
        {
            return points;
        }

        if (maxPoints == 1)
        {
            return [points[0]];
        }

        var result = new List<Trackpoint>(maxPoints);
        var step = (points.Count - 1) / (double)(maxPoints - 1);
        for (var i = 0; i < maxPoints; i++)
        {
            result.Add(points[(int)Math.Round(i * step)]);
        }

        return result;
    }

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static long? ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
}

public sealed record Trackpoint(
    string Time,
    double? Latitude,
    double? Longitude,
    double? AltitudeMeters,
    double? DistanceMeters,
    long? HeartRateBpm);
