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
/// Measurement units for display
/// </summary>
public enum MeasurementUnit
{
    Millimeter,  // mm (default, 1:1 with internal values)
    Centimeter,  // cm (1cm = 10mm)
    Meter        // m (1m = 1000mm)
}

/// <summary>
/// Helper class for unit conversion
/// </summary>
public static class MeasurementUnitHelper
{
    public static float GetMultiplier(MeasurementUnit unit) => unit switch
    {
        MeasurementUnit.Millimeter => 1f,
        MeasurementUnit.Centimeter => 0.1f,
        MeasurementUnit.Meter => 0.001f,
        _ => 1f
    };

    public static string GetSuffix(MeasurementUnit unit) => unit switch
    {
        MeasurementUnit.Millimeter => "mm",
        MeasurementUnit.Centimeter => "cm",
        MeasurementUnit.Meter => "m",
        _ => "mm"
    };

    public static string GetAreaSuffix(MeasurementUnit unit) => unit switch
    {
        MeasurementUnit.Millimeter => "mm²",
        MeasurementUnit.Centimeter => "cm²",
        MeasurementUnit.Meter => "m²",
        _ => "mm²"
    };

    public static string GetVolumeSuffix(MeasurementUnit unit) => unit switch
    {
        MeasurementUnit.Millimeter => "mm³",
        MeasurementUnit.Centimeter => "cm³",
        MeasurementUnit.Meter => "m³",
        _ => "mm³"
    };

    public static float GetAreaMultiplier(MeasurementUnit unit) => unit switch
    {
        MeasurementUnit.Millimeter => 1f,
        MeasurementUnit.Centimeter => 0.01f,      // 1cm² = 100mm²
        MeasurementUnit.Meter => 0.000001f,       // 1m² = 1000000mm²
        _ => 1f
    };

    public static float GetVolumeMultiplier(MeasurementUnit unit) => unit switch
    {
        MeasurementUnit.Millimeter => 1f,
        MeasurementUnit.Centimeter => 0.001f,     // 1cm³ = 1000mm³
        MeasurementUnit.Meter => 0.000000001f,    // 1m³ = 10^9 mm³
        _ => 1f
    };
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

    /// <summary>
    /// Get formatted result with default mm unit (scale = 1)
    /// </summary>
    public abstract string GetFormattedResult();

    /// <summary>
    /// Get formatted result with specified unit enum
    /// </summary>
    public virtual string GetFormattedResult(MeasurementUnit unit)
    {
        return GetFormattedResult(MeasurementUnitHelper.GetMultiplier(unit));
    }

    /// <summary>
    /// Get formatted result with custom scale (e.g., 0.001, 1, 10)
    /// Scale represents: 1mm = scale units
    /// </summary>
    public virtual string GetFormattedResult(float scale)
    {
        return GetFormattedResult(); // Default implementation
    }

    /// <summary>
    /// Restore visualization for this measurement result
    /// </summary>
    public virtual void RestoreVisualization(Action<MeasurementResult> visualizer)
    {
        visualizer?.Invoke(this);
    }
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
        return GetFormattedResult(1f);
    }

    public override string GetFormattedResult(float scale)
    {
        return $"Total: {TotalDistance * scale:F3}mm\nX: {DistanceX * scale:F3}mm\nY: {DistanceY * scale:F3}mm\nZ: {DistanceZ * scale:F3}mm";
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
        return GetFormattedResult(1f);
    }

    public override string GetFormattedResult(float scale)
    {
        return $"Axis: {Axis}\nMin: {MinHeight * scale:F3}mm\nMax: {MaxHeight * scale:F3}mm\nRange: {Range * scale:F3}mm\nMean: {Mean * scale:F3}mm\nStdDev: {StandardDeviation * scale:F3}mm\nPoints: {PointCount:N0}";
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
        return GetFormattedResult(1f);
    }

    public override string GetFormattedResult(float scale)
    {
        var s3 = scale * scale * scale; // volume scale
        return $"Size: {Size.X * scale:F3} x {Size.Y * scale:F3} x {Size.Z * scale:F3} mm\nVolume: {Volume * s3:F3} mm³\nDiagonal: {DiagonalLength * scale:F3}mm\nCenter: ({Center.X * scale:F2}, {Center.Y * scale:F2}, {Center.Z * scale:F2})";
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
        return GetFormattedResult(1f);
    }

    public override string GetFormattedResult(float scale)
    {
        return $"Centroid: ({Centroid.X * scale:F3}, {Centroid.Y * scale:F3}, {Centroid.Z * scale:F3}) mm\nPoint Count: {PointCount:N0}";
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
        return GetFormattedResult(1f);
    }

    public override string GetFormattedResult(float scale)
    {
        var s3 = scale * scale * scale; // volume scale
        return $"Density: {Density / s3:F4} pts/mm³\nAvg Spacing: {AverageSpacing * scale:F3}mm\nPoints: {PointCount:N0}\nVolume: {BoundingVolume * s3:F3} mm³";
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
        return GetFormattedResult(1f);
    }

    public override string GetFormattedResult(float scale)
    {
        var s2 = scale * scale; // area scale
        return $"Surface Area: {SurfaceArea * s2:F3} mm²\nTriangles: {TriangleCount:N0}\nAvg Triangle: {AverageTriangleArea * s2:F3} mm²";
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
        return GetFormattedResult(1f);
    }

    public override string GetFormattedResult(float scale)
    {
        return $"Distance: {Distance * scale:F3}mm\nPoint: ({Point.X * scale:F2}, {Point.Y * scale:F2}, {Point.Z * scale:F2})\nPlane Normal: ({PlaneNormal.X:F2}, {PlaneNormal.Y:F2}, {PlaneNormal.Z:F2})";
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
        return GetFormattedResult(1f);
    }

    public override string GetFormattedResult(float scale)
    {
        return $"Flatness: {Flatness * scale:F4}mm\nMax +Dev: {MaxPositiveDeviation * scale:F4}mm\nMax -Dev: {MaxNegativeDeviation * scale:F4}mm\nPoints: {PointCount:N0}";
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
        return GetFormattedResult(1f);
    }

    public override string GetFormattedResult(float scale)
    {
        return $"Roundness: {Roundness * scale:F4}mm\nRadius: {FittedRadius * scale:F3}mm\nMax Dev: {MaxRadiusDeviation * scale:F4}mm\nMin Dev: {MinRadiusDeviation * scale:F4}mm\nCenter: ({Center.X * scale:F2}, {Center.Y * scale:F2}, {Center.Z * scale:F2})";
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
        return GetFormattedResult(1f);
    }

    public override string GetFormattedResult(float scale)
    {
        return $"Cylindricity: {Cylindricity * scale:F4}mm\nRadius: {FittedRadius * scale:F3}mm\nMax Dev: {MaxRadiusDeviation * scale:F4}mm\nMin Dev: {MinRadiusDeviation * scale:F4}mm\nPoints: {PointCount:N0}";
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
        return GetFormattedResult(1f);
    }

    public override string GetFormattedResult(float scale)
    {
        return $"Parallelism: {Parallelism * scale:F4}mm\nAngle: {AngleBetweenPlanes:F4}°";
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
        return GetFormattedResult(1f);
    }

    public override string GetFormattedResult(float scale)
    {
        return $"Perpendicularity: {Perpendicularity * scale:F4}mm\nActual Angle: {ActualAngle:F4}°\nDeviation: {AngleDeviation:F4}°";
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
        return GetFormattedResult(1f);
    }

    public override string GetFormattedResult(float scale)
    {
        return $"Concentricity: {Concentricity * scale:F4}mm\nOffset: ({Offset.X * scale:F3}, {Offset.Y * scale:F3}, {Offset.Z * scale:F3})\nCircle 1 R: {Circle1Radius * scale:F3}mm\nCircle 2 R: {Circle2Radius * scale:F3}mm";
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
        return GetFormattedResult(1f);
    }

    public override string GetFormattedResult(float scale)
    {
        return $"Coaxiality: {Coaxiality * scale:F4}mm\nAxis Offset: {AxisOffset * scale:F4}mm\nAxis Angle: {AxisAngle:F4}°";
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
