using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Concentricity Measurement", "Point Cloud/GD&T", "Measure concentricity between two circles")]
public class ConcentricityMeasurementNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public void SetGraph(NodeGraph graph)
    {
        _graph = graph;
    }

    public ConcentricityMeasurementNode()
    {
        AddParameter<bool>("AutoMeasure", true, required: false, displayName: "Auto Measure",
            description: "Auto-measure on execution");
        AddParameter<float>("ConcentricityTolerance", 0.1f, required: false, displayName: "Concentricity Tolerance (mm)",
            description: "Maximum allowed center offset");
    }

    protected override Task ExecuteCoreAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        var autoMeasure = GetParameter<bool>("AutoMeasure");
        if (!autoMeasure) return Task.CompletedTask;

        var circles = GetConnectedCircleResults(context);

        if (circles.Count < 2)
        {
            return Task.CompletedTask;
        }

        PerformMeasurement(context, circles[0], circles[1]);
        return Task.CompletedTask;
    }

    public void PerformMeasurement(ExecutionContext context, CircleDetectionResult circle1, CircleDetectionResult circle2)
    {
        var tolerance = GetParameter<float>("ConcentricityTolerance");

        // Calculate center offset in XY plane (assuming circles are on parallel planes)
        var offset2D = new Vector2(
            circle2.Center.X - circle1.Center.X,
            circle2.Center.Y - circle1.Center.Y
        );
        var centerOffset = offset2D.Length();

        // Also calculate 3D offset
        var offset3D = Vector3.Distance(circle1.Center, circle2.Center);

        var result = new ConcentricityResult
        {
            CenterOffset = centerOffset,
            Center1 = circle1.Center,
            Center2 = circle2.Center,
            Radius1 = circle1.Radius,
            Radius2 = circle2.Radius
        };

        context.Set($"ConcentricityMeasurement_{Id}", result);

        var pass = centerOffset <= tolerance;
        var inspection = new InspectionResult
        {
            Pass = pass,
            Message = pass
                ? $"Concentricity OK: {centerOffset:F4}mm (Tol: {tolerance}mm)"
                : $"Concentricity NG: {centerOffset:F4}mm exceeds tolerance {tolerance}mm",
            Measurements = new Dictionary<string, double>
            {
                ["CenterOffset2D"] = centerOffset,
                ["CenterOffset3D"] = offset3D,
                ["ConcentricityTolerance"] = tolerance,
                ["Circle1CenterX"] = circle1.Center.X,
                ["Circle1CenterY"] = circle1.Center.Y,
                ["Circle1CenterZ"] = circle1.Center.Z,
                ["Circle1Radius"] = circle1.Radius,
                ["Circle2CenterX"] = circle2.Center.X,
                ["Circle2CenterY"] = circle2.Center.Y,
                ["Circle2CenterZ"] = circle2.Center.Z,
                ["Circle2Radius"] = circle2.Radius
            }
        };
        if (!pass)
            inspection.Failures.Add($"Center offset {centerOffset:F4}mm exceeds tolerance {tolerance}mm");

        context.Set($"InspectionResult_{Id}", inspection);
    }

    private List<CircleDetectionResult> GetConnectedCircleResults(ExecutionContext context)
    {
        var results = new List<CircleDetectionResult>();
        if (_graph == null) return results;

        var sourceNodeIds = _graph.Connections
            .Where(c => c.TargetNodeId == Id)
            .Select(c => c.SourceNodeId)
            .ToList();

        foreach (var nodeId in sourceNodeIds)
        {
            var circle = context.Get<CircleDetectionResult>($"{ExecutionContext.CircleResultKey}_{nodeId}");
            if (circle != null && circle.InlierCount > 0)
                results.Add(circle);
        }

        return results;
    }
}
