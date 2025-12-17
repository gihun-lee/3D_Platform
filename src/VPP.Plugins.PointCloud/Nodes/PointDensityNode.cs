using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Point Density", "Point Cloud/Measurement", "Calculate point density and average spacing")]
public class PointDensityNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public void SetGraph(NodeGraph graph)
    {
        _graph = graph;
    }

    public PointDensityNode()
    {
        AddParameter<bool>("AutoMeasure", true, required: false, displayName: "Auto Measure",
            description: "Auto-measure on execution");
        AddParameter<int>("SampleSize", 1000, required: false, displayName: "Sample Size",
            description: "Number of points to sample for spacing calculation");
        AddParameter<int>("KNeighbors", 6, required: false, displayName: "K Neighbors",
            description: "Number of nearest neighbors for spacing calculation");
    }

    protected override Task ExecuteCoreAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        var autoMeasure = GetParameter<bool>("AutoMeasure");
        var cloud = GetConnectedCloud(context);

        if (cloud == null || cloud.Count == 0)
        {
            return Task.CompletedTask;
        }

        if (!autoMeasure)
        {
            context.Set($"PointDensityInputCloud_{Id}", cloud);
            return Task.CompletedTask;
        }

        PerformMeasurement(context, cloud);
        return Task.CompletedTask;
    }

    public void PerformMeasurement(ExecutionContext context, PointCloudData cloud)
    {
        cloud.ComputeBoundingBox();
        var min = new Vector3(cloud.BoundingBox[0], cloud.BoundingBox[1], cloud.BoundingBox[2]);
        var max = new Vector3(cloud.BoundingBox[3], cloud.BoundingBox[4], cloud.BoundingBox[5]);
        var size = max - min;
        var volume = size.X * size.Y * size.Z;

        var density = volume > 0 ? cloud.Points.Count / volume : 0;

        // Calculate average spacing using sampling
        var sampleSize = Math.Min(GetParameter<int>("SampleSize"), cloud.Points.Count);
        var kNeighbors = GetParameter<int>("KNeighbors");
        var avgSpacing = CalculateAverageSpacing(cloud.Points, sampleSize, kNeighbors);

        var result = new PointDensityResult
        {
            Density = density,
            AverageSpacing = avgSpacing,
            TotalPoints = cloud.Points.Count,
            Volume = volume
        };

        context.Set($"PointDensity_{Id}", result);

        // Create inspection result for UI display
        var inspection = new InspectionResult
        {
            Pass = true,
            Message = $"Density: {density:F4} pts/mm³, Spacing: {avgSpacing:F3}mm, Points: {cloud.Points.Count}",
            Measurements = new Dictionary<string, double>
            {
                ["Density"] = density,
                ["AverageSpacing"] = avgSpacing,
                ["TotalPoints"] = cloud.Points.Count,
                ["BoundingVolume"] = volume,
                ["VolumeSizeX"] = size.X,
                ["VolumeSizeY"] = size.Y,
                ["VolumeSizeZ"] = size.Z
            }
        };
        context.Set($"InspectionResult_{Id}", inspection);
    }

    private float CalculateAverageSpacing(List<Vector3> points, int sampleSize, int kNeighbors)
    {
        if (points.Count < 2) return 0;

        var random = new Random(42);
        var sampleIndices = Enumerable.Range(0, points.Count)
            .OrderBy(_ => random.Next())
            .Take(sampleSize)
            .ToList();

        double totalSpacing = 0;
        int count = 0;

        foreach (var idx in sampleIndices)
        {
            var p = points[idx];
            var distances = new List<float>();

            foreach (var other in points)
            {
                if (other != p)
                {
                    distances.Add(Vector3.Distance(p, other));
                }
            }

            distances.Sort();
            var kNearestDistances = distances.Take(Math.Min(kNeighbors, distances.Count));
            if (kNearestDistances.Any())
            {
                totalSpacing += kNearestDistances.Average();
                count++;
            }
        }

        return count > 0 ? (float)(totalSpacing / count) : 0;
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
