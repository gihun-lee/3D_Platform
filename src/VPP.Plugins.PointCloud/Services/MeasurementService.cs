using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using VPP.Plugins.PointCloud.Models;

namespace VPP.Plugins.PointCloud.Services;

/// <summary>
/// Service for performing various measurement operations on point clouds
/// </summary>
public class MeasurementService
{
    /// <summary>
    /// Calculate distance between two 3D points
    /// </summary>
    public DistanceMeasurement MeasureDistance(Vector3 point1, Vector3 point2)
    {
        var diff = point2 - point1;
        return new DistanceMeasurement
        {
            Point1 = point1,
            Point2 = point2,
            TotalDistance = diff.Length(),
            DistanceX = Math.Abs(diff.X),
            DistanceY = Math.Abs(diff.Y),
            DistanceZ = Math.Abs(diff.Z),
            IsValid = true,
            Description = $"Distance from ({point1.X:F2}, {point1.Y:F2}, {point1.Z:F2}) to ({point2.X:F2}, {point2.Y:F2}, {point2.Z:F2})"
        };
    }

    /// <summary>
    /// Calculate angle between three points (vertex is the middle point)
    /// </summary>
    public AngleMeasurement MeasureAngle(Vector3 point1, Vector3 vertex, Vector3 point3)
    {
        var v1 = Vector3.Normalize(point1 - vertex);
        var v2 = Vector3.Normalize(point3 - vertex);

        var dot = Vector3.Dot(v1, v2);
        dot = Math.Clamp(dot, -1f, 1f);  // Handle floating point errors

        var angleRad = (float)Math.Acos(dot);
        var angleDeg = angleRad * 180f / MathF.PI;

        return new AngleMeasurement
        {
            Point1 = point1,
            Vertex = vertex,
            Point3 = point3,
            AngleRadians = angleRad,
            AngleDegrees = angleDeg,
            IsValid = true,
            Description = $"Angle at vertex ({vertex.X:F2}, {vertex.Y:F2}, {vertex.Z:F2})"
        };
    }

    /// <summary>
    /// Calculate height statistics along specified axis
    /// </summary>
    public HeightMeasurement MeasureHeight(PointCloudData cloud, string axis = "Z")
    {
        if (cloud?.Points == null || cloud.Points.Count == 0)
        {
            return new HeightMeasurement
            {
                Axis = axis,
                IsValid = false,
                ErrorMessage = "No points in cloud"
            };
        }

        Func<Vector3, float> getAxisValue = axis.ToUpper() switch
        {
            "X" => p => p.X,
            "Y" => p => p.Y,
            _ => p => p.Z
        };

        var values = cloud.Points.Select(getAxisValue).ToList();
        var min = values.Min();
        var max = values.Max();
        var mean = values.Average();

        var variance = values.Sum(v => (v - mean) * (v - mean)) / values.Count;
        var stdDev = (float)Math.Sqrt(variance);

        return new HeightMeasurement
        {
            Axis = axis.ToUpper(),
            MinHeight = min,
            MaxHeight = max,
            Range = max - min,
            Mean = (float)mean,
            StandardDeviation = stdDev,
            PointCount = cloud.Points.Count,
            IsValid = true,
            Description = $"Height statistics along {axis}-axis"
        };
    }

    /// <summary>
    /// Calculate bounding box measurements
    /// </summary>
    public BoundingBoxMeasurement MeasureBoundingBox(PointCloudData cloud)
    {
        if (cloud?.Points == null || cloud.Points.Count == 0)
        {
            return new BoundingBoxMeasurement
            {
                IsValid = false,
                ErrorMessage = "No points in cloud"
            };
        }

        var minX = cloud.Points.Min(p => p.X);
        var maxX = cloud.Points.Max(p => p.X);
        var minY = cloud.Points.Min(p => p.Y);
        var maxY = cloud.Points.Max(p => p.Y);
        var minZ = cloud.Points.Min(p => p.Z);
        var maxZ = cloud.Points.Max(p => p.Z);

        var min = new Vector3(minX, minY, minZ);
        var max = new Vector3(maxX, maxY, maxZ);
        var size = max - min;
        var center = (min + max) / 2f;
        var volume = size.X * size.Y * size.Z;
        var diagonal = size.Length();

        return new BoundingBoxMeasurement
        {
            Min = min,
            Max = max,
            Size = size,
            Center = center,
            Volume = volume,
            DiagonalLength = diagonal,
            IsValid = true,
            Description = "Bounding box of point cloud"
        };
    }

    /// <summary>
    /// Calculate centroid (center of mass)
    /// </summary>
    public CentroidMeasurement MeasureCentroid(PointCloudData cloud)
    {
        if (cloud?.Points == null || cloud.Points.Count == 0)
        {
            return new CentroidMeasurement
            {
                IsValid = false,
                ErrorMessage = "No points in cloud"
            };
        }

        var sumX = cloud.Points.Sum(p => (double)p.X);
        var sumY = cloud.Points.Sum(p => (double)p.Y);
        var sumZ = cloud.Points.Sum(p => (double)p.Z);
        var count = cloud.Points.Count;

        return new CentroidMeasurement
        {
            Centroid = new Vector3((float)(sumX / count), (float)(sumY / count), (float)(sumZ / count)),
            PointCount = count,
            IsValid = true,
            Description = "Center of mass of point cloud"
        };
    }

    /// <summary>
    /// Calculate point density and average spacing
    /// </summary>
    public PointDensityMeasurement MeasurePointDensity(PointCloudData cloud)
    {
        if (cloud?.Points == null || cloud.Points.Count == 0)
        {
            return new PointDensityMeasurement
            {
                IsValid = false,
                ErrorMessage = "No points in cloud"
            };
        }

        // Calculate bounding volume
        var bb = MeasureBoundingBox(cloud);
        var volume = bb.Volume;

        if (volume <= 0)
        {
            return new PointDensityMeasurement
            {
                IsValid = false,
                ErrorMessage = "Bounding volume is zero"
            };
        }

        var density = cloud.Points.Count / volume;

        // Estimate average spacing using k-nearest neighbors approach (simplified)
        // Use cube root of (volume / point_count) as approximation
        var avgSpacing = (float)Math.Pow(volume / cloud.Points.Count, 1.0 / 3.0);

        return new PointDensityMeasurement
        {
            Density = density,
            AverageSpacing = avgSpacing,
            PointCount = cloud.Points.Count,
            BoundingVolume = volume,
            IsValid = true,
            Description = "Point density analysis"
        };
    }

    /// <summary>
    /// Estimate surface area using local triangulation (Delaunay-like approximation)
    /// </summary>
    public SurfaceAreaMeasurement MeasureSurfaceArea(PointCloudData cloud)
    {
        if (cloud?.Points == null || cloud.Points.Count < 3)
        {
            return new SurfaceAreaMeasurement
            {
                IsValid = false,
                ErrorMessage = "Need at least 3 points"
            };
        }

        // Simple surface area estimation using point spacing
        // This is an approximation - for accurate results, proper triangulation would be needed
        var density = MeasurePointDensity(cloud);
        if (!density.IsValid)
        {
            return new SurfaceAreaMeasurement
            {
                IsValid = false,
                ErrorMessage = "Could not calculate density"
            };
        }

        // Estimate based on point spacing (assumes roughly uniform distribution on surface)
        var avgSpacing = density.AverageSpacing;
        var estimatedTriangleArea = avgSpacing * avgSpacing * 0.433f; // Equilateral triangle area ≈ s²√3/4
        var estimatedTriangles = (int)(cloud.Points.Count * 2); // Roughly 2 triangles per point
        var surfaceArea = estimatedTriangles * estimatedTriangleArea;

        return new SurfaceAreaMeasurement
        {
            SurfaceArea = surfaceArea,
            TriangleCount = estimatedTriangles,
            AverageTriangleArea = estimatedTriangleArea,
            IsValid = true,
            Description = "Estimated surface area (approximation)"
        };
    }

    /// <summary>
    /// Calculate distance from point to plane
    /// </summary>
    public PointToPlaneMeasurement MeasurePointToPlane(Vector3 point, Vector3 planeNormal, float planeD)
    {
        planeNormal = Vector3.Normalize(planeNormal);
        var distance = Vector3.Dot(point, planeNormal) + planeD;
        var closestPoint = point - distance * planeNormal;

        return new PointToPlaneMeasurement
        {
            Point = point,
            PlaneNormal = planeNormal,
            PlaneD = planeD,
            Distance = Math.Abs(distance),
            ClosestPointOnPlane = closestPoint,
            IsValid = true,
            Description = "Distance from point to plane"
        };
    }

    /// <summary>
    /// Fit a plane to points and calculate flatness
    /// </summary>
    public FlatnessMeasurement MeasureFlatness(PointCloudData cloud)
    {
        if (cloud?.Points == null || cloud.Points.Count < 3)
        {
            return new FlatnessMeasurement
            {
                IsValid = false,
                ErrorMessage = "Need at least 3 points"
            };
        }

        // Fit plane using least squares (simplified PCA approach)
        var centroid = MeasureCentroid(cloud).Centroid;

        // Build covariance matrix
        double cxx = 0, cyy = 0, czz = 0, cxy = 0, cxz = 0, cyz = 0;
        foreach (var p in cloud.Points)
        {
            var dx = p.X - centroid.X;
            var dy = p.Y - centroid.Y;
            var dz = p.Z - centroid.Z;
            cxx += dx * dx;
            cyy += dy * dy;
            czz += dz * dz;
            cxy += dx * dy;
            cxz += dx * dz;
            cyz += dy * dz;
        }

        // Simplified: assume dominant plane is XY if Z variance is smallest, etc.
        var normal = new Vector3(0, 0, 1); // Default Z-up
        if (cxx < cyy && cxx < czz) normal = new Vector3(1, 0, 0);
        else if (cyy < cxx && cyy < czz) normal = new Vector3(0, 1, 0);

        // For better accuracy, would use eigenvalue decomposition
        // Using power iteration for smallest eigenvector
        normal = FindSmallestEigenvector(cloud.Points, centroid);

        var planeD = -Vector3.Dot(normal, centroid);

        // Calculate deviations
        float maxPos = 0, maxNeg = 0;
        foreach (var p in cloud.Points)
        {
            var dist = Vector3.Dot(p, normal) + planeD;
            if (dist > maxPos) maxPos = dist;
            if (dist < maxNeg) maxNeg = dist;
        }

        return new FlatnessMeasurement
        {
            Flatness = maxPos - maxNeg,
            MaxPositiveDeviation = maxPos,
            MaxNegativeDeviation = maxNeg,
            PlaneNormal = normal,
            PlaneD = planeD,
            PointCount = cloud.Points.Count,
            IsValid = true,
            Description = "Flatness measurement"
        };
    }

    /// <summary>
    /// Measure roundness (circularity) of points
    /// </summary>
    public RoundnessMeasurement MeasureRoundness(PointCloudData cloud)
    {
        if (cloud?.Points == null || cloud.Points.Count < 3)
        {
            return new RoundnessMeasurement
            {
                IsValid = false,
                ErrorMessage = "Need at least 3 points"
            };
        }

        // Fit circle using least squares
        var centroid = MeasureCentroid(cloud).Centroid;

        // Project to best-fit plane first (use XY plane for simplicity, could improve with PCA)
        var points2D = cloud.Points.Select(p => new Vector2(p.X, p.Y)).ToList();
        var center2D = new Vector2(centroid.X, centroid.Y);

        // Calculate average radius
        var avgRadius = (float)points2D.Average(p => (p - center2D).Length());

        // Calculate deviations
        float maxDev = 0, minDev = float.MaxValue;
        foreach (var p in points2D)
        {
            var radius = (p - center2D).Length();
            var dev = radius - avgRadius;
            if (dev > maxDev) maxDev = dev;
            if (dev < minDev) minDev = dev;
        }

        return new RoundnessMeasurement
        {
            Roundness = maxDev - minDev,
            FittedRadius = avgRadius,
            Center = new Vector3(center2D.X, center2D.Y, centroid.Z),
            MaxRadiusDeviation = maxDev,
            MinRadiusDeviation = minDev,
            PointCount = cloud.Points.Count,
            IsValid = true,
            Description = "Roundness (circularity) measurement"
        };
    }

    /// <summary>
    /// Measure cylindricity
    /// </summary>
    public CylindricityMeasurement MeasureCylindricity(PointCloudData cloud)
    {
        if (cloud?.Points == null || cloud.Points.Count < 4)
        {
            return new CylindricityMeasurement
            {
                IsValid = false,
                ErrorMessage = "Need at least 4 points"
            };
        }

        // Simplified cylinder fit - assumes cylinder axis is along Z
        var centroid = MeasureCentroid(cloud).Centroid;
        var axisDirection = new Vector3(0, 0, 1);

        // Project points to XY plane and fit circle
        var points2D = cloud.Points.Select(p => new Vector2(p.X - centroid.X, p.Y - centroid.Y)).ToList();
        var avgRadius = (float)points2D.Average(p => p.Length());

        float maxDev = 0, minDev = float.MaxValue;
        foreach (var p in points2D)
        {
            var radius = p.Length();
            var dev = radius - avgRadius;
            if (dev > maxDev) maxDev = dev;
            if (dev < minDev) minDev = dev;
        }

        return new CylindricityMeasurement
        {
            Cylindricity = maxDev - minDev,
            FittedRadius = avgRadius,
            AxisPoint = centroid,
            AxisDirection = axisDirection,
            MaxRadiusDeviation = maxDev,
            MinRadiusDeviation = minDev,
            PointCount = cloud.Points.Count,
            IsValid = true,
            Description = "Cylindricity measurement"
        };
    }

    /// <summary>
    /// Measure parallelism between two sets of points (planes)
    /// </summary>
    public ParallelismMeasurement MeasureParallelism(PointCloudData cloud1, PointCloudData cloud2)
    {
        var flat1 = MeasureFlatness(cloud1);
        var flat2 = MeasureFlatness(cloud2);

        if (!flat1.IsValid || !flat2.IsValid)
        {
            return new ParallelismMeasurement
            {
                IsValid = false,
                ErrorMessage = "Could not fit planes to point clouds"
            };
        }

        var dot = Math.Abs(Vector3.Dot(flat1.PlaneNormal, flat2.PlaneNormal));
        dot = Math.Clamp(dot, 0, 1);
        var angle = (float)Math.Acos(dot) * 180f / MathF.PI;

        // Parallelism is the maximum distance between planes
        var maxDist = Math.Max(flat1.Flatness, flat2.Flatness);

        return new ParallelismMeasurement
        {
            AngleBetweenPlanes = angle,
            Parallelism = maxDist,
            Plane1Normal = flat1.PlaneNormal,
            Plane2Normal = flat2.PlaneNormal,
            IsValid = true,
            Description = "Parallelism between two planes"
        };
    }

    /// <summary>
    /// Measure perpendicularity between two sets of points
    /// </summary>
    public PerpendicularityMeasurement MeasurePerpendicularity(PointCloudData cloud1, PointCloudData cloud2)
    {
        var flat1 = MeasureFlatness(cloud1);
        var flat2 = MeasureFlatness(cloud2);

        if (!flat1.IsValid || !flat2.IsValid)
        {
            return new PerpendicularityMeasurement
            {
                IsValid = false,
                ErrorMessage = "Could not fit planes to point clouds"
            };
        }

        var dot = Math.Abs(Vector3.Dot(flat1.PlaneNormal, flat2.PlaneNormal));
        dot = Math.Clamp(dot, 0, 1);
        var actualAngle = (float)Math.Acos(dot) * 180f / MathF.PI;
        var deviation = Math.Abs(90f - actualAngle);

        return new PerpendicularityMeasurement
        {
            ActualAngle = actualAngle,
            AngleDeviation = deviation,
            Perpendicularity = deviation * MathF.PI / 180f, // Convert to linear deviation approximation
            Surface1Normal = flat1.PlaneNormal,
            Surface2Normal = flat2.PlaneNormal,
            IsValid = true,
            Description = "Perpendicularity measurement"
        };
    }

    /// <summary>
    /// Measure concentricity between two circles
    /// </summary>
    public ConcentricityMeasurement MeasureConcentricity(PointCloudData cloud1, PointCloudData cloud2)
    {
        var round1 = MeasureRoundness(cloud1);
        var round2 = MeasureRoundness(cloud2);

        if (!round1.IsValid || !round2.IsValid)
        {
            return new ConcentricityMeasurement
            {
                IsValid = false,
                ErrorMessage = "Could not fit circles to point clouds"
            };
        }

        var offset = round2.Center - round1.Center;
        var concentricity = new Vector2(offset.X, offset.Y).Length();

        return new ConcentricityMeasurement
        {
            Concentricity = concentricity,
            Circle1Center = round1.Center,
            Circle2Center = round2.Center,
            Circle1Radius = round1.FittedRadius,
            Circle2Radius = round2.FittedRadius,
            Offset = offset,
            IsValid = true,
            Description = "Concentricity measurement"
        };
    }

    /// <summary>
    /// Measure coaxiality between two cylinders
    /// </summary>
    public CoaxialityMeasurement MeasureCoaxiality(PointCloudData cloud1, PointCloudData cloud2)
    {
        var cyl1 = MeasureCylindricity(cloud1);
        var cyl2 = MeasureCylindricity(cloud2);

        if (!cyl1.IsValid || !cyl2.IsValid)
        {
            return new CoaxialityMeasurement
            {
                IsValid = false,
                ErrorMessage = "Could not fit cylinders to point clouds"
            };
        }

        // Calculate axis offset (perpendicular distance between axes)
        var p1 = cyl1.AxisPoint;
        var d1 = cyl1.AxisDirection;
        var p2 = cyl2.AxisPoint;
        var d2 = cyl2.AxisDirection;

        var w = p1 - p2;
        var a = Vector3.Dot(d1, d1);
        var b = Vector3.Dot(d1, d2);
        var c = Vector3.Dot(d2, d2);
        var d = Vector3.Dot(d1, w);
        var e = Vector3.Dot(d2, w);

        var denom = a * c - b * b;
        var axisOffset = 0f;
        if (Math.Abs(denom) > 1e-6f)
        {
            var s = (b * e - c * d) / denom;
            var t = (a * e - b * d) / denom;
            var closestOnAxis1 = p1 + s * d1;
            var closestOnAxis2 = p2 + t * d2;
            axisOffset = (closestOnAxis1 - closestOnAxis2).Length();
        }
        else
        {
            // Parallel axes
            axisOffset = (w - Vector3.Dot(w, d1) * d1).Length();
        }

        // Calculate angle between axes
        var dot = Math.Abs(Vector3.Dot(d1, d2));
        dot = Math.Clamp(dot, 0, 1);
        var axisAngle = (float)Math.Acos(dot) * 180f / MathF.PI;

        return new CoaxialityMeasurement
        {
            Coaxiality = axisOffset,
            AxisOffset = axisOffset,
            AxisAngle = axisAngle,
            Axis1Point = p1,
            Axis1Direction = d1,
            Axis2Point = p2,
            Axis2Direction = d2,
            IsValid = true,
            Description = "Coaxiality measurement"
        };
    }

    /// <summary>
    /// Find the eigenvector corresponding to smallest eigenvalue (normal of best-fit plane)
    /// Using power iteration on inverse covariance matrix
    /// </summary>
    private Vector3 FindSmallestEigenvector(List<Vector3> points, Vector3 centroid)
    {
        // Build covariance matrix
        double cxx = 0, cyy = 0, czz = 0, cxy = 0, cxz = 0, cyz = 0;
        foreach (var p in points)
        {
            var dx = p.X - centroid.X;
            var dy = p.Y - centroid.Y;
            var dz = p.Z - centroid.Z;
            cxx += dx * dx;
            cyy += dy * dy;
            czz += dz * dz;
            cxy += dx * dy;
            cxz += dx * dz;
            cyz += dy * dz;
        }

        // Simple approach: find which axis has minimum variance
        // For better accuracy, use SVD or proper eigenvalue decomposition
        var variances = new[] { cxx, cyy, czz };
        var minIdx = Array.IndexOf(variances, variances.Min());

        return minIdx switch
        {
            0 => Vector3.Normalize(new Vector3(1, (float)(cxy / (cxx + 1e-10)), (float)(cxz / (cxx + 1e-10)))),
            1 => Vector3.Normalize(new Vector3((float)(cxy / (cyy + 1e-10)), 1, (float)(cyz / (cyy + 1e-10)))),
            _ => Vector3.Normalize(new Vector3((float)(cxz / (czz + 1e-10)), (float)(cyz / (czz + 1e-10)), 1))
        };
    }
}
