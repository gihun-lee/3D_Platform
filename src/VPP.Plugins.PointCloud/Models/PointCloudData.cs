using System.Numerics;

namespace VPP.Plugins.PointCloud.Models;

public class PointCloudData
{
    public List<Vector3> Points { get; set; } = new();
    public List<Vector3>? Colors { get; set; }
    public List<Vector3>? Normals { get; set; }
    public float[] BoundingBox { get; set; } = new float[6]; // MinX, MinY, MinZ, MaxX, MaxY, MaxZ

    public int Count => Points.Count;

    public void ComputeBoundingBox()
    {
        if (Points.Count == 0) return;

        BoundingBox[0] = BoundingBox[3] = Points[0].X;
        BoundingBox[1] = BoundingBox[4] = Points[0].Y;
        BoundingBox[2] = BoundingBox[5] = Points[0].Z;

        foreach (var p in Points)
        {
            BoundingBox[0] = Math.Min(BoundingBox[0], p.X);
            BoundingBox[1] = Math.Min(BoundingBox[1], p.Y);
            BoundingBox[2] = Math.Min(BoundingBox[2], p.Z);
            BoundingBox[3] = Math.Max(BoundingBox[3], p.X);
            BoundingBox[4] = Math.Max(BoundingBox[4], p.Y);
            BoundingBox[5] = Math.Max(BoundingBox[5], p.Z);
        }
    }

    public PointCloudData Clone() => new()
    {
        Points = new List<Vector3>(Points),
        Colors = Colors != null ? new List<Vector3>(Colors) : null,
        Normals = Normals != null ? new List<Vector3>(Normals) : null,
        BoundingBox = (float[])BoundingBox.Clone()
    };
}

public class ROI3D
{
    public Vector3 Center { get; set; }
    public Vector3 Size { get; set; }
    public ROIShape Shape { get; set; } = ROIShape.Box;
    public float Radius { get; set; } // For cylinder/sphere
}

public enum ROIShape
{
    Box,
    Cylinder,
    Sphere
}

public class CircleDetectionResult
{
    public Vector3 Center { get; set; }
    public float Radius { get; set; }
    public Vector3 Normal { get; set; }
    public float FitError { get; set; }
    public int InlierCount { get; set; }
    public List<Vector3> InlierPoints { get; set; } = new();
}

public class InspectionResult
{
    public bool Pass { get; set; }
    public string Message { get; set; } = "";
    public Dictionary<string, double> Measurements { get; set; } = new();
    public List<string> Failures { get; set; } = new();
}
