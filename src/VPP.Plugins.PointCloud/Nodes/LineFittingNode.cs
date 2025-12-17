using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Line Fitting", "Point Cloud/Detection", "Fit a line to point cloud using RANSAC")]
public class LineFittingNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public void SetGraph(NodeGraph graph)
    {
        _graph = graph;
    }

    public LineFittingNode()
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

        if (cloud == null || cloud.Count < 2)
        {
            context.Set($"LineFitting_{Id}", new LineFittingResult { FitError = float.MaxValue });
            return Task.CompletedTask;
        }

        if (!autoDetect)
        {
            context.Set($"LineFittingInputCloud_{Id}", cloud);
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

        var result = FitLineRANSAC(cloud.Points, maxIterations, threshold, minInlierRatio, cancellationToken);

        context.Set($"LineFitting_{Id}", result);

        // Create inspection result for UI display
        var inspection = new InspectionResult
        {
            Pass = result.InlierCount > 0,
            Message = result.InlierCount > 0
                ? $"Line: Length={result.Length:F3}mm, Dir=({result.Direction.X:F3}, {result.Direction.Y:F3}, {result.Direction.Z:F3})"
                : "Line fitting failed",
            Measurements = new Dictionary<string, double>
            {
                ["Length"] = result.Length,
                ["DirectionX"] = result.Direction.X,
                ["DirectionY"] = result.Direction.Y,
                ["DirectionZ"] = result.Direction.Z,
                ["PointX"] = result.Point.X,
                ["PointY"] = result.Point.Y,
                ["PointZ"] = result.Point.Z,
                ["StartX"] = result.StartPoint.X,
                ["StartY"] = result.StartPoint.Y,
                ["StartZ"] = result.StartPoint.Z,
                ["EndX"] = result.EndPoint.X,
                ["EndY"] = result.EndPoint.Y,
                ["EndZ"] = result.EndPoint.Z,
                ["FitError"] = result.FitError,
                ["InlierCount"] = result.InlierCount,
                ["InlierRatio"] = cloud.Count > 0 ? (double)result.InlierCount / cloud.Count : 0
            }
        };
        context.Set($"InspectionResult_{Id}", inspection);
    }

    private LineFittingResult FitLineRANSAC(List<Vector3> points, int maxIter, float threshold, float minInlierRatio, CancellationToken ct)
    {
        if (points.Count < 2)
            return new LineFittingResult { FitError = float.MaxValue };

        var random = new Random();
        var bestResult = new LineFittingResult { FitError = float.MaxValue, InlierCount = 0 };

        for (int iter = 0; iter < maxIter && !ct.IsCancellationRequested; iter++)
        {
            // Pick 2 random points
            var idx0 = random.Next(points.Count);
            var idx1 = random.Next(points.Count);
            if (idx1 == idx0) idx1 = (idx1 + 1) % points.Count;

            var p0 = points[idx0];
            var p1 = points[idx1];

            var direction = p1 - p0;
            if (direction.LengthSquared() < 1e-10f)
                continue;

            direction = Vector3.Normalize(direction);

            // Count inliers
            var inliers = new List<int>();
            float totalError = 0;
            float minT = float.MaxValue;
            float maxT = float.MinValue;

            for (int i = 0; i < points.Count; i++)
            {
                var toPoint = points[i] - p0;
                var t = Vector3.Dot(toPoint, direction);
                var projection = p0 + t * direction;
                var dist = Vector3.Distance(points[i], projection);

                if (dist <= threshold)
                {
                    inliers.Add(i);
                    totalError += dist;
                    minT = Math.Min(minT, t);
                    maxT = Math.Max(maxT, t);
                }
            }

            float inlierRatio = (float)inliers.Count / points.Count;

            if (inliers.Count > bestResult.InlierCount && inlierRatio >= minInlierRatio)
            {
                var startPoint = p0 + minT * direction;
                var endPoint = p0 + maxT * direction;

                bestResult = new LineFittingResult
                {
                    Point = p0,
                    Direction = direction,
                    Length = maxT - minT,
                    StartPoint = startPoint,
                    EndPoint = endPoint,
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
