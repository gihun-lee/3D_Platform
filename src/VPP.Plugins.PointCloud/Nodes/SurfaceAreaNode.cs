using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Surface Area", "Point Cloud/Measurement", "Estimate surface area using local triangulation")]
public class SurfaceAreaNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public void SetGraph(NodeGraph graph)
    {
        _graph = graph;
    }

    public SurfaceAreaNode()
    {
        AddParameter<bool>("AutoMeasure", true, required: false, displayName: "Auto Measure",
            description: "Auto-measure on execution");
        AddParameter<int>("KNeighbors", 6, required: false, displayName: "K Neighbors",
            description: "Number of neighbors for local triangulation");
        AddParameter<int>("SampleSize", 5000, required: false, displayName: "Sample Size",
            description: "Maximum points to sample for calculation");
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
            context.Set($"SurfaceAreaInputCloud_{Id}", cloud);
            return Task.CompletedTask;
        }

        PerformMeasurement(context, cloud);
        return Task.CompletedTask;
    }

    public void PerformMeasurement(ExecutionContext context, PointCloudData cloud)
    {
        var kNeighbors = GetParameter<int>("KNeighbors");
        var sampleSize = Math.Min(GetParameter<int>("SampleSize"), cloud.Points.Count);

        // Sample points if necessary
        var random = new Random(42);
        var sampledPoints = cloud.Points.Count > sampleSize
            ? cloud.Points.OrderBy(_ => random.Next()).Take(sampleSize).ToList()
            : cloud.Points;

        // Estimate surface area using Voronoi-like approach
        var totalArea = EstimateSurfaceArea(sampledPoints, kNeighbors);

        // Scale up if we sampled
        if (cloud.Points.Count > sampleSize)
        {
            totalArea *= (float)cloud.Points.Count / sampleSize;
        }

        var avgTriangleArea = totalArea / Math.Max(1, sampledPoints.Count);

        var result = new SurfaceAreaResult
        {
            EstimatedArea = totalArea,
            TriangleCount = sampledPoints.Count,
            AverageTriangleArea = avgTriangleArea
        };

        context.Set($"SurfaceArea_{Id}", result);

        var inspection = new InspectionResult
        {
            Pass = true,
            Message = $"Surface Area: {totalArea:F2} mm² (from {cloud.Points.Count} points)",
            Measurements = new Dictionary<string, double>
            {
                ["SurfaceArea"] = totalArea,
                ["SurfaceAreaCm2"] = totalArea / 100,
                ["AverageLocalArea"] = avgTriangleArea,
                ["PointCount"] = cloud.Points.Count,
                ["SampledPoints"] = sampledPoints.Count
            }
        };
        context.Set($"InspectionResult_{Id}", inspection);
    }

    private float EstimateSurfaceArea(List<Vector3> points, int kNeighbors)
    {
        if (points.Count < 3) return 0;

        float totalArea = 0;

        foreach (var p in points)
        {
            // Find k nearest neighbors
            var neighbors = points
                .Where(other => other != p)
                .OrderBy(other => Vector3.DistanceSquared(p, other))
                .Take(kNeighbors)
                .ToList();

            if (neighbors.Count < 2) continue;

            // Estimate local area using average triangle with neighbors
            float localArea = 0;
            for (int i = 0; i < neighbors.Count; i++)
            {
                var n1 = neighbors[i];
                var n2 = neighbors[(i + 1) % neighbors.Count];

                // Triangle area using cross product
                var v1 = n1 - p;
                var v2 = n2 - p;
                var cross = Vector3.Cross(v1, v2);
                localArea += cross.Length() * 0.5f;
            }

            // Average the overlapping triangles
            localArea /= kNeighbors;
            totalArea += localArea;
        }

        // Divide by 3 since each triangle is counted 3 times (once per vertex)
        return totalArea / 3;
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
