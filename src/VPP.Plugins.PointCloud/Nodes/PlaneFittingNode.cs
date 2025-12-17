using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Plane Fitting", "Point Cloud/Detection", "Fit a plane to point cloud using RANSAC")]
public class PlaneFittingNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public void SetGraph(NodeGraph graph)
    {
        _graph = graph;
    }

    public PlaneFittingNode()
    {
        AddParameter<bool>("AutoDetect", true, required: false, displayName: "Auto Detect",
            description: "Auto-detect on execution");
        AddParameter<int>("MaxIterations", 1000, required: false, displayName: "Max Iterations",
            description: "Maximum RANSAC iterations");
        AddParameter<float>("DistanceThreshold", 1.0f, required: false, displayName: "Distance Threshold (mm)",
            description: "Distance threshold for inliers");
        AddParameter<float>("MinInlierRatio", 0.3f, required: false, displayName: "Min Inlier Ratio",
            description: "Minimum ratio of inlier points (0-1)");
    }

    protected override Task ExecuteCoreAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        var autoDetect = GetParameter<bool>("AutoDetect");
        var cloud = GetConnectedCloud(context);

        if (cloud == null || cloud.Count < 3)
        {
            context.Set($"PlaneFitting_{Id}", new PlaneFittingResult { FitError = float.MaxValue });
            return Task.CompletedTask;
        }

        if (!autoDetect)
        {
            context.Set($"PlaneFittingInputCloud_{Id}", cloud);
            return Task.CompletedTask;
        }

        PerformDetection(context, cloud, cancellationToken);
        return Task.CompletedTask;
    }

    public void PerformDetection(ExecutionContext context, PointCloudData cloud, CancellationToken cancellationToken)
    {
        var maxIterations = GetParameter<int>("MaxIterations");
        var threshold = GetParameter<float>("DistanceThreshold");
        var minInlierRatio = GetParameter<float>("MinInlierRatio");

        var result = FitPlaneRANSAC(cloud.Points, maxIterations, threshold, minInlierRatio, cancellationToken);

        context.Set($"PlaneFitting_{Id}", result);

        // Create inspection result for UI display
        var inspection = new InspectionResult
        {
            Pass = result.InlierCount > 0,
            Message = result.InlierCount > 0
                ? $"Plane: Normal=({result.Normal.X:F3}, {result.Normal.Y:F3}, {result.Normal.Z:F3}), Inliers={result.InlierCount}"
                : "Plane fitting failed",
            Measurements = new Dictionary<string, double>
            {
                ["NormalX"] = result.Normal.X,
                ["NormalY"] = result.Normal.Y,
                ["NormalZ"] = result.Normal.Z,
                ["D"] = result.D,
                ["FitError"] = result.FitError,
                ["InlierCount"] = result.InlierCount,
                ["InlierRatio"] = cloud.Count > 0 ? (double)result.InlierCount / cloud.Count : 0
            }
        };
        context.Set($"InspectionResult_{Id}", inspection);
    }

    private PlaneFittingResult FitPlaneRANSAC(List<Vector3> points, int maxIter, float threshold, float minInlierRatio, CancellationToken ct)
    {
        if (points.Count < 3)
            return new PlaneFittingResult { FitError = float.MaxValue };

        var random = new Random();
        var bestResult = new PlaneFittingResult { FitError = float.MaxValue, InlierCount = 0 };

        for (int iter = 0; iter < maxIter && !ct.IsCancellationRequested; iter++)
        {
            // Pick 3 random points
            var idx0 = random.Next(points.Count);
            var idx1 = random.Next(points.Count);
            var idx2 = random.Next(points.Count);

            if (idx1 == idx0) idx1 = (idx1 + 1) % points.Count;
            if (idx2 == idx0 || idx2 == idx1) idx2 = (idx2 + 2) % points.Count;

            var p0 = points[idx0];
            var p1 = points[idx1];
            var p2 = points[idx2];

            // Calculate plane normal
            var v1 = p1 - p0;
            var v2 = p2 - p0;
            var normal = Vector3.Cross(v1, v2);

            if (normal.LengthSquared() < 1e-10f)
                continue;

            normal = Vector3.Normalize(normal);
            var d = -Vector3.Dot(normal, p0);

            // Count inliers
            var inliers = new List<int>();
            float totalError = 0;

            for (int i = 0; i < points.Count; i++)
            {
                var dist = Math.Abs(Vector3.Dot(normal, points[i]) + d);
                if (dist <= threshold)
                {
                    inliers.Add(i);
                    totalError += dist;
                }
            }

            float inlierRatio = (float)inliers.Count / points.Count;

            if (inliers.Count > bestResult.InlierCount && inlierRatio >= minInlierRatio)
            {
                bestResult = new PlaneFittingResult
                {
                    Normal = normal,
                    Point = p0,
                    D = d,
                    InlierCount = inliers.Count,
                    FitError = inliers.Count > 0 ? totalError / inliers.Count : float.MaxValue,
                    InlierPoints = inliers.Select(i => points[i]).ToList()
                };
            }
        }

        return bestResult;
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
