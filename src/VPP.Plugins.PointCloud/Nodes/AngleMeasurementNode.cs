using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Angle Measurement", "Point Cloud/Measurement", "Measure angle between three points (vertex at middle point)")]
public class AngleMeasurementNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public void SetGraph(NodeGraph graph)
    {
        _graph = graph;
    }

    public AngleMeasurementNode()
    {
        AddParameter<bool>("AutoMeasure", true, required: false, displayName: "Auto Measure",
            description: "Auto-measure on execution");

        // Point 1 (start of first ray)
        AddParameter<float>("Point1X", 0f, required: false, displayName: "Point 1 X (mm)");
        AddParameter<float>("Point1Y", 0f, required: false, displayName: "Point 1 Y (mm)");
        AddParameter<float>("Point1Z", 0f, required: false, displayName: "Point 1 Z (mm)");

        // Vertex point (angle vertex)
        AddParameter<float>("VertexX", 0f, required: false, displayName: "Vertex X (mm)");
        AddParameter<float>("VertexY", 0f, required: false, displayName: "Vertex Y (mm)");
        AddParameter<float>("VertexZ", 0f, required: false, displayName: "Vertex Z (mm)");

        // Point 3 (end of second ray)
        AddParameter<float>("Point3X", 10f, required: false, displayName: "Point 3 X (mm)");
        AddParameter<float>("Point3Y", 0f, required: false, displayName: "Point 3 Y (mm)");
        AddParameter<float>("Point3Z", 0f, required: false, displayName: "Point 3 Z (mm)");
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
        var point1 = new Vector3(
            GetParameter<float>("Point1X"),
            GetParameter<float>("Point1Y"),
            GetParameter<float>("Point1Z")
        );
        var vertex = new Vector3(
            GetParameter<float>("VertexX"),
            GetParameter<float>("VertexY"),
            GetParameter<float>("VertexZ")
        );
        var point3 = new Vector3(
            GetParameter<float>("Point3X"),
            GetParameter<float>("Point3Y"),
            GetParameter<float>("Point3Z")
        );

        // Calculate vectors from vertex to points
        var v1 = Vector3.Normalize(point1 - vertex);
        var v2 = Vector3.Normalize(point3 - vertex);

        // Calculate angle using dot product
        var dot = Vector3.Dot(v1, v2);
        dot = Math.Clamp(dot, -1f, 1f); // Clamp to avoid NaN from floating point errors
        var angleRadians = (float)Math.Acos(dot);
        var angleDegrees = angleRadians * 180f / (float)Math.PI;

        var result = new AngleMeasurementResult
        {
            Point1 = point1,
            Vertex = vertex,
            Point3 = point3,
            AngleRadians = angleRadians,
            AngleDegrees = angleDegrees
        };

        context.Set($"AngleMeasurement_{Id}", result);

        // Create inspection result for UI display
        var inspection = new InspectionResult
        {
            Pass = true,
            Message = $"Angle: {angleDegrees:F2}° ({angleRadians:F4} rad)",
            Measurements = new Dictionary<string, double>
            {
                ["AngleDegrees"] = angleDegrees,
                ["AngleRadians"] = angleRadians,
                ["Point1X"] = point1.X,
                ["Point1Y"] = point1.Y,
                ["Point1Z"] = point1.Z,
                ["VertexX"] = vertex.X,
                ["VertexY"] = vertex.Y,
                ["VertexZ"] = vertex.Z,
                ["Point3X"] = point3.X,
                ["Point3Y"] = point3.Y,
                ["Point3Z"] = point3.Z
            }
        };
        context.Set($"InspectionResult_{Id}", inspection);
    }
}
