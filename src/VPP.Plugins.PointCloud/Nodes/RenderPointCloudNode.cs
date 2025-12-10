using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Render Point Cloud", "Point Cloud/Visualization", "Render point cloud to 3D viewer")]
public class RenderPointCloudNode : NodeBase
{
    public RenderPointCloudNode()
    {
        AddParameter<float>("PointSize", 2f, required: false, displayName: "Point Size",
            description: "Size of points in visualization");
    }

    protected override Task ExecuteCoreAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        // Get point cloud from context (filtered or original)
        var pointCloud = context.Get<PointCloudData>(ExecutionContext.FilteredCloudKey)
                        ?? context.Get<PointCloudData>(ExecutionContext.PointCloudKey);

        if (pointCloud == null)
            throw new InvalidOperationException("No point cloud found in context. Run Import Point Cloud node first.");

        // Rendering is handled by the viewer automatically
        // This node is just a placeholder for explicit rendering control

        return Task.CompletedTask;
    }
}
