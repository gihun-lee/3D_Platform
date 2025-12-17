using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Centroid", "Point Cloud/Measurement", "Calculate centroid (center of mass) of point cloud")]
public class CentroidMeasurementNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public void SetGraph(NodeGraph graph)
    {
        _graph = graph;
    }

    public CentroidMeasurementNode()
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
            context.Set($"CentroidInputCloud_{Id}", cloud);
            return Task.CompletedTask;
        }

        PerformMeasurement(context, cloud);
        return Task.CompletedTask;
    }

    public void PerformMeasurement(ExecutionContext context, PointCloudData cloud)
    {
        var sum = Vector3.Zero;
        foreach (var p in cloud.Points)
            sum += p;

        var centroid = sum / cloud.Points.Count;

        var result = new CentroidResult
        {
            Centroid = centroid,
            PointCount = cloud.Points.Count
        };

        context.Set($"Centroid_{Id}", result);

        // Create inspection result for UI display
        var inspection = new InspectionResult
        {
            Pass = true,
            Message = $"Centroid: ({centroid.X:F3}, {centroid.Y:F3}, {centroid.Z:F3}) from {cloud.Points.Count} points",
            Measurements = new Dictionary<string, double>
            {
                ["CentroidX"] = centroid.X,
                ["CentroidY"] = centroid.Y,
                ["CentroidZ"] = centroid.Z,
                ["PointCount"] = cloud.Points.Count
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
