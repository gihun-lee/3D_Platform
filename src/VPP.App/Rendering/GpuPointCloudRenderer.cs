using System;
using System.Collections.Generic;
using System.Linq;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using VPP.Plugins.PointCloud.Models;

namespace VPP.App.Rendering;

/// <summary>
/// GPU-optimized point cloud renderer with LOD support
/// </summary>
public class GpuPointCloudRenderer
{
    // LOD thresholds
    public const int MAX_POINTS_FOR_FULL_RENDER = 10_000_000;
    public const int LOD_THRESHOLD_HIGH = 2_000_000;
    public const int LOD_THRESHOLD_MEDIUM = 5_000_000;

    /// <summary>
    /// Creates GPU-accelerated PointGeometry3D from point cloud data with automatic LOD
    /// </summary>
    public static (PointGeometry3D geometry, int renderedPoints, string lodInfo) CreateGeometry(
        PointCloudData cloudData,
        bool enableLod = true)
    {
        if (cloudData == null || cloudData.Points.Count == 0)
        {
            throw new ArgumentException("Point cloud data is empty", nameof(cloudData));
        }

        var totalPoints = cloudData.Points.Count;
        var hasColors = cloudData.Colors != null && cloudData.Colors.Count == cloudData.Points.Count;

        // Determine LOD level
        int stride = 1;
        string lodInfo = "";

        if (enableLod)
        {
            if (totalPoints > MAX_POINTS_FOR_FULL_RENDER)
            {
                stride = (int)Math.Ceiling((double)totalPoints / MAX_POINTS_FOR_FULL_RENDER);
                lodInfo = $"LOD: 1/{stride}";
            }
            else if (totalPoints > LOD_THRESHOLD_MEDIUM)
            {
                stride = 2;
                lodInfo = "LOD: Medium";
            }
            else if (totalPoints > LOD_THRESHOLD_HIGH)
            {
                stride = 1;
                lodInfo = "LOD: High";
            }
            else
            {
                lodInfo = "LOD: Full";
            }
        }

        // Calculate points to render
        var pointsToRender = (totalPoints + stride - 1) / stride;

        // Create geometry with optimized batching
        var geometry = CreateGeometryInternal(cloudData, stride, pointsToRender, hasColors);

        return (geometry, pointsToRender, lodInfo);
    }

    /// <summary>
    /// Creates geometry from multiple point clouds with automatic merging
    /// </summary>
    public static (PointGeometry3D geometry, int renderedPoints, string lodInfo) CreateGeometryFromMultiple(
        IEnumerable<PointCloudData> clouds,
        bool enableLod = true)
    {
        var cloudList = clouds.Where(c => c != null && c.Points.Count > 0).ToList();
        if (!cloudList.Any())
        {
            throw new ArgumentException("No valid point clouds provided", nameof(clouds));
        }

        // Merge clouds
        var mergedPoints = new List<System.Numerics.Vector3>();
        var mergedColors = new List<System.Numerics.Vector3>();
        bool hasColors = true;

        foreach (var cloud in cloudList)
        {
            mergedPoints.AddRange(cloud.Points);

            if (cloud.Colors != null && cloud.Colors.Count == cloud.Points.Count)
            {
                mergedColors.AddRange(cloud.Colors);
            }
            else
            {
                hasColors = false;
            }
        }

        var mergedCloud = new PointCloudData
        {
            Points = mergedPoints,
            Colors = hasColors ? mergedColors : null
        };

        return CreateGeometry(mergedCloud, enableLod);
    }

    /// <summary>
    /// Internal method to create geometry with optimized GPU transfer
    /// </summary>
    private static PointGeometry3D CreateGeometryInternal(
        PointCloudData cloudData,
        int stride,
        int pointsToRender,
        bool hasColors)
    {
        // Preallocate arrays for GPU transfer (minimizes allocations)
        var positions = new Vector3[pointsToRender];
        var colors = hasColors ? new Color4[pointsToRender] : null;

        // Fast conversion loop with stride-based LOD
        int writeIndex = 0;
        int totalPoints = cloudData.Points.Count;

        for (int i = 0; i < totalPoints; i += stride)
        {
            var pt = cloudData.Points[i];
            positions[writeIndex] = new Vector3(pt.X, pt.Y, pt.Z);

            if (hasColors && colors != null)
            {
                var color = cloudData.Colors![i];
                // Normalize RGB values (0-255 -> 0-1) if needed
                colors[writeIndex] = new Color4(color.X, color.Y, color.Z, 1.0f);
            }

            writeIndex++;
        }

        // Create GPU geometry with vertex buffer
        var geometry = new PointGeometry3D
        {
            Positions = new Vector3Collection(positions)
        };

        if (hasColors && colors != null)
        {
            geometry.Colors = new Color4Collection(colors);
        }

        return geometry;
    }

    /// <summary>
    /// Estimates memory usage for rendering
    /// </summary>
    public static long EstimateMemoryUsage(int pointCount, bool hasColors)
    {
        // Each Vector3 position: 12 bytes (3 floats)
        // Each Color4: 16 bytes (4 floats)
        long memoryBytes = pointCount * 12; // Positions

        if (hasColors)
        {
            memoryBytes += pointCount * 16; // Colors
        }

        return memoryBytes;
    }

    /// <summary>
    /// Gets recommended LOD level for given point count
    /// </summary>
    public static string GetRecommendedLod(int pointCount)
    {
        if (pointCount > MAX_POINTS_FOR_FULL_RENDER)
        {
            return $"High (1/{(int)Math.Ceiling((double)pointCount / MAX_POINTS_FOR_FULL_RENDER)})";
        }
        else if (pointCount > LOD_THRESHOLD_MEDIUM)
        {
            return "Medium (1/2)";
        }
        else if (pointCount > LOD_THRESHOLD_HIGH)
        {
            return "High (Full)";
        }
        else
        {
            return "Full (1/1)";
        }
    }
}
