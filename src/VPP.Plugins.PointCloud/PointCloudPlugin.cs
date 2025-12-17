using VPP.Core.Interfaces;
using VPP.Plugins.PointCloud.Nodes;

namespace VPP.Plugins.PointCloud;

public class PointCloudPlugin : IPlugin
{
    public string Id => "VPP.Plugins.PointCloud";
    public string Name => "Point Cloud Processing";
    public string Version => "1.0.0";
    public string Author => "VPP Team";
    public string Description => "Point cloud import, ROI, detection, measurement, and inspection nodes";

    public IReadOnlyList<Type> NodeTypes { get; } = new List<Type>
    {
        // Import & Transform
        typeof(ImportPointCloudNode),
        typeof(RigidTransformNode),

        // ROI
        typeof(ROIDrawNode),
        typeof(ROIFilterNode),

        // Detection (Geometry Fitting)
        typeof(CircleDetectionNode),
        typeof(PlaneFittingNode),
        typeof(SphereFittingNode),
        typeof(CylinderFittingNode),
        typeof(LineFittingNode),

        // Basic Measurements
        typeof(DistanceMeasurementNode),
        typeof(AngleMeasurementNode),
        typeof(HeightMeasurementNode),
        typeof(BoundingBoxMeasurementNode),
        typeof(CentroidMeasurementNode),
        typeof(PointDensityNode),
        typeof(SurfaceAreaNode),
        typeof(PointToPlaneDistanceNode),

        // GD&T Measurements
        typeof(FlatnessMeasurementNode),
        typeof(RoundnessMeasurementNode),
        typeof(CylindricityMeasurementNode),
        typeof(ParallelismMeasurementNode),
        typeof(PerpendicularityMeasurementNode),
        typeof(ConcentricityMeasurementNode),
        typeof(CoaxialityMeasurementNode),

        // Inspection
        typeof(InspectionNode)
    };

    public void Initialize() { }
    public void Shutdown() { }
}
