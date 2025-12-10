using VPP.Core.Interfaces;
using VPP.Plugins.PointCloud.Nodes;

namespace VPP.Plugins.PointCloud;

public class PointCloudPlugin : IPlugin
{
    public string Id => "VPP.Plugins.PointCloud";
    public string Name => "Point Cloud Processing";
    public string Version => "1.0.0";
    public string Author => "VPP Team";
    public string Description => "Point cloud import, ROI, circle detection, and inspection nodes";

    public IReadOnlyList<Type> NodeTypes { get; } = new List<Type>
    {
        typeof(ImportPointCloudNode),
        typeof(ROIDrawNode),
        typeof(ROIFilterNode),
        typeof(CircleDetectionNode),
        typeof(InspectionNode),
        typeof(RigidTransformNode) // Added rigid transform node
    };

    public void Initialize() { }
    public void Shutdown() { }
}
