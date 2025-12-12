using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Spec Inspection", "Point Cloud/Inspection", "Inspect circle radius against expected value within tolerance")]
public class InspectionNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public void SetGraph(NodeGraph graph)
    {
        _graph = graph;
    }

    public InspectionNode()
    {
        // Auto-inspection mode
        AddParameter<bool>("AutoInspect", false, required: false, displayName: "Auto Inspect",
            description: "Auto-inspect on execution or wait for manual trigger");

        // Radius-only specifications
        AddParameter<float>("ExpectedRadius", 10f, required: true, displayName: "Expected Radius (mm)",
            description: "Expected circle radius");
        AddParameter<float>("RadiusTolerance", 1f, required: true, displayName: "Radius Tolerance (mm)",
            description: "Acceptable error for radius (±)");
    }

    protected override Task ExecuteCoreAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        var autoInspect = GetParameter<bool>("AutoInspect");

        if (!autoInspect)
        {
            // Manual mode - just store circle result for later manual inspection
            var circle = GetConnectedCircleResult(context);
            if (circle != null)
            {
                context.Set($"InspectionInputCircle_{Id}", circle);
            }
            return Task.CompletedTask;
        }

        // Auto mode - perform inspection immediately
        return Task.Run(() => PerformInspection(context), cancellationToken);
    }

    private CircleDetectionResult? GetConnectedCircleResult(ExecutionContext context)
    {
        if (_graph == null) return null;

        // Find connected Circle Detection node
        var circleNodeId = _graph.Connections
            .Where(c => c.TargetNodeId == Id)
            .Select(c => c.SourceNodeId)
            .FirstOrDefault(id => 
            {
                var node = _graph.Nodes.FirstOrDefault(n => n.Id == id);
                return node?.Name == "Circle Detection";
            });

        if (circleNodeId != null)
        {
            return context.Get<CircleDetectionResult>($"{ExecutionContext.CircleResultKey}_{circleNodeId}");
        }

        return null;
    }

    public void PerformInspection(ExecutionContext context)
    {
        var circle = GetConnectedCircleResult(context)
                     ?? context.Get<CircleDetectionResult>($"InspectionInputCircle_{Id}");

        if (circle == null)
            throw new InvalidOperationException("No circle detection result found in context. Run Circle Detection node first.");

        // Get expected values and tolerances
        var expectedRadius = GetParameter<float>("ExpectedRadius");
        var radiusTolerance = GetParameter<float>("RadiusTolerance");

        var result = new InspectionResult
        {
            Measurements = new Dictionary<string, double>
            {
                ["Radius"] = circle.Radius,
                ["ExpectedRadius"] = expectedRadius,
                ["RadiusTolerance"] = radiusTolerance
            }
        };

        // Check radius against expected ± tolerance
        var radiusError = Math.Abs(circle.Radius - expectedRadius);
        if (radiusError > radiusTolerance)
        {
            result.Failures.Add($"Radius {circle.Radius:F3}mm differs from expected {expectedRadius:F3}mm by {radiusError:F3}mm (tolerance: ±{radiusTolerance:F3}mm)");
        }

        result.Pass = result.Failures.Count == 0;
        result.Message = result.Pass
            ? $"Radius OK: {circle.Radius:F2}mm (Expected {expectedRadius:F2}mm ±{radiusTolerance:F2}mm)"
            : $"Radius NG: {string.Join("; ", result.Failures)}";

        // Store in context with unique key
        context.Set($"InspectionResult_{Id}", result);
        context.Set("InspectionResult", result); // Legacy
    }
}
