using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Circle Detection", "Point Cloud/Detection", "Detect circle in point cloud using RANSAC")]
public class CircleDetectionNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public void SetGraph(NodeGraph graph)
    {
        _graph = graph;
    }

    public CircleDetectionNode()
    {
        // Detection mode parameter
        AddParameter<bool>("AutoDetect", false, required: false, displayName: "Auto Detect",
            description: "Auto-detect on execution or wait for manual trigger");

        // RANSAC parameters
        AddParameter<int>("MaxIterations", 2000, required: false, displayName: "Max Iterations",
            description: "Maximum RANSAC iterations");
        AddParameter<float>("DistanceThreshold", 5.0f, required: false, displayName: "Distance Threshold (mm)",
            description: "Distance threshold for inliers");
        AddParameter<float>("MinRadius", 5.0f, required: false, displayName: "Min Radius (mm)",
            description: "Minimum circle radius");
        AddParameter<float>("MaxRadius", 200.0f, required: false, displayName: "Max Radius (mm)",
            description: "Maximum circle radius");
        AddParameter<float>("MinInlierRatio", 0.15f, required: false, displayName: "Min Inlier Ratio",
            description: "Minimum ratio of inlier points (0-1)");
    }

    protected override Task ExecuteCoreAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        // Get filtered cloud from connected ROI Filter node
        var cloud = GetConnectedFilteredCloud(context);

        // If no filtered cloud, skip detection and allow UI to show full cloud.
        if (cloud == null || cloud.Count < 3)
        {
            // Ensure previous detection visuals are cleared
            context.Set($"{ExecutionContext.CircleResultKey}_{Id}", new CircleDetectionResult { FitError = float.MaxValue, InlierCount = 0 });
            context.Set($"DetectedCircleCloud_{Id}", new PointCloudData());
            // Store that detection input is missing (for UI logic)
            context.Set<PointCloudData>($"CircleDetectionInputCloud_{Id}", null);
            return Task.CompletedTask;
        }

        var autoDetect = GetParameter<bool>("AutoDetect");
        
        // If auto-detect is off, skip detection during normal execution; store cloud for manual detection.
        if (!autoDetect)
        {
            context.Set($"CircleDetectionInputCloud_{Id}", cloud);
            return Task.CompletedTask;
        }

        // Perform detection only on filtered cloud
        PerformDetection(context, cloud, cancellationToken);

        return Task.CompletedTask;
    }

    private PointCloudData? GetConnectedFilteredCloud(ExecutionContext context)
    {
        if (_graph == null) return null;

        // Find connected ROI Filter node
        var roiFilterNodeId = _graph.Connections
            .Where(c => c.TargetNodeId == Id)
            .Select(c => c.SourceNodeId)
            .FirstOrDefault(id => 
            {
                var node = _graph.Nodes.FirstOrDefault(n => n.Id == id);
                return node?.Name == "ROI Filter";
            });

        if (roiFilterNodeId != null)
        {
            return context.Get<PointCloudData>($"{ExecutionContext.FilteredCloudKey}_{roiFilterNodeId}");
        }

        return null;
    }

    public void PerformDetection(ExecutionContext context, PointCloudData cloud, CancellationToken cancellationToken)
    {
        var maxIterations = GetParameter<int>("MaxIterations");
        var threshold = GetParameter<float>("DistanceThreshold");
        var minRadius = GetParameter<float>("MinRadius");
        var maxRadius = GetParameter<float>("MaxRadius");
        var minInlierRatio = GetParameter<float>("MinInlierRatio");

        // Try to get ROI to guide plane selection from connected filter
        var roi = GetConnectedRoi(context);

        // Log input data for debugging
        System.Diagnostics.Debug.WriteLine($"Circle Detection: Processing {cloud.Points.Count} points");
        System.Diagnostics.Debug.WriteLine($"Parameters: MaxIter={maxIterations}, Threshold={threshold}mm, Radius=[{minRadius}, {maxRadius}]mm, MinInlierRatio={minInlierRatio}");

        var result = DetectCircleRANSAC(cloud.Points, maxIterations, threshold, minRadius, maxRadius, minInlierRatio, cancellationToken, roi);

        // Log detection result
        if (result.InlierCount > 0)
        {
            System.Diagnostics.Debug.WriteLine($"? Circle detected: Radius={result.Radius:F2}mm, Center=({result.Center.X:F1}, {result.Center.Y:F1}, {result.Center.Z:F1}), Inliers={result.InlierCount}/{cloud.Points.Count}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"? Circle detection failed: No valid circle found");
        }

        // Store result in context with unique key
        context.Set($"{ExecutionContext.CircleResultKey}_{Id}", result);
        // Also set global key for backward compatibility/UI if needed (though UI should use specific keys now)
        context.Set(ExecutionContext.CircleResultKey, result);
        
        // Store detected circle points for visualization (always store, even if empty)
        if (result.InlierPoints != null && result.InlierPoints.Count > 0)
        {
            var detectedCircleCloud = new PointCloudData
            {
                Points = result.InlierPoints.ToList()
            };
            detectedCircleCloud.ComputeBoundingBox();
            context.Set($"DetectedCircleCloud_{Id}", detectedCircleCloud);
            context.Set("DetectedCircleCloud", detectedCircleCloud); // Legacy
        }
        else
        {
            // Clear previous detection if no circle found
            context.Set($"DetectedCircleCloud_{Id}", new PointCloudData());
            context.Set("DetectedCircleCloud", new PointCloudData()); // Legacy
        }
    }

    private ROI3D? GetConnectedRoi(ExecutionContext context)
    {
        if (_graph == null) return null;

        // Find connected ROI Filter node
        var roiFilterNodeId = _graph.Connections
            .Where(c => c.TargetNodeId == Id)
            .Select(c => c.SourceNodeId)
            .FirstOrDefault(id => 
            {
                var node = _graph.Nodes.FirstOrDefault(n => n.Id == id);
                return node?.Name == "ROI Filter";
            });

        if (roiFilterNodeId != null)
        {
            return context.Get<ROI3D>($"{ExecutionContext.ROIKey}_{roiFilterNodeId}");
        }

        return null;
    }

    private CircleDetectionResult DetectCircleRANSAC(

        List<Vector3> points, int maxIter, float threshold,
        float minRadius, float maxRadius, float minInlierRatio, CancellationToken ct, ROI3D? roi = null)
    {
        if (points.Count < 3)
            return new CircleDetectionResult { FitError = float.MaxValue };

        var random = new Random();
        var bestResult = new CircleDetectionResult { FitError = float.MaxValue, InlierCount = 0 };

        // Compute bounding box to understand data distribution
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in points)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        var range = max - min;

        // Select plane
        Vector3 planeNormal;
        Vector3 planePoint;
        if (roi != null && roi.Shape == ROIShape.Cylinder)
        {
            // For line-scan data, the hole lies on XY. Use ROI center as plane point.
            planeNormal = Vector3.UnitZ;
            planePoint = roi.Center;
        }
        else
        {
            planePoint = (min + max) * 0.5f;
            if (range.Z < range.X && range.Z < range.Y)
                planeNormal = Vector3.UnitZ;
            else if (range.Y < range.X && range.Y < range.Z)
                planeNormal = Vector3.UnitY;
            else
                planeNormal = Vector3.UnitX;
        }

        // Project points to 2D on the detected plane
        var (u, v) = GetPlaneAxes(planeNormal);
        var points2D = new List<Vector2>(points.Count);
        foreach (var p in points)
        {
            var rel = p - planePoint;
            points2D.Add(new Vector2(Vector3.Dot(rel, u), Vector3.Dot(rel, v)));
        }

        // Find boundary points (edge of hole) with slightly larger grid for robustness
        var boundaryPoints = FindBoundaryPoints(points2D, Math.Max(1f, threshold * 2));

        // Use boundary points for RANSAC if we found enough
        var samplePoints = boundaryPoints.Count > 10 ? boundaryPoints : points2D;

        for (int iter = 0; iter < maxIter && !ct.IsCancellationRequested; iter++)
        {
            if (samplePoints.Count < 3) break;

            // Pick 3 random points from boundary
            var idx0 = random.Next(samplePoints.Count);
            var idx1 = random.Next(samplePoints.Count);
            var idx2 = random.Next(samplePoints.Count);
            if (idx1 == idx0) idx1 = (idx1 + 1) % samplePoints.Count;
            if (idx2 == idx0 || idx2 == idx1) idx2 = (idx2 + 2) % samplePoints.Count;

            var p1 = samplePoints[idx0];
            var p2 = samplePoints[idx1];
            var p3 = samplePoints[idx2];

            // Fit circle through 3 points
            if (!FitCircle3Points(p1, p2, p3, out var center2D, out var radius))
                continue;

            if (radius < minRadius || radius > maxRadius)
                continue;

            // Count inliers - points that are NEAR the circle boundary
            var inliers = new List<int>(points2D.Count);
            float totalError = 0;
            for (int i = 0; i < points2D.Count; i++)
            {
                var distToCenter = (points2D[i] - center2D).Length();
                var distToCircle = Math.Abs(distToCenter - radius);
                
                if (distToCircle <= threshold)
                {
                    inliers.Add(i);
                    totalError += distToCircle;
                }
            }

            float inlierRatio = (float)inliers.Count / points2D.Count;
            
            // Need minimum number of inliers
            if (inliers.Count > bestResult.InlierCount && inlierRatio >= minInlierRatio)
            {
                // Convert back to 3D
                var center3D = planePoint + center2D.X * u + center2D.Y * v;

                bestResult = new CircleDetectionResult
                {
                    Center = center3D,
                    Radius = radius,
                    Normal = planeNormal,
                    InlierCount = inliers.Count,
                    FitError = inliers.Count > 0 ? totalError / inliers.Count : float.MaxValue,
                    InlierPoints = inliers.Select(i => new Vector3(points[i].X, points[i].Y, points[i].Z)).ToList()
                };
            }
        }

        return bestResult;
    }

    private List<Vector2> FindBoundaryPoints(List<Vector2> points, float gridSize)
    {
        if (points.Count == 0) return new List<Vector2>();

        // Find bounding box
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var p in points)
        {
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }

        // Create a simple grid
        var range = max - min;
        int gridX = Math.Max(10, (int)(range.X / gridSize));
        int gridY = Math.Max(10, (int)(range.Y / gridSize));
        
        var grid = new List<Vector2>[gridX, gridY];
        
        // Assign points to grid cells
        foreach (var p in points)
        {
            int x = Math.Min(gridX - 1, Math.Max(0, (int)((p.X - min.X) / range.X * (gridX - 1))));
            int y = Math.Min(gridY - 1, Math.Max(0, (int)((p.Y - min.Y) / range.Y * (gridY - 1))));
            
            if (grid[x, y] == null)
                grid[x, y] = new List<Vector2>();
            grid[x, y].Add(p);
        }

        // Find boundary points (cells that have empty neighbors)
        var boundaryPoints = new List<Vector2>();
        for (int x = 0; x < gridX; x++)
        {
            for (int y = 0; y < gridY; y++)
            {
                if (grid[x, y] == null || grid[x, y].Count == 0)
                    continue;

                // Check if this cell has any empty neighbors
                bool isBoundary = false;
                for (int dx = -1; dx <= 1 && !isBoundary; dx++)
                {
                    for (int dy = -1; dy <= 1 && !isBoundary; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx;
                        int ny = y + dy;
                        if (nx < 0 || nx >= gridX || ny < 0 || ny >= gridY ||
                            grid[nx, ny] == null || grid[nx, ny].Count == 0)
                        {
                            isBoundary = true;
                        }
                    }
                }

                if (isBoundary)
                    boundaryPoints.AddRange(grid[x, y]);
            }
        }

        return boundaryPoints;
    }

    private bool FitCircle3Points(Vector2 p1, Vector2 p2, Vector2 p3, out Vector2 center, out float radius)
    {
        center = Vector2.Zero;
        radius = 0;

        var ax = p1.X; var ay = p1.Y;
        var bx = p2.X; var by = p2.Y;
        var cx = p3.X; var cy = p3.Y;

        var d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
        if (Math.Abs(d) < 1e-10) return false;

        var ux = ((ax * ax + ay * ay) * (by - cy) + (bx * bx + by * by) * (cy - ay) + (cx * cx + cy * cy) * (ay - by)) / d;
        var uy = ((ax * ax + ay * ay) * (cx - bx) + (bx * bx + by * by) * (ax - cx) + (cx * cx + cy * cy) * (bx - ax)) / d;

        center = new Vector2(ux, uy);
        radius = (p1 - center).Length();
        return true;
    }

    private (Vector3 u, Vector3 v) GetPlaneAxes(Vector3 normal)
    {
        var u = Math.Abs(normal.X) < 0.9f
            ? Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitX))
            : Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitY));
        var v = Vector3.Cross(normal, u);
        return (u, v);
    }
}
