using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Roundness Measurement", "Point Cloud/GD&T", "Measure roundness (circularity) of detected circle")]
public class RoundnessMeasurementNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public void SetGraph(NodeGraph graph)
    {
        _graph = graph;
    }

    public RoundnessMeasurementNode()
    {
        AddParameter<bool>("AutoMeasure", true, required: false, displayName: "Auto Measure",
            description: "Auto-measure on execution");
        AddParameter<float>("RoundnessTolerance", 0.1f, required: false, displayName: "Roundness Tolerance (mm)",
            description: "Maximum allowed roundness deviation");
    }

    protected override Task ExecuteCoreAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        var autoMeasure = GetParameter<bool>("AutoMeasure");
        if (!autoMeasure) return Task.CompletedTask;

        var circle = GetConnectedCircleResult(context);
        if (circle == null || circle.InlierCount == 0)
        {
            return Task.CompletedTask;
        }

        PerformMeasurement(context, circle);
        return Task.CompletedTask;
    }

    public void PerformMeasurement(ExecutionContext context, CircleDetectionResult circle)
    {
        var tolerance = GetParameter<float>("RoundnessTolerance");

        if (circle.InlierPoints == null || circle.InlierPoints.Count < 3)
        {
            return;
        }

        // Calculate radial deviations from the fitted circle
        var deviations = new List<float>();
        foreach (var p in circle.InlierPoints)
        {
            // Project to circle plane
            var toPoint = p - circle.Center;
            var alongNormal = Vector3.Dot(toPoint, circle.Normal) * circle.Normal;
            var inPlane = toPoint - alongNormal;
            var radius = inPlane.Length();
            var deviation = Math.Abs(radius - circle.Radius);
            deviations.Add(deviation);
        }

        var maxDeviation = deviations.Max();
        var minDeviation = deviations.Min();
        var avgDeviation = (float)deviations.Average();
        var roundness = maxDeviation; // Roundness is typically max deviation from ideal circle

        // Calculate min/max radii
        var radii = circle.InlierPoints.Select(p =>
        {
            var toPoint = p - circle.Center;
            var alongNormal = Vector3.Dot(toPoint, circle.Normal) * circle.Normal;
            var inPlane = toPoint - alongNormal;
            return inPlane.Length();
        }).ToList();

        var minRadius = radii.Min();
        var maxRadius = radii.Max();
        var radialRange = maxRadius - minRadius;

        context.Set($"RoundnessMeasurement_{Id}", new
        {
            Roundness = roundness,
            MaxDeviation = maxDeviation,
            MinRadius = minRadius,
            MaxRadius = maxRadius,
            RadialRange = radialRange
        });

        var pass = roundness <= tolerance;
        var inspection = new InspectionResult
        {
            Pass = pass,
            Message = pass
                ? $"Roundness OK: {roundness:F4}mm (Tol: {tolerance}mm)"
                : $"Roundness NG: {roundness:F4}mm exceeds tolerance {tolerance}mm",
            Measurements = new Dictionary<string, double>
            {
                ["Roundness"] = roundness,
                ["MaxDeviation"] = maxDeviation,
                ["AvgDeviation"] = avgDeviation,
                ["MinRadius"] = minRadius,
                ["MaxRadius"] = maxRadius,
                ["RadialRange"] = radialRange,
                ["NominalRadius"] = circle.Radius,
                ["RoundnessTolerance"] = tolerance,
                ["PointCount"] = circle.InlierPoints.Count
            }
        };
        if (!pass)
            inspection.Failures.Add($"Roundness {roundness:F4}mm exceeds tolerance {tolerance}mm");

        context.Set($"InspectionResult_{Id}", inspection);
    }

    private CircleDetectionResult? GetConnectedCircleResult(ExecutionContext context)
    {
        if (_graph == null) return null;

        var sourceNodeId = _graph.Connections
            .Where(c => c.TargetNodeId == Id)
            .Select(c => c.SourceNodeId)
            .FirstOrDefault();

        if (sourceNodeId != null)
        {
            return context.Get<CircleDetectionResult>($"{ExecutionContext.CircleResultKey}_{sourceNodeId}");
        }

        return null;
    }
}
