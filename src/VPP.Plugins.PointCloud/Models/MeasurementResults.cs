using System.Numerics;

namespace VPP.Plugins.PointCloud.Models;

/// <summary>
/// Result of distance measurement between two points
/// </summary>
public class DistanceMeasurementResult
{
    public Vector3 Point1 { get; set; }
    public Vector3 Point2 { get; set; }
    public float Distance { get; set; }
    public float DistanceX { get; set; }
    public float DistanceY { get; set; }
    public float DistanceZ { get; set; }
}

/// <summary>
/// Result of angle measurement between three points
/// </summary>
public class AngleMeasurementResult
{
    public Vector3 Point1 { get; set; }
    public Vector3 Vertex { get; set; }
    public Vector3 Point3 { get; set; }
    public float AngleRadians { get; set; }
    public float AngleDegrees { get; set; }
}

/// <summary>
/// Result of plane fitting using RANSAC
/// </summary>
public class PlaneFittingResult
{
    public Vector3 Normal { get; set; }
    public Vector3 Point { get; set; }
    public float D { get; set; } // ax + by + cz + d = 0
    public float FitError { get; set; }
    public int InlierCount { get; set; }
    public List<Vector3> InlierPoints { get; set; } = new();

    /// <summary>
    /// Get distance from a point to the plane
    /// </summary>
    public float DistanceToPoint(Vector3 p)
    {
        return Math.Abs(Vector3.Dot(Normal, p) + D);
    }
}

/// <summary>
/// Result of sphere fitting using RANSAC
/// </summary>
public class SphereFittingResult
{
    public Vector3 Center { get; set; }
    public float Radius { get; set; }
    public float FitError { get; set; }
    public int InlierCount { get; set; }
    public List<Vector3> InlierPoints { get; set; } = new();
}

/// <summary>
/// Result of cylinder fitting using RANSAC
/// </summary>
public class CylinderFittingResult
{
    public Vector3 AxisPoint { get; set; }
    public Vector3 AxisDirection { get; set; }
    public float Radius { get; set; }
    public float Height { get; set; }
    public float FitError { get; set; }
    public int InlierCount { get; set; }
    public List<Vector3> InlierPoints { get; set; } = new();
}

/// <summary>
/// Result of line fitting using RANSAC
/// </summary>
public class LineFittingResult
{
    public Vector3 Point { get; set; }
    public Vector3 Direction { get; set; }
    public float Length { get; set; }
    public float FitError { get; set; }
    public int InlierCount { get; set; }
    public List<Vector3> InlierPoints { get; set; } = new();

    public Vector3 StartPoint { get; set; }
    public Vector3 EndPoint { get; set; }
}

/// <summary>
/// Result of bounding box measurement
/// </summary>
public class BoundingBoxResult
{
    public Vector3 Min { get; set; }
    public Vector3 Max { get; set; }
    public Vector3 Center { get; set; }
    public Vector3 Size { get; set; }
    public float Volume { get; set; }
    public float DiagonalLength { get; set; }
}

/// <summary>
/// Result of centroid measurement
/// </summary>
public class CentroidResult
{
    public Vector3 Centroid { get; set; }
    public int PointCount { get; set; }
}

/// <summary>
/// Result of height measurement
/// </summary>
public class HeightMeasurementResult
{
    public float MinZ { get; set; }
    public float MaxZ { get; set; }
    public float Height { get; set; }
    public float AverageZ { get; set; }
    public Vector3 LowestPoint { get; set; }
    public Vector3 HighestPoint { get; set; }
}

/// <summary>
/// Result of point density calculation
/// </summary>
public class PointDensityResult
{
    public float Density { get; set; } // points per unit volume
    public float AverageSpacing { get; set; } // average distance between points
    public int TotalPoints { get; set; }
    public float Volume { get; set; }
}

/// <summary>
/// Result of edge detection
/// </summary>
public class EdgeDetectionResult
{
    public List<Vector3> EdgePoints { get; set; } = new();
    public int EdgePointCount { get; set; }
    public float TotalEdgeLength { get; set; }
}

/// <summary>
/// Result of surface area estimation
/// </summary>
public class SurfaceAreaResult
{
    public float EstimatedArea { get; set; }
    public int TriangleCount { get; set; }
    public float AverageTriangleArea { get; set; }
}

/// <summary>
/// Result of flatness measurement
/// </summary>
public class FlatnessMeasurementResult
{
    public float MaxDeviation { get; set; }
    public float AverageDeviation { get; set; }
    public float RMSDeviation { get; set; }
    public PlaneFittingResult ReferencePlane { get; set; } = new();
}

/// <summary>
/// Result of parallelism measurement between two planes
/// </summary>
public class ParallelismResult
{
    public float AngleDeviation { get; set; } // in degrees
    public float MaxDistanceDeviation { get; set; }
    public PlaneFittingResult Plane1 { get; set; } = new();
    public PlaneFittingResult Plane2 { get; set; } = new();
}

/// <summary>
/// Result of perpendicularity measurement
/// </summary>
public class PerpendicularityResult
{
    public float AngleDeviation { get; set; } // deviation from 90 degrees
    public float MeasuredAngle { get; set; }
    public PlaneFittingResult Plane1 { get; set; } = new();
    public PlaneFittingResult Plane2 { get; set; } = new();
}

/// <summary>
/// Result of concentricity measurement between two circles
/// </summary>
public class ConcentricityResult
{
    public float CenterOffset { get; set; }
    public Vector3 Center1 { get; set; }
    public Vector3 Center2 { get; set; }
    public float Radius1 { get; set; }
    public float Radius2 { get; set; }
}

/// <summary>
/// Result of coaxiality measurement between two cylinders
/// </summary>
public class CoaxialityResult
{
    public float AxisOffset { get; set; }
    public float AngleDeviation { get; set; }
    public CylinderFittingResult Cylinder1 { get; set; } = new();
    public CylinderFittingResult Cylinder2 { get; set; } = new();
}
