using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Parallelism Measurement", "Point Cloud/GD&T", "Measure parallelism between two planes")]
public class ParallelismMeasurementNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public void SetGraph(NodeGraph graph)
    {
        _graph = graph;
    }

    public ParallelismMeasurementNode()
    {
        AddParameter<bool>("AutoMeasure", true, required: false, displayName: "Auto Measure",
            description: "Auto-measure on execution");
        AddParameter<float>("AngleTolerance", 1.0f, required: false, displayName: "Angle Tolerance (degrees)",
            description: "Maximum angle deviation for parallelism");
    }

    protected override Task ExecuteCoreAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        var autoMeasure = GetParameter<bool>("AutoMeasure");
        if (!autoMeasure) return Task.CompletedTask;

        // Get connected plane fitting results
        var planes = GetConnectedPlaneFittingResults(context);

        if (planes.Count < 2)
        {
            return Task.CompletedTask;
        }

        PerformMeasurement(context, planes[0], planes[1]);
        return Task.CompletedTask;
    }

    public void PerformMeasurement(ExecutionContext context, PlaneFittingResult plane1, PlaneFittingResult plane2)
    {
        var angleTolerance = GetParameter<float>("AngleTolerance");

        // Calculate angle between plane normals
        var dot = Math.Abs(Vector3.Dot(plane1.Normal, plane2.Normal));
        dot = Math.Clamp(dot, 0, 1);
        var angleRadians = (float)Math.Acos(dot);
        var angleDegrees = angleRadians * 180f / (float)Math.PI;

        // For parallel planes, angle should be close to 0 or 180 degrees
        var parallelismDeviation = Math.Min(angleDegrees, 180 - angleDegrees);

        // Calculate max distance deviation (how far apart the planes are)
        var distanceDeviation = Math.Abs(plane1.D - plane2.D);

        var result = new ParallelismResult
        {
            AngleDeviation = parallelismDeviation,
            MaxDistanceDeviation = distanceDeviation,
            Plane1 = plane1,
            Plane2 = plane2
        };

        context.Set($"ParallelismMeasurement_{Id}", result);

        var pass = parallelismDeviation <= angleTolerance;
        var inspection = new InspectionResult
        {
            Pass = pass,
            Message = pass
                ? $"Parallelism OK: {parallelismDeviation:F4}° (Tol: {angleTolerance}°)"
                : $"Parallelism NG: {parallelismDeviation:F4}° exceeds tolerance {angleTolerance}°",
            Measurements = new Dictionary<string, double>
            {
                ["AngleDeviation"] = parallelismDeviation,
                ["DistanceDeviation"] = distanceDeviation,
                ["AngleTolerance"] = angleTolerance,
                ["Plane1NormalX"] = plane1.Normal.X,
                ["Plane1NormalY"] = plane1.Normal.Y,
                ["Plane1NormalZ"] = plane1.Normal.Z,
                ["Plane2NormalX"] = plane2.Normal.X,
                ["Plane2NormalY"] = plane2.Normal.Y,
                ["Plane2NormalZ"] = plane2.Normal.Z
            }
        };
        if (!pass)
            inspection.Failures.Add($"Parallelism deviation {parallelismDeviation:F4}° exceeds tolerance {angleTolerance}°");

        context.Set($"InspectionResult_{Id}", inspection);
    }

    private List<PlaneFittingResult> GetConnectedPlaneFittingResults(ExecutionContext context)
    {
        var results = new List<PlaneFittingResult>();
        if (_graph == null) return results;

        var sourceNodeIds = _graph.Connections
            .Where(c => c.TargetNodeId == Id)
            .Select(c => c.SourceNodeId)
            .ToList();

        foreach (var nodeId in sourceNodeIds)
        {
            var plane = context.Get<PlaneFittingResult>($"PlaneFitting_{nodeId}");
            if (plane != null && plane.InlierCount > 0)
                results.Add(plane);
        }

        return results;
    }
}
