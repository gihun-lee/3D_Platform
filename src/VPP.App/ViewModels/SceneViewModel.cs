using CommunityToolkit.Mvvm.ComponentModel;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System.Collections.ObjectModel;
using System.Windows.Media.Media3D;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using VPP.App.Rendering;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;

namespace VPP.App.ViewModels;

public partial class SceneViewModel : ObservableObject
{
    // GPU-accelerated SharpDX properties
    [ObservableProperty] private PointGeometry3D? _pointCloudGeometry;
    [ObservableProperty] private Color _pointCloudColor = Colors.LightGray;
    [ObservableProperty] private double _pointSize = 2.0;
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.Camera _camera;
    [ObservableProperty] private IEffectsManager _effectsManager;

    // Origin Axes
    [ObservableProperty] private LineGeometry3D? _originAxesGeometry; // legacy
    [ObservableProperty] private LineGeometry3D? _originXGeometry;
    [ObservableProperty] private LineGeometry3D? _originYGeometry;
    [ObservableProperty] private LineGeometry3D? _originZGeometry;
    [ObservableProperty] private Color _originXColor = Colors.Red;
    [ObservableProperty] private Color _originYColor = Colors.Green;
    [ObservableProperty] private Color _originZColor = Colors.Blue;

    // ROI Visualization
    [ObservableProperty] private ObservableCollection<RoiVisualization> _roiVisualizations = new();
    [ObservableProperty] private ObservableElement3DCollection _roiModels = new();
    
    // Legacy ROI properties (for compatibility if needed, or we can try to remove them if XAML is updated)
    [ObservableProperty] private LineGeometry3D? _roiWireframeGeometry;
    [ObservableProperty] private Color _roiWireframeColor = Colors.Yellow;
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.MeshGeometry3D? _roiCenterPointGeometry;
    [ObservableProperty] private Color _roiCenterPointColor = Colors.Red;

    // Detected Circle Visualization
    [ObservableProperty] private PointGeometry3D? _detectedCircleGeometry;
    [ObservableProperty] private Color _detectedCircleColor = Colors.Yellow;
    [ObservableProperty] private double _detectedCirclePointSize = 4.0;
    [ObservableProperty] private LineGeometry3D? _detectedCircleOutlineGeometry;
    [ObservableProperty] private Color _detectedCircleOutlineColor = Colors.Red;
    
    // Multiple Detected Circles
    [ObservableProperty] private ObservableElement3DCollection _detectedCircleModels = new();

    // Constants
    private const float RoiCenterSphereRadius = 0.1f;

    public SceneViewModel()
    {
        _effectsManager = new DefaultEffectsManager();
        _camera = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera
        {
            Position = new Point3D(0, 0, 300),
            LookDirection = new Vector3D(0, 0, -1),
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 45,
            NearPlaneDistance = 0.1,
            FarPlaneDistance = 100000
        };
    }

    public void UpdateDetectedCircles(IEnumerable<DetectedCircleData> circles)
    {
        DetectedCircleModels.Clear();
        
        // Clear legacy single properties
        DetectedCircleGeometry = null;
        DetectedCircleOutlineGeometry = null;

        foreach (var circleData in circles)
        {
            // Create Point Cloud Geometry Model
            if (circleData.Cloud != null && circleData.Cloud.Points.Count > 0)
            {
                try 
                {
                    var (geometry, _, _) = GpuPointCloudRenderer.CreateGeometry(circleData.Cloud, enableLod: false);
                    var pointModel = new PointGeometryModel3D
                    {
                        Geometry = geometry,
                        Color = Colors.Yellow, 
                        Size = new System.Windows.Size(4, 4),
                        FixedSize = true
                    };
                    DetectedCircleModels.Add(pointModel);
                }
                catch { /* ignore */ }
            }

            // Create Outline Geometry Model
            if (circleData.Result != null && circleData.Result.Radius > 0)
            {
                var outlineGeometry = CreateCircleOutline(
                    circleData.Result.Center,
                    circleData.Result.Radius,
                    circleData.Result.Normal);
                
                var lineModel = new LineGeometryModel3D
                {
                    Geometry = outlineGeometry,
                    Color = Colors.Red,
                    Thickness = 4
                };
                DetectedCircleModels.Add(lineModel);
            }
        }
    }

    public void UpdatePointCloud(List<PointCloudData> clouds, bool fitCamera)
    {
        if (clouds == null || clouds.Count == 0)
        {
            PointCloudGeometry = null;
            return;
        }

        try
        {
            PointGeometry3D geometry;
            if (clouds.Count == 1)
                (geometry, _, _) = GpuPointCloudRenderer.CreateGeometry(clouds[0], enableLod: true);
            else
                (geometry, _, _) = GpuPointCloudRenderer.CreateGeometryFromMultiple(clouds);

            PointCloudGeometry = geometry;
            
            if (fitCamera)
            {
                FitCameraToPointCloud();
            }

            CreateOriginAxes();
        }
        catch (Exception)
        {
            // Handle or log error
            PointCloudGeometry = null;
        }
    }

    public void ClearPointCloud()
    {
        PointCloudGeometry = null;
    }

    public void UpdateDetectedCircle(PointCloudData? detectedCloud, CircleDetectionResult? circleResult)
    {
        if (detectedCloud != null && detectedCloud.Points.Count > 0)
        {
            try
            {
                var (geometry, _, _) = GpuPointCloudRenderer.CreateGeometry(detectedCloud, enableLod: false);
                DetectedCircleGeometry = geometry;

                if (circleResult != null && circleResult.Radius > 0)
                {
                    DetectedCircleOutlineGeometry = CreateCircleOutline(
                        circleResult.Center,
                        circleResult.Radius,
                        circleResult.Normal);
                }
                else
                {
                    DetectedCircleOutlineGeometry = null;
                }
            }
            catch
            {
                DetectedCircleGeometry = null;
                DetectedCircleOutlineGeometry = null;
            }
        }
        else
        {
            DetectedCircleGeometry = null;
            DetectedCircleOutlineGeometry = null;
        }
    }

    private LineGeometry3D CreateCircleOutline(System.Numerics.Vector3 center, float radius, System.Numerics.Vector3 normal)
    {
        const int segments = 64;
        var positions = new Vector3Collection();
        var indices = new IntCollection();

        var (u, v) = GetPlaneAxes(normal);

        for (int i = 0; i < segments; i++)
        {
            float angle = i * 2.0f * MathF.PI / segments;
            float x = radius * MathF.Cos(angle);
            float y = radius * MathF.Sin(angle);

            var point = center + x * u + y * v;
            positions.Add(new Vector3(point.X, point.Y, point.Z));
        }

        for (int i = 0; i < segments; i++)
        {
            indices.Add(i);
            indices.Add((i + 1) % segments);
        }

        return new LineGeometry3D
        {
            Positions = positions,
            Indices = indices
        };
    }

    private (System.Numerics.Vector3 u, System.Numerics.Vector3 v) GetPlaneAxes(System.Numerics.Vector3 normal)
    {
        var u = Math.Abs(normal.X) < 0.9f
            ? System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(normal, System.Numerics.Vector3.UnitX))
            : System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(normal, System.Numerics.Vector3.UnitY));
        var v = System.Numerics.Vector3.Cross(normal, u);
        return (u, v);
    }

    public void FitCameraToPointCloud()
    {
        if (PointCloudGeometry?.Positions == null || PointCloudGeometry.Positions.Count == 0 || Camera == null)
            return;

        var positions = PointCloudGeometry.Positions;
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        foreach (var p in positions)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        var center = (min + max) * 0.5f;
        var extents = max - min;
        float radius = extents.Length() * 0.5f;
        if (radius <= 0) radius = 1f;

        const double farPlaneBufferFactor = 10.0;

        var pc = Camera as HelixToolkit.Wpf.SharpDX.PerspectiveCamera;
        if (pc != null)
        {
            double fovRad = pc.FieldOfView * Math.PI / 180.0;
            double distance = radius / Math.Sin(fovRad / 2.0);
            distance *= 1.2;
            var position = new Point3D(center.X, center.Y, center.Z + distance);
            var lookDir = new Vector3D(center.X - position.X, center.Y - position.Y, center.Z - position.Z);
            pc.Position = position;
            pc.LookDirection = lookDir;
            pc.UpDirection = new Vector3D(0, 1, 0);
            pc.NearPlaneDistance = 0.1;
            var desiredFar = distance + radius * farPlaneBufferFactor;
            if (desiredFar > pc.FarPlaneDistance)
                pc.FarPlaneDistance = desiredFar;
        }
        else
        {
            var oc = Camera as HelixToolkit.Wpf.SharpDX.OrthographicCamera;
            if (oc != null)
            {
                double distance = radius * 2.5;
                var position = new Point3D(center.X, center.Y, center.Z + distance);
                var lookDir = new Vector3D(center.X - position.X, center.Y - position.Y, center.Z - position.Z);
                oc.Position = position;
                oc.LookDirection = lookDir;
                oc.UpDirection = new Vector3D(0, 1, 0);
                oc.Width = radius * 2.5;
                oc.NearPlaneDistance = 0.1;
                var desiredFar = distance + radius * farPlaneBufferFactor;
                if (desiredFar > oc.FarPlaneDistance)
                    oc.FarPlaneDistance = desiredFar;
            }
        }
    }

    private void CreateOriginAxes()
    {
        float axisLength = 50f;
        if (PointCloudGeometry?.Positions != null && PointCloudGeometry.Positions.Count > 0)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var p in PointCloudGeometry.Positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            var size = max - min;
            axisLength = Math.Max(10f, Math.Min((size.X + size.Y + size.Z) / 30f, 500f));
        }

        // Legacy combined geometry
        var positionsCombined = new Vector3Collection
        {
            new Vector3(0,0,0), new Vector3(axisLength,0,0),
            new Vector3(0,0,0), new Vector3(0,axisLength,0),
            new Vector3(0,0,0), new Vector3(0,0,axisLength)
        };
        var colorsCombined = new Color4Collection
        {
            new Color4(1,0,0,1), new Color4(1,0,0,1),
            new Color4(0,1,0,1), new Color4(0,1,0,1),
            new Color4(0,0,1,1), new Color4(0,0,1,1)
        };
        OriginAxesGeometry = new LineGeometry3D { Positions = positionsCombined, Colors = colorsCombined, Indices = new IntCollection {0,1,2,3,4,5} };

        // Separate geometries
        OriginXGeometry = new LineGeometry3D
        {
            Positions = new Vector3Collection { new Vector3(0,0,0), new Vector3(axisLength,0,0) },
            Indices = new IntCollection {0,1}
        };
        OriginYGeometry = new LineGeometry3D
        {
            Positions = new Vector3Collection { new Vector3(0,0,0), new Vector3(0,axisLength,0) },
            Indices = new IntCollection {0,1}
        };
        OriginZGeometry = new LineGeometry3D
        {
            Positions = new Vector3Collection { new Vector3(0,0,0), new Vector3(0,0,axisLength) },
            Indices = new IntCollection {0,1}
        };
    }

    public void UpdateRoiVisualizations(IEnumerable<RoiVisualizationData> rois)
    {
        RoiVisualizations.Clear();
        RoiModels.Clear();
        
        // Clear legacy
        RoiWireframeGeometry = null;
        RoiCenterPointGeometry = null;

        foreach (var roiData in rois)
        {
            var (major, minor, centerPoint) = CreateRoiGeometry(roiData.Center, roiData.Size, roiData.Radius, roiData.Shape);
            
            var roiViz = new RoiVisualization
            {
                NodeId = roiData.NodeId,
                WireframeGeometry = major, // Bind major to legacy property
                CenterPointGeometry = centerPoint
            };
            RoiVisualizations.Add(roiViz);

            if (major != null)
            {
                RoiModels.Add(new LineGeometryModel3D
                {
                    Geometry = major,
                    Thickness = 3,
                    Color = Colors.Yellow
                });
            }

            if (minor != null)
            {
                RoiModels.Add(new LineGeometryModel3D
                {
                    Geometry = minor,
                    Thickness = 1.0,
                    Color = Color.FromArgb(100, 255, 255, 0) // Semi-transparent yellow for thin lines
                });
            }

            if (centerPoint != null)
            {
                var meshModel = new MeshGeometryModel3D
                {
                    Geometry = centerPoint,
                    Material = new PhongMaterial { DiffuseColor = new Color4(1.0f, 0.0f, 0.0f, 1.0f) }
                };
                RoiModels.Add(meshModel);
            }

            // For legacy binding support (shows last one)
            RoiWireframeGeometry = major;
            RoiCenterPointGeometry = centerPoint;
        }
    }

    private (LineGeometry3D? major, LineGeometry3D? minor, HelixToolkit.Wpf.SharpDX.MeshGeometry3D centerPoint) CreateRoiGeometry(
        System.Numerics.Vector3 center, 
        System.Numerics.Vector3 size, 
        float radius, 
        ROIShape shape)
    {
        var positions = new Vector3Collection();
        var majorIndices = new IntCollection();
        var minorIndices = new IntCollection();

        if (shape == ROIShape.Box)
        {
            var hx = size.X / 2;
            var hy = size.Y / 2;
            var hz = size.Z / 2;

            var corners = new[]
            {
                new Vector3(center.X - hx, center.Y - hy, center.Z - hz),
                new Vector3(center.X + hx, center.Y - hy, center.Z - hz),
                new Vector3(center.X + hx, center.Y + hy, center.Z - hz),
                new Vector3(center.X - hx, center.Y + hy, center.Z - hz),
                new Vector3(center.X - hx, center.Y - hy, center.Z + hz),
                new Vector3(center.X + hx, center.Y - hy, center.Z + hz),
                new Vector3(center.X + hx, center.Y + hy, center.Z + hz),
                new Vector3(center.X - hx, center.Y + hy, center.Z + hz),
            };

            foreach (var corner in corners)
                positions.Add(corner);

            int[] edgeIndices = { 0,1, 1,2, 2,3, 3,0, 4,5, 5,6, 6,7, 7,4, 0,4, 1,5, 2,6, 3,7 };
            foreach (var idx in edgeIndices)
                majorIndices.Add(idx);
        }
        else if (shape == ROIShape.Cylinder)
        {
            int segments = 64; // High resolution for smooth circles
            
            // Top circle (Y+)
            for (int i = 0; i < segments; i++)
            {
                float angle = i * 2 * MathF.PI / segments;
                float x = center.X + radius * MathF.Cos(angle);
                float z = center.Z + radius * MathF.Sin(angle);
                positions.Add(new Vector3(x, center.Y + size.Y / 2, z));
            }
            // Bottom circle (Y-)
            for (int i = 0; i < segments; i++)
            {
                float angle = i * 2 * MathF.PI / segments;
                float x = center.X + radius * MathF.Cos(angle);
                float z = center.Z + radius * MathF.Sin(angle);
                positions.Add(new Vector3(x, center.Y - size.Y / 2, z));
            }

            // Indices
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                
                // Top Circle (Major)
                majorIndices.Add(i);
                majorIndices.Add(next);

                // Bottom Circle (Major)
                majorIndices.Add(segments + i);
                majorIndices.Add(segments + next);

                // Verticals
                // Major: 4 lines (0, 90, 180, 270 degrees)
                if (i % (segments / 4) == 0)
                {
                    majorIndices.Add(i);
                    majorIndices.Add(segments + i);
                }
                // Minor: Every 8th segment (approx 45 degrees), excluding majors
                else if (i % (segments / 8) == 0)
                {
                    minorIndices.Add(i);
                    minorIndices.Add(segments + i);
                }
            }
        }
        else if (shape == ROIShape.Sphere)
        {
            int segments = 64;
            int rings = 32;

            for (int ring = 0; ring <= rings; ring++)
            {
                float phi = ring * MathF.PI / rings;
                for (int seg = 0; seg < segments; seg++)
                {
                    float theta = seg * 2 * MathF.PI / segments;
                    float x = center.X + radius * MathF.Sin(phi) * MathF.Cos(theta);
                    float y = center.Y + radius * MathF.Cos(phi);
                    float z = center.Z + radius * MathF.Sin(phi) * MathF.Sin(theta);

                    positions.Add(new Vector3(x, y, z));
                }
            }

            for (int ring = 0; ring < rings; ring++)
            {
                for (int seg = 0; seg < segments; seg++)
                {
                    int current = ring * segments + seg;
                    int nextSeg = (seg + 1) % segments;
                    int nextRing = (ring + 1) * segments + seg;

                    // Horizontal lines (Latitudes)
                    // Major: Equator
                    if (ring == rings / 2)
                    {
                        majorIndices.Add(current);
                        majorIndices.Add(ring * segments + nextSeg);
                    }
                    // Minor: Every 4th ring
                    else if (ring % 4 == 0)
                    {
                        minorIndices.Add(current);
                        minorIndices.Add(ring * segments + nextSeg);
                    }

                    // Vertical lines (Longitudes)
                    // Major: 0, 90, 180, 270 degrees
                    if (seg % (segments / 4) == 0)
                    {
                        majorIndices.Add(current);
                        majorIndices.Add(nextRing);
                    }
                    // Minor: Every 8th segment
                    else if (seg % (segments / 8) == 0)
                    {
                        minorIndices.Add(current);
                        minorIndices.Add(nextRing);
                    }
                }
            }
        }

        var majorGeometry = new LineGeometry3D
        {
            Positions = positions,
            Indices = majorIndices
        };

        var minorGeometry = minorIndices.Count > 0 ? new LineGeometry3D
        {
            Positions = positions,
            Indices = minorIndices
        } : null;

        var centerPoint = CreateSphereGeometry(center, RoiCenterSphereRadius, 6, 3);

        return (majorGeometry, minorGeometry, centerPoint);
    }

    private HelixToolkit.Wpf.SharpDX.MeshGeometry3D CreateSphereGeometry(System.Numerics.Vector3 center, float radius, int segments, int rings)
    {
        var positions = new Vector3Collection();
        var indices = new IntCollection();
        var normals = new Vector3Collection();
        var texCoords = new Vector2Collection();

        for (int ring = 0; ring <= rings; ring++)
        {
            float phi = ring * MathF.PI / rings;
            for (int seg = 0; seg <= segments; seg++)
            {
                float theta = seg * 2 * MathF.PI / segments;
                float x = center.X + radius * MathF.Sin(phi) * MathF.Cos(theta);
                float y = center.Y + radius * MathF.Cos(phi);
                float z = center.Z + radius * MathF.Sin(phi) * MathF.Sin(theta);

                positions.Add(new Vector3(x, y, z));

                var normal = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(
                    x - center.X, y - center.Y, z - center.Z));
                normals.Add(new Vector3(normal.X, normal.Y, normal.Z));

                texCoords.Add(new Vector2((float)seg / segments, (float)ring / rings));
            }
        }

        for (int ring = 0; ring < rings; ring++)
        {
            for (int seg = 0; seg < segments; seg++)
            {
                int current = ring * (segments + 1) + seg;
                int next = current + (segments + 1);

                indices.Add(current);
                indices.Add(next);
                indices.Add(current + 1);

                indices.Add(current + 1);
                indices.Add(next);
                indices.Add(next + 1);
            }
        }

        return new HelixToolkit.Wpf.SharpDX.MeshGeometry3D
        {
            Positions = positions,
            Indices = indices,
            Normals = normals,
            TextureCoordinates = texCoords
        };
    }
}

public class RoiVisualizationData
{
    public string NodeId { get; set; } = string.Empty;
    public System.Numerics.Vector3 Center { get; set; }
    public System.Numerics.Vector3 Size { get; set; }
    public float Radius { get; set; }
    public ROIShape Shape { get; set; }
}

// Helper class to hold ROI visualization data
public class RoiVisualization : ObservableObject
{
    public string NodeId { get; set; } = string.Empty;
    public LineGeometry3D? WireframeGeometry { get; set; }
    public HelixToolkit.Wpf.SharpDX.MeshGeometry3D? CenterPointGeometry { get; set; }
}

public class DetectedCircleData
{
    public string NodeId { get; set; } = string.Empty;
    public PointCloudData? Cloud { get; set; }
    public CircleDetectionResult? Result { get; set; }
}
