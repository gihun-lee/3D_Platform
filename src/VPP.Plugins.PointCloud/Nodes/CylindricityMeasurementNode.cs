using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Cylindricity Measurement", "Point Cloud/GD&T", "Measure cylindricity of fitted cylinder")]
public class CylindricityMeasurementNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public void SetGraph(NodeGraph graph)
    {
        _graph = graph;
    }

    public CylindricityMeasurementNode()
    {
        AddParameter<bool>("AutoMeasure", true, required: false, displayName: "Auto Measure",
            description: "Auto-measure on execution");
        AddParameter<float>("CylindricityTolerance", 0.1f, required: false, displayName: "Cylindricity Tolerance (mm)",
            description: "Maximum allowed cylindricity deviation");
    }

    protected override Task ExecuteCoreAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        var autoMeasure = GetParameter<bool>("AutoMeasure");
        if (!autoMeasure) return Task.CompletedTask;

        var cylinder = GetConnectedCylinderResult(context);
        if (cylinder == null || cylinder.InlierCount == 0)
        {
            return Task.CompletedTask;
        }

        PerformMeasurement(context, cylinder);
        return Task.CompletedTask;
    }

    public void PerformMeasurement(ExecutionContext context, CylinderFittingResult cylinder)
    {
        var tolerance = GetParameter<float>("CylindricityTolerance");

        if (cylinder.InlierPoints == null || cylinder.InlierPoints.Count < 3)
        {
            return;
        }

        // Calculate radial deviations from the fitted cylinder axis
        var deviations = new List<float>();
        var radii = new List<float>();

        foreach (var p in cylinder.InlierPoints)
        {
            var toPoint = p - cylinder.AxisPoint;
            var alongAxis = Vector3.Dot(toPoint, cylinder.AxisDirection);
            var projection = cylinder.AxisPoint + alongAxis * cylinder.AxisDirection;
            var radius = Vector3.Distance(p, projection);
            var deviation = Math.Abs(radius - cylinder.Radius);

            deviations.Add(deviation);
            radii.Add(radius);
        }

        var maxDeviation = deviations.Max();
        var avgDeviation = (float)deviations.Average();
        var minRadius = radii.Min();
        var maxRadius = radii.Max();
        var cylindricity = maxRadius - minRadius; // Cylindricity is the radial zone

        context.Set($"CylindricityMeasurement_{Id}", new
        {
            Cylindricity = cylindricity,
            MaxDeviation = maxDeviation,
            MinRadius = minRadius,
            MaxRadius = maxRadius
        });

        var pass = cylindricity <= tolerance;
        var inspection = new InspectionResult
        {
            Pass = pass,
            Message = pass
                ? $"Cylindricity OK: {cylindricity:F4}mm (Tol: {tolerance}mm)"
                : $"Cylindricity NG: {cylindricity:F4}mm exceeds tolerance {tolerance}mm",
            Measurements = new Dictionary<string, double>
            {
                ["Cylindricity"] = cylindricity,
                ["MaxDeviation"] = maxDeviation,
                ["AvgDeviation"] = avgDeviation,
                ["MinRadius"] = minRadius,
                ["MaxRadius"] = maxRadius,
                ["NominalRadius"] = cylinder.Radius,
                ["CylindricityTolerance"] = tolerance,
                ["Height"] = cylinder.Height,
                ["PointCount"] = cylinder.InlierPoints.Count
            }
        };
        if (!pass)
            inspection.Failures.Add($"Cylindricity {cylindricity:F4}mm exceeds tolerance {tolerance}mm");

        context.Set($"InspectionResult_{Id}", inspection);
    }

    private CylinderFittingResult? GetConnectedCylinderResult(ExecutionContext context)
    {
        if (_graph == null) return null;

        var sourceNodeId = _graph.Connections
            .Where(c => c.TargetNodeId == Id)
            .Select(c => c.SourceNodeId)
            .FirstOrDefault();

        if (sourceNodeId != null)
        {
            return context.Get<CylinderFittingResult>($"CylinderFitting_{sourceNodeId}");
        }

        return null;
    }
}
