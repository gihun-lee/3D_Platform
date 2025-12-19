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

    // Depth Visualization
    [ObservableProperty] private bool _isDepthVisualizationEnabled;

    // Store last clouds for re-rendering
    private List<PointCloudData>? _lastClouds;

    // Store ORIGINAL (imported) Z range - set once and never changes
    private float? _originalMinZ;
    private float? _originalMaxZ;
    
    // Cache for depth color arrays to reduce allocations
    private List<System.Numerics.Vector3>? _depthColorCache;

    public SceneViewModel()
    {
        _effectsManager = new DefaultEffectsManager();
        _camera = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera
        {
            Position = new Point3D(0, 0, 300),
            LookDirection = new Vector3D(0, 0, -300),
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 45,
            NearPlaneDistance = 0.1,
            FarPlaneDistance = 100000
        };
    }

    // Handler for when depth visualization is toggled
    partial void OnIsDepthVisualizationEnabledChanged(bool value)
    {
        // Re-render point cloud with new visualization mode
        if (_lastClouds != null && _lastClouds.Count > 0)
        {
            UpdatePointCloud(_lastClouds, fitCamera: false);
        }
    }

    /// <summary>
    /// Set the ORIGINAL (imported) Z range - call this ONCE after importing data
    /// This range will be used for depth coloring regardless of filtering/transformations
    /// </summary>
    public void SetOriginalDepthRange(float minZ, float maxZ)
    {
        _originalMinZ = minZ;
        _originalMaxZ = maxZ;
    }

    /// <summary>
    /// Clear the original depth range (e.g., when clearing the workflow)
    /// </summary>
    public void ClearOriginalDepthRange()
    {
        _originalMinZ = null;
        _originalMaxZ = null;
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
            _lastClouds = null;
            return;
        }

        // Store clouds for re-rendering when depth visualization is toggled
        _lastClouds = clouds;

        try
        {
            PointGeometry3D geometry;
            
            // Apply depth visualization if enabled
            if (IsDepthVisualizationEnabled)
            {
                // Use ORIGINAL (imported) Z range if available, otherwise calculate from current clouds
                float globalMinZ, globalMaxZ;
                
                if (_originalMinZ.HasValue && _originalMaxZ.HasValue)
                {
                    // Use the fixed ORIGINAL range from imported data
                    globalMinZ = _originalMinZ.Value;
                    globalMaxZ = _originalMaxZ.Value;
                }
                else
                {
                    // Fallback: calculate from current clouds (e.g., if range wasn't set)
                    globalMinZ = float.MaxValue;
                    globalMaxZ = float.MinValue;
                    foreach (var cloud in clouds)
                    {
                        foreach (var p in cloud.Points)
                        {
                            globalMinZ = Math.Min(globalMinZ, p.Z);
                            globalMaxZ = Math.Max(globalMaxZ, p.Z);
                        }
                    }
                }

                // Apply depth colors using the SAME global Z range for all clouds
                var cloudsWithDepthColors = clouds.Select(c => ApplyDepthColors(c, globalMinZ, globalMaxZ)).ToList();
                if (cloudsWithDepthColors.Count == 1)
                    (geometry, _, _) = GpuPointCloudRenderer.CreateGeometry(cloudsWithDepthColors[0], enableLod: true);
                else
                    (geometry, _, _) = GpuPointCloudRenderer.CreateGeometryFromMultiple(cloudsWithDepthColors);
            }
            else
            {
                // Normal rendering - keep original colors
                if (clouds.Count == 1)
                    (geometry, _, _) = GpuPointCloudRenderer.CreateGeometry(clouds[0], enableLod: true);
                else
                    (geometry, _, _) = GpuPointCloudRenderer.CreateGeometryFromMultiple(clouds);
            }

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

    private PointCloudData ApplyDepthColors(PointCloudData cloud, float globalMinZ, float globalMaxZ)
    {
        var pointCount = cloud.Points.Count;
        
        // Reuse or create color cache (reduces GC pressure)
        if (_depthColorCache == null || _depthColorCache.Capacity < pointCount)
        {
            _depthColorCache = new List<System.Numerics.Vector3>(pointCount);
        }
        else
        {
            _depthColorCache.Clear();
        }

        float range = globalMaxZ - globalMinZ;
        if (range < 1e-6f) range = 1.0f; // Avoid division by zero

        // Apply depth-based colors using GLOBAL Z range (optimized loop)
        for (int i = 0; i < pointCount; i++)
        {
            var p = cloud.Points[i];
            float normalized = (p.Z - globalMinZ) / range; // 0 = global min depth, 1 = global max depth
            _depthColorCache.Add(GetDepthColor(normalized));
        }

        var coloredCloud = new PointCloudData
        {
            Points = new List<System.Numerics.Vector3>(cloud.Points),
            Normals = cloud.Normals != null ? new List<System.Numerics.Vector3>(cloud.Normals) : null,
            Colors = _depthColorCache
        };

        coloredCloud.ComputeBoundingBox();
        return coloredCloud;
    }

    private System.Numerics.Vector3 GetDepthColor(float t)
    {
        // Rainbow colormap: Blue (far) -> Cyan -> Green -> Yellow -> Orange -> Red (near)
        // Inverted so that closer points (higher Z) are warmer colors
        t = 1.0f - t; // Invert: now 0 = far (blue), 1 = near (red)

        if (t < 0.167f) // Blue -> Cyan
        {
            float local = t / 0.167f;
            return new System.Numerics.Vector3(0, local, 1);
        }
        else if (t < 0.333f) // Cyan -> Green
        {
            float local = (t - 0.167f) / 0.167f;
            return new System.Numerics.Vector3(0, 1, 1 - local);
        }
        else if (t < 0.5f) // Green -> Yellow
        {
            float local = (t - 0.333f) / 0.167f;
            return new System.Numerics.Vector3(local, 1, 0);
        }
        else if (t < 0.667f) // Yellow -> Orange
        {
            float local = (t - 0.5f) / 0.167f;
            return new System.Numerics.Vector3(1, 1 - 0.5f * local, 0);
        }
        else if (t < 0.833f) // Orange -> Red
        {
            float local = (t - 0.667f) / 0.167f;
            return new System.Numerics.Vector3(1, 0.5f - 0.5f * local, 0);
        }
        else // Dark Red
        {
            return new System.Numerics.Vector3(1, 0, 0);
        }
    }

    public void ClearPointCloud()
    {
        PointCloudGeometry = null;
        ClearOriginalDepthRange();
        _depthColorCache = null; // Release cached memory
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

                // Verticals - All Minor
                if (i % (segments / 8) == 0)
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

                    // Vertical lines (Longitudes) - All Minor
                    if (seg % (segments / 8) == 0)
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

    #region Measurement Visualization

    // Measurement visualization properties
    [ObservableProperty] private ObservableElement3DCollection _measurementModels = new();
    [ObservableProperty] private LineGeometry3D? _measurementLineGeometry;
    [ObservableProperty] private Color _measurementLineColor = Colors.Cyan;
    [ObservableProperty] private PointGeometry3D? _measurementPointsGeometry;
    [ObservableProperty] private Color _measurementPointColor = Colors.Magenta;
    [ObservableProperty] private LineGeometry3D? _measurementPlaneGeometry;
    [ObservableProperty] private Color _measurementPlaneColor = Colors.LightBlue;
    [ObservableProperty] private LineGeometry3D? _boundingBoxGeometry;
    [ObservableProperty] private Color _boundingBoxColor = Colors.Orange;

    /// <summary>
    /// Update picked points visualization
    /// </summary>
    public void UpdatePickedPoints(List<System.Numerics.Vector3> points)
    {
        MeasurementModels.Clear();

        if (points == null || points.Count == 0)
        {
            MeasurementPointsGeometry = null;
            MeasurementLineGeometry = null;
            return;
        }

        // Create point geometry for picked points
        var pointPositions = new Vector3Collection();
        foreach (var p in points)
        {
            pointPositions.Add(new Vector3(p.X, p.Y, p.Z));
        }

        var pointGeometry = new PointGeometry3D { Positions = pointPositions };
        MeasurementPointsGeometry = pointGeometry;

        // Add point spheres for visibility
        foreach (var point in points)
        {
            var sphereGeometry = CreateSphereGeometry(point, 1.0f, 8, 4);
            var sphereModel = new MeshGeometryModel3D
            {
                Geometry = sphereGeometry,
                Material = new PhongMaterial { DiffuseColor = new Color4(1.0f, 0.0f, 1.0f, 1.0f) } // Magenta
            };
            MeasurementModels.Add(sphereModel);
        }

        // If we have 2+ points, draw lines between them
        if (points.Count >= 2)
        {
            var linePositions = new Vector3Collection();
            var lineIndices = new IntCollection();

            for (int i = 0; i < points.Count; i++)
            {
                linePositions.Add(new Vector3(points[i].X, points[i].Y, points[i].Z));
            }

            for (int i = 0; i < points.Count - 1; i++)
            {
                lineIndices.Add(i);
                lineIndices.Add(i + 1);
            }

            var lineGeometry = new LineGeometry3D
            {
                Positions = linePositions,
                Indices = lineIndices
            };

            MeasurementLineGeometry = lineGeometry;

            var lineModel = new LineGeometryModel3D
            {
                Geometry = lineGeometry,
                Color = Colors.Cyan,
                Thickness = 3
            };
            MeasurementModels.Add(lineModel);
        }
    }

    /// <summary>
    /// Show distance measurement visualization
    /// </summary>
    public void ShowDistanceMeasurement(System.Numerics.Vector3 p1, System.Numerics.Vector3 p2, float distance)
    {
        MeasurementModels.Clear();

        // Create line between points
        var linePositions = new Vector3Collection
        {
            new Vector3(p1.X, p1.Y, p1.Z),
            new Vector3(p2.X, p2.Y, p2.Z)
        };
        var lineIndices = new IntCollection { 0, 1 };

        var lineGeometry = new LineGeometry3D
        {
            Positions = linePositions,
            Indices = lineIndices
        };
        MeasurementLineGeometry = lineGeometry;

        // Main measurement line
        MeasurementModels.Add(new LineGeometryModel3D
        {
            Geometry = lineGeometry,
            Color = Colors.Cyan,
            Thickness = 4
        });

        // End point spheres
        var sphere1 = CreateSphereGeometry(p1, 1.5f, 8, 4);
        MeasurementModels.Add(new MeshGeometryModel3D
        {
            Geometry = sphere1,
            Material = new PhongMaterial { DiffuseColor = new Color4(0, 1, 0, 1) } // Green
        });

        var sphere2 = CreateSphereGeometry(p2, 1.5f, 8, 4);
        MeasurementModels.Add(new MeshGeometryModel3D
        {
            Geometry = sphere2,
            Material = new PhongMaterial { DiffuseColor = new Color4(1, 0, 0, 1) } // Red
        });

        // Draw X, Y, Z component lines (dashed visualization)
        var componentPositions = new Vector3Collection
        {
            // X component
            new Vector3(p1.X, p1.Y, p1.Z),
            new Vector3(p2.X, p1.Y, p1.Z),
            // Y component
            new Vector3(p2.X, p1.Y, p1.Z),
            new Vector3(p2.X, p2.Y, p1.Z),
            // Z component
            new Vector3(p2.X, p2.Y, p1.Z),
            new Vector3(p2.X, p2.Y, p2.Z)
        };
        var componentIndices = new IntCollection { 0, 1, 2, 3, 4, 5 };

        MeasurementModels.Add(new LineGeometryModel3D
        {
            Geometry = new LineGeometry3D { Positions = componentPositions, Indices = componentIndices },
            Color = Color.FromArgb(150, 255, 255, 0), // Semi-transparent yellow
            Thickness = 1
        });
    }

    /// <summary>
    /// Show angle measurement visualization
    /// </summary>
    public void ShowAngleMeasurement(System.Numerics.Vector3 p1, System.Numerics.Vector3 vertex, System.Numerics.Vector3 p3, float angleDegrees)
    {
        MeasurementModels.Clear();

        // Draw lines from vertex to both points
        var linePositions = new Vector3Collection
        {
            new Vector3(vertex.X, vertex.Y, vertex.Z),
            new Vector3(p1.X, p1.Y, p1.Z),
            new Vector3(vertex.X, vertex.Y, vertex.Z),
            new Vector3(p3.X, p3.Y, p3.Z)
        };
        var lineIndices = new IntCollection { 0, 1, 2, 3 };

        MeasurementModels.Add(new LineGeometryModel3D
        {
            Geometry = new LineGeometry3D { Positions = linePositions, Indices = lineIndices },
            Color = Colors.Cyan,
            Thickness = 3
        });

        // Draw arc at vertex
        var v1 = System.Numerics.Vector3.Normalize(p1 - vertex);
        var v2 = System.Numerics.Vector3.Normalize(p3 - vertex);
        var arcRadius = Math.Min((p1 - vertex).Length(), (p3 - vertex).Length()) * 0.3f;

        var arcPositions = new Vector3Collection();
        var arcIndices = new IntCollection();
        const int arcSegments = 32;

        for (int i = 0; i <= arcSegments; i++)
        {
            float t = (float)i / arcSegments;
            var interpolated = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Lerp(v1, v2, t));
            var arcPoint = vertex + interpolated * arcRadius;
            arcPositions.Add(new Vector3(arcPoint.X, arcPoint.Y, arcPoint.Z));

            if (i > 0)
            {
                arcIndices.Add(i - 1);
                arcIndices.Add(i);
            }
        }

        MeasurementModels.Add(new LineGeometryModel3D
        {
            Geometry = new LineGeometry3D { Positions = arcPositions, Indices = arcIndices },
            Color = Colors.Yellow,
            Thickness = 2
        });

        // Point spheres
        foreach (var point in new[] { p1, vertex, p3 })
        {
            var sphere = CreateSphereGeometry(point, 1.0f, 8, 4);
            MeasurementModels.Add(new MeshGeometryModel3D
            {
                Geometry = sphere,
                Material = new PhongMaterial { DiffuseColor = new Color4(1, 0, 1, 1) } // Magenta
            });
        }
    }

    /// <summary>
    /// Show bounding box visualization
    /// </summary>
    public void ShowBoundingBox(System.Numerics.Vector3 min, System.Numerics.Vector3 max, System.Numerics.Vector3 center)
    {
        MeasurementModels.Clear();

        var corners = new[]
        {
            new Vector3(min.X, min.Y, min.Z),
            new Vector3(max.X, min.Y, min.Z),
            new Vector3(max.X, max.Y, min.Z),
            new Vector3(min.X, max.Y, min.Z),
            new Vector3(min.X, min.Y, max.Z),
            new Vector3(max.X, min.Y, max.Z),
            new Vector3(max.X, max.Y, max.Z),
            new Vector3(min.X, max.Y, max.Z),
        };

        var positions = new Vector3Collection();
        foreach (var corner in corners)
            positions.Add(corner);

        var indices = new IntCollection { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 };

        BoundingBoxGeometry = new LineGeometry3D { Positions = positions, Indices = indices };

        MeasurementModels.Add(new LineGeometryModel3D
        {
            Geometry = BoundingBoxGeometry,
            Color = Colors.Orange,
            Thickness = 2
        });

        // Center point
        var centerSphere = CreateSphereGeometry(center, 2.0f, 8, 4);
        MeasurementModels.Add(new MeshGeometryModel3D
        {
            Geometry = centerSphere,
            Material = new PhongMaterial { DiffuseColor = new Color4(1, 0.5f, 0, 1) } // Orange
        });
    }

    /// <summary>
    /// Show centroid visualization
    /// </summary>
    public void ShowCentroid(System.Numerics.Vector3 centroid)
    {
        MeasurementModels.Clear();

        // Centroid sphere
        var sphere = CreateSphereGeometry(centroid, 3.0f, 12, 6);
        MeasurementModels.Add(new MeshGeometryModel3D
        {
            Geometry = sphere,
            Material = new PhongMaterial { DiffuseColor = new Color4(0, 1, 1, 1) } // Cyan
        });

        // Cross axes at centroid
        float axisLength = 20f;
        var axisPositions = new Vector3Collection
        {
            new Vector3(centroid.X - axisLength, centroid.Y, centroid.Z),
            new Vector3(centroid.X + axisLength, centroid.Y, centroid.Z),
            new Vector3(centroid.X, centroid.Y - axisLength, centroid.Z),
            new Vector3(centroid.X, centroid.Y + axisLength, centroid.Z),
            new Vector3(centroid.X, centroid.Y, centroid.Z - axisLength),
            new Vector3(centroid.X, centroid.Y, centroid.Z + axisLength)
        };
        var axisIndices = new IntCollection { 0, 1, 2, 3, 4, 5 };

        MeasurementModels.Add(new LineGeometryModel3D
        {
            Geometry = new LineGeometry3D { Positions = axisPositions, Indices = axisIndices },
            Color = Colors.Cyan,
            Thickness = 1
        });
    }

    /// <summary>
    /// Show plane visualization for flatness/point-to-plane
    /// </summary>
    public void ShowPlane(System.Numerics.Vector3 center, System.Numerics.Vector3 normal, float size = 50f)
    {
        MeasurementModels.Clear();

        var (u, v) = GetPlaneAxes(normal);

        // Create plane grid
        var positions = new Vector3Collection();
        var indices = new IntCollection();

        int gridLines = 10;
        float halfSize = size / 2;

        for (int i = 0; i <= gridLines; i++)
        {
            float t = -halfSize + (size * i / gridLines);

            // Lines along U direction
            var p1 = center + t * v - halfSize * u;
            var p2 = center + t * v + halfSize * u;
            int idx = positions.Count;
            positions.Add(new Vector3(p1.X, p1.Y, p1.Z));
            positions.Add(new Vector3(p2.X, p2.Y, p2.Z));
            indices.Add(idx);
            indices.Add(idx + 1);

            // Lines along V direction
            p1 = center + t * u - halfSize * v;
            p2 = center + t * u + halfSize * v;
            idx = positions.Count;
            positions.Add(new Vector3(p1.X, p1.Y, p1.Z));
            positions.Add(new Vector3(p2.X, p2.Y, p2.Z));
            indices.Add(idx);
            indices.Add(idx + 1);
        }

        MeasurementPlaneGeometry = new LineGeometry3D { Positions = positions, Indices = indices };

        MeasurementModels.Add(new LineGeometryModel3D
        {
            Geometry = MeasurementPlaneGeometry,
            Color = Color.FromArgb(100, 100, 200, 255), // Semi-transparent light blue
            Thickness = 1
        });

        // Normal vector arrow
        var normalEnd = center + normal * (size / 3);
        var normalPositions = new Vector3Collection
        {
            new Vector3(center.X, center.Y, center.Z),
            new Vector3(normalEnd.X, normalEnd.Y, normalEnd.Z)
        };

        MeasurementModels.Add(new LineGeometryModel3D
        {
            Geometry = new LineGeometry3D { Positions = normalPositions, Indices = new IntCollection { 0, 1 } },
            Color = Colors.Green,
            Thickness = 3
        });
    }

    /// <summary>
    /// Show roundness/circle visualization
    /// </summary>
    public void ShowCircleFit(System.Numerics.Vector3 center, float radius, System.Numerics.Vector3 normal)
    {
        MeasurementModels.Clear();

        // Draw fitted circle
        var circleGeometry = CreateCircleOutline(center, radius, normal);

        MeasurementModels.Add(new LineGeometryModel3D
        {
            Geometry = circleGeometry,
            Color = Colors.Lime,
            Thickness = 3
        });

        // Center point
        var centerSphere = CreateSphereGeometry(center, 1.5f, 8, 4);
        MeasurementModels.Add(new MeshGeometryModel3D
        {
            Geometry = centerSphere,
            Material = new PhongMaterial { DiffuseColor = new Color4(0, 1, 0, 1) } // Green
        });

        // Radius line
        var (u, _) = GetPlaneAxes(normal);
        var radiusEnd = center + u * radius;
        var radiusPositions = new Vector3Collection
        {
            new Vector3(center.X, center.Y, center.Z),
            new Vector3(radiusEnd.X, radiusEnd.Y, radiusEnd.Z)
        };

        MeasurementModels.Add(new LineGeometryModel3D
        {
            Geometry = new LineGeometry3D { Positions = radiusPositions, Indices = new IntCollection { 0, 1 } },
            Color = Colors.Yellow,
            Thickness = 2
        });
    }

    /// <summary>
    /// Show cylindricity visualization
    /// </summary>
    public void ShowCylinderFit(System.Numerics.Vector3 axisPoint, System.Numerics.Vector3 axisDir, float radius, float height = 50f)
    {
        MeasurementModels.Clear();

        // Create cylinder visualization
        int segments = 32;
        var positions = new Vector3Collection();
        var indices = new IntCollection();

        var (u, v) = GetPlaneAxes(axisDir);
        var halfHeight = height / 2;
        var top = axisPoint + axisDir * halfHeight;
        var bottom = axisPoint - axisDir * halfHeight;

        // Top circle
        for (int i = 0; i < segments; i++)
        {
            float angle = i * 2 * MathF.PI / segments;
            var offset = u * radius * MathF.Cos(angle) + v * radius * MathF.Sin(angle);
            positions.Add(new Vector3(top.X + offset.X, top.Y + offset.Y, top.Z + offset.Z));
        }

        // Bottom circle
        for (int i = 0; i < segments; i++)
        {
            float angle = i * 2 * MathF.PI / segments;
            var offset = u * radius * MathF.Cos(angle) + v * radius * MathF.Sin(angle);
            positions.Add(new Vector3(bottom.X + offset.X, bottom.Y + offset.Y, bottom.Z + offset.Z));
        }

        // Top circle indices
        for (int i = 0; i < segments; i++)
        {
            indices.Add(i);
            indices.Add((i + 1) % segments);
        }

        // Bottom circle indices
        for (int i = 0; i < segments; i++)
        {
            indices.Add(segments + i);
            indices.Add(segments + (i + 1) % segments);
        }

        // Vertical lines
        for (int i = 0; i < segments; i += segments / 8)
        {
            indices.Add(i);
            indices.Add(segments + i);
        }

        MeasurementModels.Add(new LineGeometryModel3D
        {
            Geometry = new LineGeometry3D { Positions = positions, Indices = indices },
            Color = Colors.Lime,
            Thickness = 2
        });

        // Axis line
        var axisPositions = new Vector3Collection
        {
            new Vector3(bottom.X, bottom.Y, bottom.Z),
            new Vector3(top.X, top.Y, top.Z)
        };

        MeasurementModels.Add(new LineGeometryModel3D
        {
            Geometry = new LineGeometry3D { Positions = axisPositions, Indices = new IntCollection { 0, 1 } },
            Color = Colors.Red,
            Thickness = 3
        });
    }

    /// <summary>
    /// Clear all measurement visualizations
    /// </summary>
    public void ClearMeasurementVisualization()
    {
        MeasurementModels.Clear();
        MeasurementLineGeometry = null;
        MeasurementPointsGeometry = null;
        MeasurementPlaneGeometry = null;
        BoundingBoxGeometry = null;
    }

    #endregion
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
