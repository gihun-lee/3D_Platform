using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Distance Measurement", "Point Cloud/Measurement", "Measure distance between two points in 3D space")]
public class DistanceMeasurementNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public void SetGraph(NodeGraph graph)
    {
        _graph = graph;
    }

    public DistanceMeasurementNode()
    {
        AddParameter<bool>("AutoMeasure", true, required: false, displayName: "Auto Measure",
            description: "Auto-measure on execution");

        // Point selection mode
        AddParameter<string>("Mode", "Centroid", required: false, displayName: "Measurement Mode",
            description: "Manual: Use specified coordinates, Centroid: Use centroids of connected clouds, MinMax: Use min/max points");

        // Manual point coordinates
        AddParameter<float>("Point1X", 0f, required: false, displayName: "Point 1 X (mm)");
        AddParameter<float>("Point1Y", 0f, required: false, displayName: "Point 1 Y (mm)");
        AddParameter<float>("Point1Z", 0f, required: false, displayName: "Point 1 Z (mm)");
        AddParameter<float>("Point2X", 0f, required: false, displayName: "Point 2 X (mm)");
        AddParameter<float>("Point2Y", 0f, required: false, displayName: "Point 2 Y (mm)");
        AddParameter<float>("Point2Z", 0f, required: false, displayName: "Point 2 Z (mm)");
    }

    protected override Task ExecuteCoreAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        var autoMeasure = GetParameter<bool>("AutoMeasure");
        if (!autoMeasure) return Task.CompletedTask;

        PerformMeasurement(context);
        return Task.CompletedTask;
    }

    public void PerformMeasurement(ExecutionContext context)
    {
        var mode = GetParameter<string>("Mode") ?? "Manual";
        Vector3 point1, point2;

        switch (mode)
        {
            case "Centroid":
                var clouds = GetConnectedClouds(context);
                if (clouds.Count >= 2)
                {
                    point1 = CalculateCentroid(clouds[0]);
                    point2 = CalculateCentroid(clouds[1]);
                }
                else if (clouds.Count == 1)
                {
                    var cloud = clouds[0];
                    point1 = CalculateCentroid(cloud);
                    point2 = new Vector3(
                        GetParameter<float>("Point2X"),
                        GetParameter<float>("Point2Y"),
                        GetParameter<float>("Point2Z")
                    );
                }
                else
                {
                    point1 = new Vector3(GetParameter<float>("Point1X"), GetParameter<float>("Point1Y"), GetParameter<float>("Point1Z"));
                    point2 = new Vector3(GetParameter<float>("Point2X"), GetParameter<float>("Point2Y"), GetParameter<float>("Point2Z"));
                }
                break;

            case "MinMax":
                var cloud1 = GetConnectedCloud(context);
                if (cloud1 != null && cloud1.Count > 0)
                {
                    point1 = new Vector3(cloud1.BoundingBox[0], cloud1.BoundingBox[1], cloud1.BoundingBox[2]);
                    point2 = new Vector3(cloud1.BoundingBox[3], cloud1.BoundingBox[4], cloud1.BoundingBox[5]);
                }
                else
                {
                    point1 = new Vector3(GetParameter<float>("Point1X"), GetParameter<float>("Point1Y"), GetParameter<float>("Point1Z"));
                    point2 = new Vector3(GetParameter<float>("Point2X"), GetParameter<float>("Point2Y"), GetParameter<float>("Point2Z"));
                }
                break;

            default: // Manual
                point1 = new Vector3(
                    GetParameter<float>("Point1X"),
                    GetParameter<float>("Point1Y"),
                    GetParameter<float>("Point1Z")
                );
                point2 = new Vector3(
                    GetParameter<float>("Point2X"),
                    GetParameter<float>("Point2Y"),
                    GetParameter<float>("Point2Z")
                );
                break;
        }

        var result = new DistanceMeasurementResult
        {
            Point1 = point1,
            Point2 = point2,
            Distance = Vector3.Distance(point1, point2),
            DistanceX = Math.Abs(point2.X - point1.X),
            DistanceY = Math.Abs(point2.Y - point1.Y),
            DistanceZ = Math.Abs(point2.Z - point1.Z)
        };

        context.Set($"DistanceMeasurement_{Id}", result);

        // Also create inspection result for UI display
        var inspection = new InspectionResult
        {
            Pass = true,
            Message = $"Distance: {result.Distance:F3}mm (ΔX={result.DistanceX:F2}, ΔY={result.DistanceY:F2}, ΔZ={result.DistanceZ:F2})",
            Measurements = new Dictionary<string, double>
            {
                ["Distance"] = result.Distance,
                ["DistanceX"] = result.DistanceX,
                ["DistanceY"] = result.DistanceY,
                ["DistanceZ"] = result.DistanceZ,
                ["Point1X"] = point1.X,
                ["Point1Y"] = point1.Y,
                ["Point1Z"] = point1.Z,
                ["Point2X"] = point2.X,
                ["Point2Y"] = point2.Y,
                ["Point2Z"] = point2.Z
            }
        };
        context.Set($"InspectionResult_{Id}", inspection);
    }

    private Vector3 CalculateCentroid(PointCloudData cloud)
    {
        if (cloud.Points.Count == 0) return Vector3.Zero;
        var sum = Vector3.Zero;
        foreach (var p in cloud.Points)
            sum += p;
        return sum / cloud.Points.Count;
    }

    private List<PointCloudData> GetConnectedClouds(ExecutionContext context)
    {
        var clouds = new List<PointCloudData>();
        if (_graph == null) return clouds;

        var sourceNodeIds = _graph.Connections
            .Where(c => c.TargetNodeId == Id)
            .Select(c => c.SourceNodeId)
            .ToList();

        foreach (var nodeId in sourceNodeIds)
        {
            var cloud = context.Get<PointCloudData>($"{ExecutionContext.FilteredCloudKey}_{nodeId}")
                     ?? context.Get<PointCloudData>($"{ExecutionContext.PointCloudKey}_{nodeId}");
            if (cloud != null)
                clouds.Add(cloud);
        }

        return clouds;
    }

    private PointCloudData? GetConnectedCloud(ExecutionContext context)
    {
        var clouds = GetConnectedClouds(context);
        return clouds.FirstOrDefault();
    }
}
