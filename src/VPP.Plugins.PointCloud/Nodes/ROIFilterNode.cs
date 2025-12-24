using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("ROI Filter", "Point Cloud/Filter", "Filter points within a 3D region of interest")]
public class ROIFilterNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public void SetGraph(NodeGraph graph)
    {
        _graph = graph;
    }

    public ROIFilterNode()
    {
        AddInputPort<ROI3D>("ROI", "The ROI to use for filtering.");
    }

    protected override Task ExecuteCoreAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        // Get input point cloud data (prefer filtered, fall back to original)
        var cloud = context.Get<PointCloudData>(ExecutionContext.FilteredCloudKey)
                    ?? context.Get<PointCloudData>(ExecutionContext.PointCloudKey);
        if (cloud == null)
            throw new InvalidOperationException("No point cloud found in context. Run Import Point Cloud node first.");

        // Try to get ROI from the input port first
        var roi = GetInputValue<ROI3D>("ROI");

        // If no ROI from input port, try to get from connected ROI Draw node
        if (roi == null)
        {
            roi = GetConnectedRoi(context);
        }

        if (roi == null)
        {
            // If no ROI is connected, we cannot filter. Pass through original data.
            context.Set($"{ExecutionContext.FilteredCloudKey}_{Id}", cloud);
            return Task.CompletedTask;
        }

        var filtered = new PointCloudData();

        for (int i = 0; i < cloud.Points.Count; i++)
        {
            var point = cloud.Points[i];
            if (IsInROI(point, roi))
            {
                filtered.Points.Add(point);
                if (cloud.Colors != null && i < cloud.Colors.Count)
                {
                    filtered.Colors ??= new List<Vector3>();
                    filtered.Colors.Add(cloud.Colors[i]);
                }
            }
        }

        filtered.ComputeBoundingBox();

        // Store filtered cloud and ROI in context with unique keys for this node
        context.Set($"{ExecutionContext.FilteredCloudKey}_{Id}", filtered);
        context.Set($"{ExecutionContext.ROIKey}_{Id}", roi);

        return Task.CompletedTask;
    }

    private ROI3D? GetConnectedRoi(ExecutionContext context)
    {
        if (_graph == null) return null;

        // Find connected ROI Draw node
        var roiDrawNodeId = _graph.Connections
            .Where(c => c.TargetNodeId == Id)
            .Select(c => c.SourceNodeId)
            .FirstOrDefault(id => 
            {
                var node = _graph.Nodes.FirstOrDefault(n => n.Id == id);
                return node?.Name == "ROI Draw";
            });

        if (roiDrawNodeId != null)
        {
            return context.Get<ROI3D>($"{ExecutionContext.ROIKey}_{roiDrawNodeId}");
        }

        return null;
    }

    private bool IsInROI(Vector3 point, ROI3D roi)

    {
        var diff = point - roi.Center;

        return roi.Shape switch
        {
            ROIShape.Box =>
                Math.Abs(diff.X) <= roi.Size.X / 2 &&
                Math.Abs(diff.Y) <= roi.Size.Y / 2 &&
                Math.Abs(diff.Z) <= roi.Size.Z / 2,

            ROIShape.Cylinder =>
                // For line scan data, Z is usually the scan direction (constant)
                // So cylinder should be along Z-axis with XY radius
                Math.Sqrt(diff.X * diff.X + diff.Y * diff.Y) <= roi.Radius &&
                Math.Abs(diff.Z) <= roi.Size.Z / 2,

            ROIShape.Sphere =>
                diff.Length() <= roi.Radius,

            _ => false
        };
    }
}
