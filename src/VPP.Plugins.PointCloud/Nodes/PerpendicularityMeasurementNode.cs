using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Perpendicularity Measurement", "Point Cloud/GD&T", "Measure perpendicularity between two planes")]
public class PerpendicularityMeasurementNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public void SetGraph(NodeGraph graph)
    {
        _graph = graph;
    }

    public PerpendicularityMeasurementNode()
    {
        AddParameter<bool>("AutoMeasure", true, required: false, displayName: "Auto Measure",
            description: "Auto-measure on execution");
        AddParameter<float>("AngleTolerance", 1.0f, required: false, displayName: "Angle Tolerance (degrees)",
            description: "Maximum deviation from 90 degrees");
    }

    protected override Task ExecuteCoreAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        var autoMeasure = GetParameter<bool>("AutoMeasure");
        if (!autoMeasure) return Task.CompletedTask;

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
        var dot = Vector3.Dot(plane1.Normal, plane2.Normal);
        dot = Math.Clamp(dot, -1, 1);
        var angleRadians = (float)Math.Acos(Math.Abs(dot));
        var angleDegrees = angleRadians * 180f / (float)Math.PI;

        // For perpendicular planes, angle should be 90 degrees
        var perpendicularityDeviation = Math.Abs(angleDegrees - 90);

        var result = new PerpendicularityResult
        {
            AngleDeviation = perpendicularityDeviation,
            MeasuredAngle = angleDegrees,
            Plane1 = plane1,
            Plane2 = plane2
        };

        context.Set($"PerpendicularityMeasurement_{Id}", result);

        var pass = perpendicularityDeviation <= angleTolerance;
        var inspection = new InspectionResult
        {
            Pass = pass,
            Message = pass
                ? $"Perpendicularity OK: {angleDegrees:F2}° (Dev: {perpendicularityDeviation:F4}°, Tol: ±{angleTolerance}°)"
                : $"Perpendicularity NG: {angleDegrees:F2}° deviates {perpendicularityDeviation:F4}° from 90°",
            Measurements = new Dictionary<string, double>
            {
                ["MeasuredAngle"] = angleDegrees,
                ["AngleDeviation"] = perpendicularityDeviation,
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
            inspection.Failures.Add($"Perpendicularity deviation {perpendicularityDeviation:F4}° exceeds tolerance {angleTolerance}°");

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
