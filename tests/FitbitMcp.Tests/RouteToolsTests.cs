// SPDX-License-Identifier: MIT

using FitbitMcp.Tools;

namespace FitbitMcp.Tests;

[TestFixture]
public class RouteToolsTests
{
    private const string SampleTcx = """
    <?xml version="1.0" encoding="UTF-8"?>
    <TrainingCenterDatabase xmlns="http://www.garmin.com/xmlschemas/TrainingCenterDatabase/v2">
      <Activities>
        <Activity Sport="Running">
          <Id>2026-08-15T06:30:00Z</Id>
          <Lap StartTime="2026-08-15T06:30:00Z">
            <TotalTimeSeconds>120</TotalTimeSeconds>
            <DistanceMeters>350.0</DistanceMeters>
            <Track>
              <Trackpoint>
                <Time>2026-08-15T06:30:00Z</Time>
                <Position>
                  <LatitudeDegrees>52.520</LatitudeDegrees>
                  <LongitudeDegrees>13.404</LongitudeDegrees>
                </Position>
                <AltitudeMeters>34.0</AltitudeMeters>
                <DistanceMeters>0.0</DistanceMeters>
                <HeartRateBpm><Value>118</Value></HeartRateBpm>
              </Trackpoint>
              <Trackpoint>
                <Time>2026-08-15T06:31:00Z</Time>
                <Position>
                  <LatitudeDegrees>52.521</LatitudeDegrees>
                  <LongitudeDegrees>13.405</LongitudeDegrees>
                </Position>
                <AltitudeMeters>35.5</AltitudeMeters>
                <DistanceMeters>175.0</DistanceMeters>
                <HeartRateBpm><Value>132</Value></HeartRateBpm>
              </Trackpoint>
              <Trackpoint>
                <Time>2026-08-15T06:32:00Z</Time>
                <Position>
                  <LatitudeDegrees>52.522</LatitudeDegrees>
                  <LongitudeDegrees>13.406</LongitudeDegrees>
                </Position>
                <AltitudeMeters>36.0</AltitudeMeters>
                <DistanceMeters>350.0</DistanceMeters>
                <HeartRateBpm><Value>140</Value></HeartRateBpm>
              </Trackpoint>
            </Track>
          </Lap>
        </Activity>
      </Activities>
    </TrainingCenterDatabase>
    """;

    [Test]
    public void ParseTrackpoints_ReadsTimePositionAltitudeDistanceAndHeartRate()
    {
        var points = RouteTools.ParseTrackpoints(SampleTcx);

        Assert.That(points, Has.Count.EqualTo(3));
        Assert.That(points[0], Is.EqualTo(new Trackpoint("2026-08-15T06:30:00Z", 52.520, 13.404, 34.0, 0.0, 118)));
        Assert.That(points[2], Is.EqualTo(new Trackpoint("2026-08-15T06:32:00Z", 52.522, 13.406, 36.0, 350.0, 140)));
    }

    [Test]
    public void ParseTrackpoints_ReturnsEmptyList_WhenNoTrackpoints()
    {
        const string tcx = """
        <TrainingCenterDatabase xmlns="http://www.garmin.com/xmlschemas/TrainingCenterDatabase/v2">
          <Activities><Activity Sport="Running"><Id>2026-08-15T06:30:00Z</Id></Activity></Activities>
        </TrainingCenterDatabase>
        """;

        var points = RouteTools.ParseTrackpoints(tcx);

        Assert.That(points, Is.Empty);
    }

    [Test]
    public void Downsample_ReturnsInput_WhenAlreadyWithinLimit()
    {
        var points = RouteTools.ParseTrackpoints(SampleTcx);

        var result = RouteTools.Downsample(points, 10);

        Assert.That(result, Is.EqualTo(points));
    }

    [Test]
    public void Downsample_KeepsFirstAndLastPoint_WhenReducingCount()
    {
        var points = Enumerable.Range(0, 100)
            .Select(i => new Trackpoint($"t{i}", i, i, null, null, null))
            .ToList();

        var result = RouteTools.Downsample(points, 10);

        Assert.That(result, Has.Count.EqualTo(10));
        Assert.That(result[0], Is.EqualTo(points[0]));
        Assert.That(result[^1], Is.EqualTo(points[^1]));
    }
}
