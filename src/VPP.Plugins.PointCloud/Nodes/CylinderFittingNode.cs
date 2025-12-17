using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Cylinder Fitting", "Point Cloud/Detection", "Fit a cylinder to point cloud using RANSAC")]
public class CylinderFittingNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public void SetGraph(NodeGraph graph)
    {
        _graph = graph;
    }

    public CylinderFittingNode()
    {
        AddParameter<bool>("AutoDetect", true, required: false, displayName: "Auto Detect",
            description: "Auto-detect on execution");
        AddParameter<int>("MaxIterations", 2000, required: false, displayName: "Max Iterations",
            description: "Maximum RANSAC iterations");
        AddParameter<float>("DistanceThreshold", 2.0f, required: false, displayName: "Distance Threshold (mm)",
            description: "Distance threshold for inliers");
        AddParameter<float>("MinRadius", 1.0f, required: false, displayName: "Min Radius (mm)",
            description: "Minimum cylinder radius");
        AddParameter<float>("MaxRadius", 500.0f, required: false, displayName: "Max Radius (mm)",
            description: "Maximum cylinder radius");
        AddParameter<float>("MinInlierRatio", 0.2f, required: false, displayName: "Min Inlier Ratio",
            description: "Minimum ratio of inlier points (0-1)");
    }

    protected override Task ExecuteCoreAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        var autoDetect = GetParameter<bool>("AutoDetect");
        var cloud = GetConnectedCloud(context);

        if (cloud == null || cloud.Count < 6)
        {
            context.Set($"CylinderFitting_{Id}", new CylinderFittingResult { FitError = float.MaxValue });
            return Task.CompletedTask;
        }

        if (!autoDetect)
        {
            context.Set($"CylinderFittingInputCloud_{Id}", cloud);
            return Task.CompletedTask;
        }

        PerformDetection(context, cloud, cancellationToken);
        return Task.CompletedTask;
    }

    public void PerformDetection(ExecutionContext context, PointCloudData cloud, CancellationToken cancellationToken)
    {
        var maxIterations = GetParameter<int>("MaxIterations");
        var threshold = GetParameter<float>("DistanceThreshold");
        var minRadius = GetParameter<float>("MinRadius");
        var maxRadius = GetParameter<float>("MaxRadius");
        var minInlierRatio = GetParameter<float>("MinInlierRatio");

        var result = FitCylinderRANSAC(cloud.Points, maxIterations, threshold, minRadius, maxRadius, minInlierRatio, cancellationToken);

        context.Set($"CylinderFitting_{Id}", result);

        // Create inspection result for UI display
        var inspection = new InspectionResult
        {
            Pass = result.InlierCount > 0,
            Message = result.InlierCount > 0
                ? $"Cylinder: R={result.Radius:F3}mm, H={result.Height:F2}mm, Dir=({result.AxisDirection.X:F2}, {result.AxisDirection.Y:F2}, {result.AxisDirection.Z:F2})"
                : "Cylinder fitting failed",
            Measurements = new Dictionary<string, double>
            {
                ["Radius"] = result.Radius,
                ["Diameter"] = result.Radius * 2,
                ["Height"] = result.Height,
                ["AxisPointX"] = result.AxisPoint.X,
                ["AxisPointY"] = result.AxisPoint.Y,
                ["AxisPointZ"] = result.AxisPoint.Z,
                ["AxisDirectionX"] = result.AxisDirection.X,
                ["AxisDirectionY"] = result.AxisDirection.Y,
                ["AxisDirectionZ"] = result.AxisDirection.Z,
                ["FitError"] = result.FitError,
                ["InlierCount"] = result.InlierCount,
                ["InlierRatio"] = cloud.Count > 0 ? (double)result.InlierCount / cloud.Count : 0
            }
        };
        context.Set($"InspectionResult_{Id}", inspection);
    }

    private CylinderFittingResult FitCylinderRANSAC(List<Vector3> points, int maxIter, float threshold, float minRadius, float maxRadius, float minInlierRatio, CancellationToken ct)
    {
        if (points.Count < 6)
            return new CylinderFittingResult { FitError = float.MaxValue };

        var random = new Random();
        var bestResult = new CylinderFittingResult { FitError = float.MaxValue, InlierCount = 0 };

        for (int iter = 0; iter < maxIter && !ct.IsCancellationRequested; iter++)
        {
            // Pick 2 random points to define axis direction (simplified approach)
            var idx0 = random.Next(points.Count);
            var idx1 = random.Next(points.Count);
            var idx2 = random.Next(points.Count);

            if (idx1 == idx0) idx1 = (idx1 + 1) % points.Count;
            if (idx2 == idx0 || idx2 == idx1) idx2 = (idx2 + 2) % points.Count;

            var p0 = points[idx0];
            var p1 = points[idx1];
            var p2 = points[idx2];

            // Estimate axis direction from normal of plane formed by 3 points (assuming cylinder surface)
            var v1 = p1 - p0;
            var v2 = p2 - p0;
            var cross = Vector3.Cross(v1, v2);

            if (cross.LengthSquared() < 1e-10f)
                continue;

            // Use cross product as potential axis direction
            var axisDir = Vector3.Normalize(cross);
            var axisPoint = (p0 + p1 + p2) / 3;

            // Calculate radius as average distance from axis
            float radiusSum = 0;
            int radiusCount = 0;

            foreach (var p in points.Take(100)) // Sample for speed
            {
                var toPoint = p - axisPoint;
                var alongAxis = Vector3.Dot(toPoint, axisDir) * axisDir;
                var perpendicular = toPoint - alongAxis;
                radiusSum += perpendicular.Length();
                radiusCount++;
            }

            var estimatedRadius = radiusSum / radiusCount;

            if (estimatedRadius < minRadius || estimatedRadius > maxRadius)
                continue;

            // Count inliers
            var inliers = new List<int>();
            float totalError = 0;
            float minAlongAxis = float.MaxValue;
            float maxAlongAxis = float.MinValue;

            for (int i = 0; i < points.Count; i++)
            {
                var toPoint = points[i] - axisPoint;
                var alongAxis = Vector3.Dot(toPoint, axisDir);
                var perpendicular = toPoint - alongAxis * axisDir;
                var distToAxis = perpendicular.Length();
                var error = Math.Abs(distToAxis - estimatedRadius);

                if (error <= threshold)
                {
                    inliers.Add(i);
                    totalError += error;
                    minAlongAxis = Math.Min(minAlongAxis, alongAxis);
                    maxAlongAxis = Math.Max(maxAlongAxis, alongAxis);
                }
            }

            float inlierRatio = (float)inliers.Count / points.Count;

            if (inliers.Count > bestResult.InlierCount && inlierRatio >= minInlierRatio)
            {
                bestResult = new CylinderFittingResult
                {
                    AxisPoint = axisPoint,
                    AxisDirection = axisDir,
                    Radius = estimatedRadius,
                    Height = maxAlongAxis - minAlongAxis,
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
