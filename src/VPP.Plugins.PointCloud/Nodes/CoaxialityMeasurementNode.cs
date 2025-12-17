using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Coaxiality Measurement", "Point Cloud/GD&T", "Measure coaxiality between two cylinders")]
public class CoaxialityMeasurementNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public void SetGraph(NodeGraph graph)
    {
        _graph = graph;
    }

    public CoaxialityMeasurementNode()
    {
        AddParameter<bool>("AutoMeasure", true, required: false, displayName: "Auto Measure",
            description: "Auto-measure on execution");
        AddParameter<float>("OffsetTolerance", 0.1f, required: false, displayName: "Offset Tolerance (mm)",
            description: "Maximum allowed axis offset");
        AddParameter<float>("AngleTolerance", 1.0f, required: false, displayName: "Angle Tolerance (degrees)",
            description: "Maximum allowed axis angle deviation");
    }

    protected override Task ExecuteCoreAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        var autoMeasure = GetParameter<bool>("AutoMeasure");
        if (!autoMeasure) return Task.CompletedTask;

        var cylinders = GetConnectedCylinderResults(context);

        if (cylinders.Count < 2)
        {
            return Task.CompletedTask;
        }

        PerformMeasurement(context, cylinders[0], cylinders[1]);
        return Task.CompletedTask;
    }

    public void PerformMeasurement(ExecutionContext context, CylinderFittingResult cyl1, CylinderFittingResult cyl2)
    {
        var offsetTolerance = GetParameter<float>("OffsetTolerance");
        var angleTolerance = GetParameter<float>("AngleTolerance");

        // Calculate axis angle deviation
        var dot = Math.Abs(Vector3.Dot(cyl1.AxisDirection, cyl2.AxisDirection));
        dot = Math.Clamp(dot, 0, 1);
        var angleRadians = (float)Math.Acos(dot);
        var angleDegrees = angleRadians * 180f / (float)Math.PI;
        var angleDeviation = Math.Min(angleDegrees, 180 - angleDegrees);

        // Calculate axis offset (perpendicular distance between axes)
        var axisOffset = CalculateAxisOffset(cyl1.AxisPoint, cyl1.AxisDirection, cyl2.AxisPoint, cyl2.AxisDirection);

        var result = new CoaxialityResult
        {
            AxisOffset = axisOffset,
            AngleDeviation = angleDeviation,
            Cylinder1 = cyl1,
            Cylinder2 = cyl2
        };

        context.Set($"CoaxialityMeasurement_{Id}", result);

        var offsetPass = axisOffset <= offsetTolerance;
        var anglePass = angleDeviation <= angleTolerance;
        var pass = offsetPass && anglePass;

        var inspection = new InspectionResult
        {
            Pass = pass,
            Message = pass
                ? $"Coaxiality OK: Offset={axisOffset:F4}mm, Angle={angleDeviation:F4}°"
                : $"Coaxiality NG: Offset={axisOffset:F4}mm (Tol:{offsetTolerance}mm), Angle={angleDeviation:F4}° (Tol:{angleTolerance}°)",
            Measurements = new Dictionary<string, double>
            {
                ["AxisOffset"] = axisOffset,
                ["AngleDeviation"] = angleDeviation,
                ["OffsetTolerance"] = offsetTolerance,
                ["AngleTolerance"] = angleTolerance,
                ["Cyl1AxisX"] = cyl1.AxisDirection.X,
                ["Cyl1AxisY"] = cyl1.AxisDirection.Y,
                ["Cyl1AxisZ"] = cyl1.AxisDirection.Z,
                ["Cyl1Radius"] = cyl1.Radius,
                ["Cyl2AxisX"] = cyl2.AxisDirection.X,
                ["Cyl2AxisY"] = cyl2.AxisDirection.Y,
                ["Cyl2AxisZ"] = cyl2.AxisDirection.Z,
                ["Cyl2Radius"] = cyl2.Radius
            }
        };
        if (!offsetPass)
            inspection.Failures.Add($"Axis offset {axisOffset:F4}mm exceeds tolerance {offsetTolerance}mm");
        if (!anglePass)
            inspection.Failures.Add($"Axis angle deviation {angleDeviation:F4}° exceeds tolerance {angleTolerance}°");

        context.Set($"InspectionResult_{Id}", inspection);
    }

    private float CalculateAxisOffset(Vector3 p1, Vector3 d1, Vector3 p2, Vector3 d2)
    {
        // Calculate shortest distance between two 3D lines
        var w = p1 - p2;
        var a = Vector3.Dot(d1, d1);
        var b = Vector3.Dot(d1, d2);
        var c = Vector3.Dot(d2, d2);
        var d = Vector3.Dot(d1, w);
        var e = Vector3.Dot(d2, w);

        var denom = a * c - b * b;

        if (Math.Abs(denom) < 1e-10f)
        {
            // Lines are parallel, return perpendicular distance
            var crossLength = Vector3.Cross(d1, w).Length();
            return crossLength;
        }

        var sc = (b * e - c * d) / denom;
        var tc = (a * e - b * d) / denom;

        var closestPoint1 = p1 + sc * d1;
        var closestPoint2 = p2 + tc * d2;

        return Vector3.Distance(closestPoint1, closestPoint2);
    }

    private List<CylinderFittingResult> GetConnectedCylinderResults(ExecutionContext context)
    {
        var results = new List<CylinderFittingResult>();
        if (_graph == null) return results;

        var sourceNodeIds = _graph.Connections
            .Where(c => c.TargetNodeId == Id)
            .Select(c => c.SourceNodeId)
            .ToList();

        foreach (var nodeId in sourceNodeIds)
        {
            var cylinder = context.Get<CylinderFittingResult>($"CylinderFitting_{nodeId}");
            if (cylinder != null && cylinder.InlierCount > 0)
                results.Add(cylinder);
        }

        return results;
    }
}
