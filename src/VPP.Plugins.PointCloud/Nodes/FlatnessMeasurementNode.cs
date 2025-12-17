using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Flatness Measurement", "Point Cloud/GD&T", "Measure flatness deviation from best-fit plane")]
public class FlatnessMeasurementNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public void SetGraph(NodeGraph graph)
    {
        _graph = graph;
    }

    public FlatnessMeasurementNode()
    {
        AddParameter<bool>("AutoMeasure", true, required: false, displayName: "Auto Measure",
            description: "Auto-measure on execution");
        AddParameter<int>("MaxIterations", 1000, required: false, displayName: "Max Iterations",
            description: "Maximum RANSAC iterations for plane fitting");
        AddParameter<float>("PlaneThreshold", 2.0f, required: false, displayName: "Plane Threshold (mm)",
            description: "Distance threshold for plane fitting inliers");
        AddParameter<float>("FlatnessTolerance", 0.1f, required: false, displayName: "Flatness Tolerance (mm)",
            description: "Acceptable flatness tolerance for pass/fail");
    }

    protected override Task ExecuteCoreAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        var autoMeasure = GetParameter<bool>("AutoMeasure");
        var cloud = GetConnectedCloud(context);

        if (cloud == null || cloud.Count < 3)
        {
            return Task.CompletedTask;
        }

        if (!autoMeasure)
        {
            context.Set($"FlatnessInputCloud_{Id}", cloud);
            return Task.CompletedTask;
        }

        PerformMeasurement(context, cloud, cancellationToken);
        return Task.CompletedTask;
    }

    public void PerformMeasurement(ExecutionContext context, PointCloudData cloud, CancellationToken cancellationToken)
    {
        var maxIterations = GetParameter<int>("MaxIterations");
        var planeThreshold = GetParameter<float>("PlaneThreshold");
        var flatnessTolerance = GetParameter<float>("FlatnessTolerance");

        // First, fit a plane
        var planeResult = FitPlaneRANSAC(cloud.Points, maxIterations, planeThreshold, cancellationToken);

        if (planeResult.InlierCount == 0)
        {
            context.Set($"FlatnessMeasurement_{Id}", new FlatnessMeasurementResult());
            return;
        }

        // Calculate deviations from plane
        var deviations = new List<float>();
        foreach (var p in cloud.Points)
        {
            var dist = Math.Abs(Vector3.Dot(planeResult.Normal, p) + planeResult.D);
            deviations.Add(dist);
        }

        var maxDeviation = deviations.Max();
        var avgDeviation = deviations.Average();
        var rmsDeviation = (float)Math.Sqrt(deviations.Select(d => d * d).Average());

        var result = new FlatnessMeasurementResult
        {
            MaxDeviation = maxDeviation,
            AverageDeviation = (float)avgDeviation,
            RMSDeviation = rmsDeviation,
            ReferencePlane = planeResult
        };

        context.Set($"FlatnessMeasurement_{Id}", result);

        // Create inspection result for UI display
        var pass = maxDeviation <= flatnessTolerance;
        var inspection = new InspectionResult
        {
            Pass = pass,
            Message = pass
                ? $"Flatness OK: Max={maxDeviation:F4}mm (Tol: {flatnessTolerance}mm)"
                : $"Flatness NG: Max={maxDeviation:F4}mm exceeds tolerance {flatnessTolerance}mm",
            Measurements = new Dictionary<string, double>
            {
                ["MaxDeviation"] = maxDeviation,
                ["AverageDeviation"] = avgDeviation,
                ["RMSDeviation"] = rmsDeviation,
                ["FlatnessTolerance"] = flatnessTolerance,
                ["PlaneNormalX"] = planeResult.Normal.X,
                ["PlaneNormalY"] = planeResult.Normal.Y,
                ["PlaneNormalZ"] = planeResult.Normal.Z,
                ["PlaneD"] = planeResult.D
            }
        };
        if (!pass)
            inspection.Failures.Add($"Flatness {maxDeviation:F4}mm exceeds tolerance {flatnessTolerance}mm");

        context.Set($"InspectionResult_{Id}", inspection);
    }

    private PlaneFittingResult FitPlaneRANSAC(List<Vector3> points, int maxIter, float threshold, CancellationToken ct)
    {
        if (points.Count < 3)
            return new PlaneFittingResult { FitError = float.MaxValue };

        var random = new Random();
        var bestResult = new PlaneFittingResult { FitError = float.MaxValue, InlierCount = 0 };

        for (int iter = 0; iter < maxIter && !ct.IsCancellationRequested; iter++)
        {
            var idx0 = random.Next(points.Count);
            var idx1 = random.Next(points.Count);
            var idx2 = random.Next(points.Count);

            if (idx1 == idx0) idx1 = (idx1 + 1) % points.Count;
            if (idx2 == idx0 || idx2 == idx1) idx2 = (idx2 + 2) % points.Count;

            var p0 = points[idx0];
            var p1 = points[idx1];
            var p2 = points[idx2];

            var v1 = p1 - p0;
            var v2 = p2 - p0;
            var normal = Vector3.Cross(v1, v2);

            if (normal.LengthSquared() < 1e-10f)
                continue;

            normal = Vector3.Normalize(normal);
            var d = -Vector3.Dot(normal, p0);

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

            if (inliers.Count > bestResult.InlierCount)
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
