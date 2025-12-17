using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Point to Plane Distance", "Point Cloud/Measurement", "Measure distance from points to a reference plane")]
public class PointToPlaneDistanceNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public void SetGraph(NodeGraph graph)
    {
        _graph = graph;
    }

    public PointToPlaneDistanceNode()
    {
        AddParameter<bool>("AutoMeasure", true, required: false, displayName: "Auto Measure",
            description: "Auto-measure on execution");

        // Reference plane parameters (if not connected to plane fitting node)
        AddParameter<float>("PlaneNormalX", 0f, required: false, displayName: "Plane Normal X");
        AddParameter<float>("PlaneNormalY", 0f, required: false, displayName: "Plane Normal Y");
        AddParameter<float>("PlaneNormalZ", 1f, required: false, displayName: "Plane Normal Z");
        AddParameter<float>("PlaneD", 0f, required: false, displayName: "Plane D (offset)",
            description: "D in plane equation: ax + by + cz + d = 0");
    }

    protected override Task ExecuteCoreAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        var autoMeasure = GetParameter<bool>("AutoMeasure");
        if (!autoMeasure) return Task.CompletedTask;

        var cloud = GetConnectedCloud(context);
        if (cloud == null || cloud.Count == 0)
        {
            return Task.CompletedTask;
        }

        // Try to get plane from connected node first
        var plane = GetConnectedPlane(context);

        PerformMeasurement(context, cloud, plane);
        return Task.CompletedTask;
    }

    public void PerformMeasurement(ExecutionContext context, PointCloudData cloud, PlaneFittingResult? plane)
    {
        Vector3 normal;
        float d;

        if (plane != null && plane.InlierCount > 0)
        {
            normal = plane.Normal;
            d = plane.D;
        }
        else
        {
            normal = Vector3.Normalize(new Vector3(
                GetParameter<float>("PlaneNormalX"),
                GetParameter<float>("PlaneNormalY"),
                GetParameter<float>("PlaneNormalZ")
            ));
            d = GetParameter<float>("PlaneD");
        }

        // Calculate distances for all points
        var distances = new List<float>();
        var signedDistances = new List<float>();

        foreach (var p in cloud.Points)
        {
            var signedDist = Vector3.Dot(normal, p) + d;
            signedDistances.Add(signedDist);
            distances.Add(Math.Abs(signedDist));
        }

        var minDist = distances.Min();
        var maxDist = distances.Max();
        var avgDist = (float)distances.Average();
        var rmsDist = (float)Math.Sqrt(distances.Select(d => d * d).Average());

        // Find closest and farthest points
        var minIdx = distances.IndexOf(minDist);
        var maxIdx = distances.IndexOf(maxDist);

        // Calculate statistics for signed distances
        var positiveCount = signedDistances.Count(d => d > 0);
        var negativeCount = signedDistances.Count(d => d < 0);

        var inspection = new InspectionResult
        {
            Pass = true,
            Message = $"Point-to-Plane: Min={minDist:F3}mm, Max={maxDist:F3}mm, Avg={avgDist:F3}mm",
            Measurements = new Dictionary<string, double>
            {
                ["MinDistance"] = minDist,
                ["MaxDistance"] = maxDist,
                ["AverageDistance"] = avgDist,
                ["RMSDistance"] = rmsDist,
                ["DistanceRange"] = maxDist - minDist,
                ["PointsAbovePlane"] = positiveCount,
                ["PointsBelowPlane"] = negativeCount,
                ["TotalPoints"] = cloud.Points.Count,
                ["PlaneNormalX"] = normal.X,
                ["PlaneNormalY"] = normal.Y,
                ["PlaneNormalZ"] = normal.Z,
                ["PlaneD"] = d,
                ["ClosestPointX"] = cloud.Points[minIdx].X,
                ["ClosestPointY"] = cloud.Points[minIdx].Y,
                ["ClosestPointZ"] = cloud.Points[minIdx].Z,
                ["FarthestPointX"] = cloud.Points[maxIdx].X,
                ["FarthestPointY"] = cloud.Points[maxIdx].Y,
                ["FarthestPointZ"] = cloud.Points[maxIdx].Z
            }
        };
        context.Set($"InspectionResult_{Id}", inspection);
    }

    private PointCloudData? GetConnectedCloud(ExecutionContext context)
    {
        if (_graph == null) return null;

        var sourceNodeIds = _graph.Connections
            .Where(c => c.TargetNodeId == Id)
            .Select(c => c.SourceNodeId)
            .ToList();

        foreach (var nodeId in sourceNodeIds)
        {
            var cloud = context.Get<PointCloudData>($"{ExecutionContext.FilteredCloudKey}_{nodeId}")
                     ?? context.Get<PointCloudData>($"{ExecutionContext.PointCloudKey}_{nodeId}");
            if (cloud != null)
                return cloud;
        }

        return null;
    }

    private PlaneFittingResult? GetConnectedPlane(ExecutionContext context)
    {
        if (_graph == null) return null;

        var sourceNodeIds = _graph.Connections
            .Where(c => c.TargetNodeId == Id)
            .Select(c => c.SourceNodeId)
            .ToList();

        foreach (var nodeId in sourceNodeIds)
        {
            var plane = context.Get<PlaneFittingResult>($"PlaneFitting_{nodeId}");
            if (plane != null && plane.InlierCount > 0)
                return plane;
        }

        return null;
    }
}
