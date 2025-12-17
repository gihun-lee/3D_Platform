using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Bounding Box", "Point Cloud/Measurement", "Calculate bounding box dimensions of point cloud")]
public class BoundingBoxMeasurementNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public void SetGraph(NodeGraph graph)
    {
        _graph = graph;
    }

    public BoundingBoxMeasurementNode()
    {
        AddParameter<bool>("AutoMeasure", true, required: false, displayName: "Auto Measure",
            description: "Auto-measure on execution");
    }

    protected override Task ExecuteCoreAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        var autoMeasure = GetParameter<bool>("AutoMeasure");
        var cloud = GetConnectedCloud(context);

        if (cloud == null || cloud.Count == 0)
        {
            return Task.CompletedTask;
        }

        if (!autoMeasure)
        {
            context.Set($"BoundingBoxInputCloud_{Id}", cloud);
            return Task.CompletedTask;
        }

        PerformMeasurement(context, cloud);
        return Task.CompletedTask;
    }

    public void PerformMeasurement(ExecutionContext context, PointCloudData cloud)
    {
        cloud.ComputeBoundingBox();

        var min = new Vector3(cloud.BoundingBox[0], cloud.BoundingBox[1], cloud.BoundingBox[2]);
        var max = new Vector3(cloud.BoundingBox[3], cloud.BoundingBox[4], cloud.BoundingBox[5]);
        var size = max - min;
        var center = (min + max) * 0.5f;
        var volume = size.X * size.Y * size.Z;
        var diagonal = size.Length();

        var result = new BoundingBoxResult
        {
            Min = min,
            Max = max,
            Center = center,
            Size = size,
            Volume = volume,
            DiagonalLength = diagonal
        };

        context.Set($"BoundingBox_{Id}", result);

        // Create inspection result for UI display
        var inspection = new InspectionResult
        {
            Pass = true,
            Message = $"BBox: {size.X:F2} x {size.Y:F2} x {size.Z:F2} mm (Vol={volume:F2}mm³)",
            Measurements = new Dictionary<string, double>
            {
                ["SizeX"] = size.X,
                ["SizeY"] = size.Y,
                ["SizeZ"] = size.Z,
                ["Volume"] = volume,
                ["DiagonalLength"] = diagonal,
                ["CenterX"] = center.X,
                ["CenterY"] = center.Y,
                ["CenterZ"] = center.Z,
                ["MinX"] = min.X,
                ["MinY"] = min.Y,
                ["MinZ"] = min.Z,
                ["MaxX"] = max.X,
                ["MaxY"] = max.Y,
                ["MaxZ"] = max.Z
            }
        };
        context.Set($"InspectionResult_{Id}", inspection);
    }

    private PointCloudData? GetConnectedCloud(ExecutionContext context)
    {
        if (_graph == null) return null;

        var sourceNodeId = _graph.Connections
            .Where(c => c.TargetNodeId == Id)
            .Select(c => c.SourceNodeId)
            .FirstOrDefault();

        if (sourceNodeId != null)
        {
            return context.Get<PointCloudData>($"{ExecutionContext.FilteredCloudKey}_{sourceNodeId}")
                ?? context.Get<PointCloudData>($"{ExecutionContext.PointCloudKey}_{sourceNodeId}");
        }

        return null;
    }
}
