using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Height Measurement", "Point Cloud/Measurement", "Measure height (Z-axis) statistics of point cloud")]
public class HeightMeasurementNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public void SetGraph(NodeGraph graph)
    {
        _graph = graph;
    }

    public HeightMeasurementNode()
    {
        AddParameter<bool>("AutoMeasure", true, required: false, displayName: "Auto Measure",
            description: "Auto-measure on execution");
        AddParameter<string>("Axis", "Z", required: false, displayName: "Measurement Axis",
            description: "Axis for height measurement (X, Y, or Z)");
        AddParameter<float>("ReferenceHeight", 0f, required: false, displayName: "Reference Height (mm)",
            description: "Reference height for relative measurements");
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
            context.Set($"HeightMeasurementInputCloud_{Id}", cloud);
            return Task.CompletedTask;
        }

        PerformMeasurement(context, cloud);
        return Task.CompletedTask;
    }

    public void PerformMeasurement(ExecutionContext context, PointCloudData cloud)
    {
        var axis = GetParameter<string>("Axis") ?? "Z";
        var referenceHeight = GetParameter<float>("ReferenceHeight");

        Func<Vector3, float> getAxisValue = axis.ToUpper() switch
        {
            "X" => p => p.X,
            "Y" => p => p.Y,
            _ => p => p.Z
        };

        var values = cloud.Points.Select(getAxisValue).ToList();
        var minValue = values.Min();
        var maxValue = values.Max();
        var avgValue = values.Average();

        var lowestIdx = values.IndexOf(minValue);
        var highestIdx = values.IndexOf(maxValue);

        var result = new HeightMeasurementResult
        {
            MinZ = minValue,
            MaxZ = maxValue,
            Height = maxValue - minValue,
            AverageZ = (float)avgValue,
            LowestPoint = cloud.Points[lowestIdx],
            HighestPoint = cloud.Points[highestIdx]
        };

        context.Set($"HeightMeasurement_{Id}", result);

        // Create inspection result for UI display
        var inspection = new InspectionResult
        {
            Pass = true,
            Message = $"Height ({axis}): {result.Height:F3}mm (Min={result.MinZ:F2}, Max={result.MaxZ:F2}, Avg={result.AverageZ:F2})",
            Measurements = new Dictionary<string, double>
            {
                ["Height"] = result.Height,
                ["MinValue"] = result.MinZ,
                ["MaxValue"] = result.MaxZ,
                ["AverageValue"] = result.AverageZ,
                ["ReferenceHeight"] = referenceHeight,
                ["RelativeMin"] = result.MinZ - referenceHeight,
                ["RelativeMax"] = result.MaxZ - referenceHeight,
                ["LowestPointX"] = result.LowestPoint.X,
                ["LowestPointY"] = result.LowestPoint.Y,
                ["LowestPointZ"] = result.LowestPoint.Z,
                ["HighestPointX"] = result.HighestPoint.X,
                ["HighestPointY"] = result.HighestPoint.Y,
                ["HighestPointZ"] = result.HighestPoint.Z
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
