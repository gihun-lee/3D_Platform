using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Sphere Fitting", "Point Cloud/Detection", "Fit a sphere to point cloud using RANSAC")]
public class SphereFittingNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public void SetGraph(NodeGraph graph)
    {
        _graph = graph;
    }

    public SphereFittingNode()
    {
        AddParameter<bool>("AutoDetect", true, required: false, displayName: "Auto Detect",
            description: "Auto-detect on execution");
        AddParameter<int>("MaxIterations", 1000, required: false, displayName: "Max Iterations",
            description: "Maximum RANSAC iterations");
        AddParameter<float>("DistanceThreshold", 1.0f, required: false, displayName: "Distance Threshold (mm)",
            description: "Distance threshold for inliers");
        AddParameter<float>("MinRadius", 1.0f, required: false, displayName: "Min Radius (mm)",
            description: "Minimum sphere radius");
        AddParameter<float>("MaxRadius", 500.0f, required: false, displayName: "Max Radius (mm)",
            description: "Maximum sphere radius");
        AddParameter<float>("MinInlierRatio", 0.3f, required: false, displayName: "Min Inlier Ratio",
            description: "Minimum ratio of inlier points (0-1)");
    }

    protected override Task ExecuteCoreAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        var autoDetect = GetParameter<bool>("AutoDetect");
        var cloud = GetConnectedCloud(context);

        if (cloud == null || cloud.Count < 4)
        {
            context.Set($"SphereFitting_{Id}", new SphereFittingResult { FitError = float.MaxValue });
            return Task.CompletedTask;
        }

        if (!autoDetect)
        {
            context.Set($"SphereFittingInputCloud_{Id}", cloud);
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

        var result = FitSphereRANSAC(cloud.Points, maxIterations, threshold, minRadius, maxRadius, minInlierRatio, cancellationToken);

        context.Set($"SphereFitting_{Id}", result);

        // Create inspection result for UI display
        var inspection = new InspectionResult
        {
            Pass = result.InlierCount > 0,
            Message = result.InlierCount > 0
                ? $"Sphere: Center=({result.Center.X:F2}, {result.Center.Y:F2}, {result.Center.Z:F2}), R={result.Radius:F3}mm, Inliers={result.InlierCount}"
                : "Sphere fitting failed",
            Measurements = new Dictionary<string, double>
            {
                ["CenterX"] = result.Center.X,
                ["CenterY"] = result.Center.Y,
                ["CenterZ"] = result.Center.Z,
                ["Radius"] = result.Radius,
                ["Diameter"] = result.Radius * 2,
                ["FitError"] = result.FitError,
                ["InlierCount"] = result.InlierCount,
                ["InlierRatio"] = cloud.Count > 0 ? (double)result.InlierCount / cloud.Count : 0
            }
        };
        context.Set($"InspectionResult_{Id}", inspection);
    }

    private SphereFittingResult FitSphereRANSAC(List<Vector3> points, int maxIter, float threshold, float minRadius, float maxRadius, float minInlierRatio, CancellationToken ct)
    {
        if (points.Count < 4)
            return new SphereFittingResult { FitError = float.MaxValue };

        var random = new Random();
        var bestResult = new SphereFittingResult { FitError = float.MaxValue, InlierCount = 0 };

        for (int iter = 0; iter < maxIter && !ct.IsCancellationRequested; iter++)
        {
            // Pick 4 random points
            var indices = new HashSet<int>();
            while (indices.Count < 4)
                indices.Add(random.Next(points.Count));

            var samplePoints = indices.Select(i => points[i]).ToArray();

            // Fit sphere through 4 points
            if (!FitSphere4Points(samplePoints, out var center, out var radius))
                continue;

            if (radius < minRadius || radius > maxRadius)
                continue;

            // Count inliers
            var inliers = new List<int>();
            float totalError = 0;

            for (int i = 0; i < points.Count; i++)
            {
                var dist = Math.Abs(Vector3.Distance(points[i], center) - radius);
                if (dist <= threshold)
                {
                    inliers.Add(i);
                    totalError += dist;
                }
            }

            float inlierRatio = (float)inliers.Count / points.Count;

            if (inliers.Count > bestResult.InlierCount && inlierRatio >= minInlierRatio)
            {
                bestResult = new SphereFittingResult
                {
                    Center = center,
                    Radius = radius,
                    InlierCount = inliers.Count,
                    FitError = inliers.Count > 0 ? totalError / inliers.Count : float.MaxValue,
                    InlierPoints = inliers.Select(i => points[i]).ToList()
                };
            }
        }

        return bestResult;
    }

    private bool FitSphere4Points(Vector3[] points, out Vector3 center, out float radius)
    {
        center = Vector3.Zero;
        radius = 0;

        // Solve using determinant method
        var p0 = points[0];
        var p1 = points[1];
        var p2 = points[2];
        var p3 = points[3];

        // Translate to p0 as origin
        var a = p1 - p0;
        var b = p2 - p0;
        var c = p3 - p0;

        var d1 = a.LengthSquared();
        var d2 = b.LengthSquared();
        var d3 = c.LengthSquared();

        // Calculate determinant
        var det = 2 * (a.X * (b.Y * c.Z - c.Y * b.Z) -
                       a.Y * (b.X * c.Z - c.X * b.Z) +
                       a.Z * (b.X * c.Y - c.X * b.Y));

        if (Math.Abs(det) < 1e-10f)
            return false;

        var cx = (d1 * (b.Y * c.Z - c.Y * b.Z) - d2 * (a.Y * c.Z - c.Y * a.Z) + d3 * (a.Y * b.Z - b.Y * a.Z)) / det;
        var cy = (d1 * (c.X * b.Z - b.X * c.Z) + d2 * (a.X * c.Z - c.X * a.Z) - d3 * (a.X * b.Z - b.X * a.Z)) / det;
        var cz = (d1 * (b.X * c.Y - c.X * b.Y) - d2 * (a.X * c.Y - c.X * a.Y) + d3 * (a.X * b.Y - b.X * a.Y)) / det;

        center = new Vector3(cx, cy, cz) + p0;
        radius = Vector3.Distance(center, p0);

        return true;
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
