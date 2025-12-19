using System;
using System.Collections.Generic;
using System.Numerics;

namespace VPP.Plugins.PointCloud.Models;

/// <summary>
/// Represents different measurement tool types
/// </summary>
public enum MeasurementToolType
{
    None,
    // Basic Measurements
    Distance,
    Angle,
    Height,
    BoundingBox,
    Centroid,
    PointDensity,
    SurfaceArea,
    PointToPlane,
    // GD&T Measurements
    Flatness,
    Roundness,
    Cylindricity,
    Parallelism,
    Perpendicularity,
    Concentricity,
    Coaxiality
}

/// <summary>
/// Category for grouping measurement tools
/// </summary>
public enum MeasurementCategory
{
    Basic,
    GDT  // Geometric Dimensioning & Tolerancing
}

/// <summary>
/// Base class for all measurement results
/// </summary>
public abstract class MeasurementResult
{
    public MeasurementToolType ToolType { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool IsValid { get; set; }
    public string ErrorMessage { get; set; } = "";

    public abstract string GetFormattedResult();
}

/// <summary>
/// Distance measurement result between two points
/// </summary>
public class DistanceMeasurement : MeasurementResult
{
    public Vector3 Point1 { get; set; }
    public Vector3 Point2 { get; set; }
    public float TotalDistance { get; set; }
    public float DistanceX { get; set; }
    public float DistanceY { get; set; }
    public float DistanceZ { get; set; }

    public DistanceMeasurement()
    {
        ToolType = MeasurementToolType.Distance;
        Name = "Distance Measurement";
    }

    public override string GetFormattedResult()
    {
        return $"Total: {TotalDistance:F3}mm\nX: {DistanceX:F3}mm\nY: {DistanceY:F3}mm\nZ: {DistanceZ:F3}mm";
    }
}

/// <summary>
/// Angle measurement result from three points
/// </summary>
public class AngleMeasurement : MeasurementResult
{
    public Vector3 Point1 { get; set; }
    public Vector3 Vertex { get; set; }  // Middle point (vertex of angle)
    public Vector3 Point3 { get; set; }
    public float AngleDegrees { get; set; }
    public float AngleRadians { get; set; }

    public AngleMeasurement()
    {
        ToolType = MeasurementToolType.Angle;
        Name = "Angle Measurement";
    }

    public override string GetFormattedResult()
    {
        return $"Angle: {AngleDegrees:F2}°\n({AngleRadians:F4} rad)";
    }
}

/// <summary>
/// Height measurement along an axis
/// </summary>
public class HeightMeasurement : MeasurementResult
{
    public string Axis { get; set; } = "Z";
    public float MinHeight { get; set; }
    public float MaxHeight { get; set; }
    public float Range { get; set; }
    public float Mean { get; set; }
    public float StandardDeviation { get; set; }
    public int PointCount { get; set; }

    public HeightMeasurement()
    {
        ToolType = MeasurementToolType.Height;
        Name = "Height Measurement";
    }

    public override string GetFormattedResult()
    {
        return $"Axis: {Axis}\nMin: {MinHeight:F3}mm\nMax: {MaxHeight:F3}mm\nRange: {Range:F3}mm\nMean: {Mean:F3}mm\nStdDev: {StandardDeviation:F3}mm\nPoints: {PointCount:N0}";
    }
}

/// <summary>
/// Bounding box measurement
/// </summary>
public class BoundingBoxMeasurement : MeasurementResult
{
    public Vector3 Min { get; set; }
    public Vector3 Max { get; set; }
    public Vector3 Size { get; set; }
    public Vector3 Center { get; set; }
    public float Volume { get; set; }
    public float DiagonalLength { get; set; }

    public BoundingBoxMeasurement()
    {
        ToolType = MeasurementToolType.BoundingBox;
        Name = "Bounding Box";
    }

    public override string GetFormattedResult()
    {
        return $"Size: {Size.X:F3} x {Size.Y:F3} x {Size.Z:F3} mm\nVolume: {Volume:F3} mm³\nDiagonal: {DiagonalLength:F3}mm\nCenter: ({Center.X:F2}, {Center.Y:F2}, {Center.Z:F2})";
    }
}

/// <summary>
/// Centroid (center of mass) measurement
/// </summary>
public class CentroidMeasurement : MeasurementResult
{
    public Vector3 Centroid { get; set; }
    public int PointCount { get; set; }

    public CentroidMeasurement()
    {
        ToolType = MeasurementToolType.Centroid;
        Name = "Centroid";
    }

    public override string GetFormattedResult()
    {
        return $"Centroid: ({Centroid.X:F3}, {Centroid.Y:F3}, {Centroid.Z:F3})\nPoint Count: {PointCount:N0}";
    }
}

/// <summary>
/// Point density measurement
/// </summary>
public class PointDensityMeasurement : MeasurementResult
{
    public float Density { get; set; }  // points per mm³
    public float AverageSpacing { get; set; }  // average distance between points
    public int PointCount { get; set; }
    public float BoundingVolume { get; set; }

    public PointDensityMeasurement()
    {
        ToolType = MeasurementToolType.PointDensity;
        Name = "Point Density";
    }

    public override string GetFormattedResult()
    {
        return $"Density: {Density:F4} pts/mm³\nAvg Spacing: {AverageSpacing:F3}mm\nPoints: {PointCount:N0}\nVolume: {BoundingVolume:F3} mm³";
    }
}

/// <summary>
/// Surface area estimation
/// </summary>
public class SurfaceAreaMeasurement : MeasurementResult
{
    public float SurfaceArea { get; set; }  // mm²
    public int TriangleCount { get; set; }
    public float AverageTriangleArea { get; set; }

    public SurfaceAreaMeasurement()
    {
        ToolType = MeasurementToolType.SurfaceArea;
        Name = "Surface Area";
    }

    public override string GetFormattedResult()
    {
        return $"Surface Area: {SurfaceArea:F3} mm²\nTriangles: {TriangleCount:N0}\nAvg Triangle: {AverageTriangleArea:F3} mm²";
    }
}

/// <summary>
/// Point to plane distance measurement
/// </summary>
public class PointToPlaneMeasurement : MeasurementResult
{
    public Vector3 Point { get; set; }
    public Vector3 PlaneNormal { get; set; }
    public float PlaneD { get; set; }
    public float Distance { get; set; }
    public Vector3 ClosestPointOnPlane { get; set; }

    public PointToPlaneMeasurement()
    {
        ToolType = MeasurementToolType.PointToPlane;
        Name = "Point to Plane Distance";
    }

    public override string GetFormattedResult()
    {
        return $"Distance: {Distance:F3}mm\nPoint: ({Point.X:F2}, {Point.Y:F2}, {Point.Z:F2})\nPlane Normal: ({PlaneNormal.X:F2}, {PlaneNormal.Y:F2}, {PlaneNormal.Z:F2})";
    }
}

// GD&T Measurements

/// <summary>
/// Flatness measurement (plane deviation)
/// </summary>
public class FlatnessMeasurement : MeasurementResult
{
    public float Flatness { get; set; }  // Total deviation from ideal plane
    public float MaxPositiveDeviation { get; set; }
    public float MaxNegativeDeviation { get; set; }
    public Vector3 PlaneNormal { get; set; }
    public float PlaneD { get; set; }
    public int PointCount { get; set; }

    public FlatnessMeasurement()
    {
        ToolType = MeasurementToolType.Flatness;
        Name = "Flatness (GD&T)";
    }

    public override string GetFormattedResult()
    {
        return $"Flatness: {Flatness:F4}mm\nMax +Dev: {MaxPositiveDeviation:F4}mm\nMax -Dev: {MaxNegativeDeviation:F4}mm\nPoints: {PointCount:N0}";
    }
}

/// <summary>
/// Roundness measurement (circularity)
/// </summary>
public class RoundnessMeasurement : MeasurementResult
{
    public float Roundness { get; set; }  // Deviation from perfect circle
    public float FittedRadius { get; set; }
    public Vector3 Center { get; set; }
    public float MaxRadiusDeviation { get; set; }
    public float MinRadiusDeviation { get; set; }
    public int PointCount { get; set; }

    public RoundnessMeasurement()
    {
        ToolType = MeasurementToolType.Roundness;
        Name = "Roundness (GD&T)";
    }

    public override string GetFormattedResult()
    {
        return $"Roundness: {Roundness:F4}mm\nRadius: {FittedRadius:F3}mm\nMax Dev: {MaxRadiusDeviation:F4}mm\nMin Dev: {MinRadiusDeviation:F4}mm\nCenter: ({Center.X:F2}, {Center.Y:F2}, {Center.Z:F2})";
    }
}

/// <summary>
/// Cylindricity measurement
/// </summary>
public class CylindricityMeasurement : MeasurementResult
{
    public float Cylindricity { get; set; }  // Total deviation from ideal cylinder
    public float FittedRadius { get; set; }
    public Vector3 AxisPoint { get; set; }
    public Vector3 AxisDirection { get; set; }
    public float MaxRadiusDeviation { get; set; }
    public float MinRadiusDeviation { get; set; }
    public int PointCount { get; set; }

    public CylindricityMeasurement()
    {
        ToolType = MeasurementToolType.Cylindricity;
        Name = "Cylindricity (GD&T)";
    }

    public override string GetFormattedResult()
    {
        return $"Cylindricity: {Cylindricity:F4}mm\nRadius: {FittedRadius:F3}mm\nMax Dev: {MaxRadiusDeviation:F4}mm\nMin Dev: {MinRadiusDeviation:F4}mm\nPoints: {PointCount:N0}";
    }
}

/// <summary>
/// Parallelism measurement between two planes
/// </summary>
public class ParallelismMeasurement : MeasurementResult
{
    public float AngleBetweenPlanes { get; set; }  // Degrees
    public float Parallelism { get; set; }  // Distance variation
    public Vector3 Plane1Normal { get; set; }
    public Vector3 Plane2Normal { get; set; }

    public ParallelismMeasurement()
    {
        ToolType = MeasurementToolType.Parallelism;
        Name = "Parallelism (GD&T)";
    }

    public override string GetFormattedResult()
    {
        return $"Parallelism: {Parallelism:F4}mm\nAngle: {AngleBetweenPlanes:F4}°";
    }
}

/// <summary>
/// Perpendicularity measurement (90° deviation)
/// </summary>
public class PerpendicularityMeasurement : MeasurementResult
{
    public float Perpendicularity { get; set; }  // Deviation from 90°
    public float ActualAngle { get; set; }
    public float AngleDeviation { get; set; }
    public Vector3 Surface1Normal { get; set; }
    public Vector3 Surface2Normal { get; set; }

    public PerpendicularityMeasurement()
    {
        ToolType = MeasurementToolType.Perpendicularity;
        Name = "Perpendicularity (GD&T)";
    }

    public override string GetFormattedResult()
    {
        return $"Perpendicularity: {Perpendicularity:F4}mm\nActual Angle: {ActualAngle:F4}°\nDeviation: {AngleDeviation:F4}°";
    }
}

/// <summary>
/// Concentricity measurement (circle center offset)
/// </summary>
public class ConcentricityMeasurement : MeasurementResult
{
    public float Concentricity { get; set; }  // Center offset distance
    public Vector3 Circle1Center { get; set; }
    public Vector3 Circle2Center { get; set; }
    public float Circle1Radius { get; set; }
    public float Circle2Radius { get; set; }
    public Vector3 Offset { get; set; }

    public ConcentricityMeasurement()
    {
        ToolType = MeasurementToolType.Concentricity;
        Name = "Concentricity (GD&T)";
    }

    public override string GetFormattedResult()
    {
        return $"Concentricity: {Concentricity:F4}mm\nOffset: ({Offset.X:F3}, {Offset.Y:F3}, {Offset.Z:F3})\nCircle 1 R: {Circle1Radius:F3}mm\nCircle 2 R: {Circle2Radius:F3}mm";
    }
}

/// <summary>
/// Coaxiality measurement (axis offset and angle)
/// </summary>
public class CoaxialityMeasurement : MeasurementResult
{
    public float Coaxiality { get; set; }  // Total deviation
    public float AxisOffset { get; set; }  // Distance between axes
    public float AxisAngle { get; set; }  // Angle between axes in degrees
    public Vector3 Axis1Point { get; set; }
    public Vector3 Axis1Direction { get; set; }
    public Vector3 Axis2Point { get; set; }
    public Vector3 Axis2Direction { get; set; }

    public CoaxialityMeasurement()
    {
        ToolType = MeasurementToolType.Coaxiality;
        Name = "Coaxiality (GD&T)";
    }

    public override string GetFormattedResult()
    {
        return $"Coaxiality: {Coaxiality:F4}mm\nAxis Offset: {AxisOffset:F4}mm\nAxis Angle: {AxisAngle:F4}°";
    }
}

/// <summary>
/// Descriptor for a measurement tool (for UI display)
/// </summary>
public class MeasurementToolDescriptor
{
    public MeasurementToolType Type { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "";
    public MeasurementCategory Category { get; set; }
    public int RequiredPoints { get; set; }  // 0 = uses point cloud, 1+ = requires specific point picks
    public bool RequiresPlane { get; set; }
    public bool RequiresTwoSelections { get; set; }  // For tools comparing two surfaces/circles

    public static List<MeasurementToolDescriptor> GetAllTools()
    {
        return new List<MeasurementToolDescriptor>
        {
            // Basic Measurements
            new MeasurementToolDescriptor
            {
                Type = MeasurementToolType.Distance,
                Name = "Distance",
                Description = "Measure distance between two points (X, Y, Z)",
                Icon = "📏",
                Category = MeasurementCategory.Basic,
                RequiredPoints = 2
            },
            new MeasurementToolDescriptor
            {
                Type = MeasurementToolType.Angle,
                Name = "Angle",
                Description = "Measure angle defined by three points",
                Icon = "📐",
                Category = MeasurementCategory.Basic,
                RequiredPoints = 3
            },
            new MeasurementToolDescriptor
            {
                Type = MeasurementToolType.Height,
                Name = "Height",
                Description = "Height statistics along axis (X/Y/Z)",
                Icon = "📊",
                Category = MeasurementCategory.Basic,
                RequiredPoints = 0
            },
            new MeasurementToolDescriptor
            {
                Type = MeasurementToolType.BoundingBox,
                Name = "Bounding Box",
                Description = "Size, volume, diagonal of bounding box",
                Icon = "📦",
                Category = MeasurementCategory.Basic,
                RequiredPoints = 0
            },
            new MeasurementToolDescriptor
            {
                Type = MeasurementToolType.Centroid,
                Name = "Centroid",
                Description = "Center of mass calculation",
                Icon = "⊙",
                Category = MeasurementCategory.Basic,
                RequiredPoints = 0
            },
            new MeasurementToolDescriptor
            {
                Type = MeasurementToolType.PointDensity,
                Name = "Point Density",
                Description = "Point density and average spacing",
                Icon = "⁘",
                Category = MeasurementCategory.Basic,
                RequiredPoints = 0
            },
            new MeasurementToolDescriptor
            {
                Type = MeasurementToolType.SurfaceArea,
                Name = "Surface Area",
                Description = "Surface area estimation",
                Icon = "▢",
                Category = MeasurementCategory.Basic,
                RequiredPoints = 0
            },
            new MeasurementToolDescriptor
            {
                Type = MeasurementToolType.PointToPlane,
                Name = "Point to Plane",
                Description = "Distance from point to plane",
                Icon = "⊥",
                Category = MeasurementCategory.Basic,
                RequiredPoints = 1,
                RequiresPlane = true
            },
            // GD&T Measurements
            new MeasurementToolDescriptor
            {
                Type = MeasurementToolType.Flatness,
                Name = "Flatness",
                Description = "Plane deviation measurement",
                Icon = "▬",
                Category = MeasurementCategory.GDT,
                RequiredPoints = 0
            },
            new MeasurementToolDescriptor
            {
                Type = MeasurementToolType.Roundness,
                Name = "Roundness",
                Description = "Circularity (radius deviation)",
                Icon = "○",
                Category = MeasurementCategory.GDT,
                RequiredPoints = 0
            },
            new MeasurementToolDescriptor
            {
                Type = MeasurementToolType.Cylindricity,
                Name = "Cylindricity",
                Description = "Cylinder form deviation",
                Icon = "⌭",
                Category = MeasurementCategory.GDT,
                RequiredPoints = 0
            },
            new MeasurementToolDescriptor
            {
                Type = MeasurementToolType.Parallelism,
                Name = "Parallelism",
                Description = "Angle between two planes",
                Icon = "∥",
                Category = MeasurementCategory.GDT,
                RequiredPoints = 0,
                RequiresTwoSelections = true
            },
            new MeasurementToolDescriptor
            {
                Type = MeasurementToolType.Perpendicularity,
                Name = "Perpendicularity",
                Description = "90 degree deviation",
                Icon = "⟂",
                Category = MeasurementCategory.GDT,
                RequiredPoints = 0,
                RequiresTwoSelections = true
            },
            new MeasurementToolDescriptor
            {
                Type = MeasurementToolType.Concentricity,
                Name = "Concentricity",
                Description = "Circle center offset",
                Icon = "◎",
                Category = MeasurementCategory.GDT,
                RequiredPoints = 0,
                RequiresTwoSelections = true
            },
            new MeasurementToolDescriptor
            {
                Type = MeasurementToolType.Coaxiality,
                Name = "Coaxiality",
                Description = "Axis offset and angle",
                Icon = "⌀",
                Category = MeasurementCategory.GDT,
                RequiredPoints = 0,
                RequiresTwoSelections = true
            }
        };
    }
}
