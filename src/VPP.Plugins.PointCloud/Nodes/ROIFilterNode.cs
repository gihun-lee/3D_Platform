using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("ROI Filter", "Point Cloud/Filter", "Filter points within a 3D region of interest")]
public class ROIFilterNode : NodeBase
{
    public ROIFilterNode()
    {
        AddInputPort<ROI3D>("ROI", "The ROI to use for filtering.");
        // Enable/Disable filter
        AddParameter<bool>("Enabled", true, required: false, displayName: "Enabled",
            description: "Enable or disable ROI filtering");

        // Parameters for ROI definition
        AddParameter<float>("SizeX", 100f, required: false, displayName: "Size X (mm)",
            description: "ROI width in X direction");
        AddParameter<float>("SizeY", 100f, required: false, displayName: "Size Y (mm)",
            description: "ROI width in Y direction");
        AddParameter<float>("SizeZ", 50f, required: false, displayName: "Size Z (mm)",
            description: "ROI height in Z direction");
        AddParameter<float>("CenterX", 0f, required: false, displayName: "Center X",
            description: "ROI center X coordinate");
        AddParameter<float>("CenterY", 0f, required: false, displayName: "Center Y",
            description: "ROI center Y coordinate");
        AddParameter<float>("CenterZ", 0f, required: false, displayName: "Center Z",
            description: "ROI center Z coordinate");
    }

    protected override Task ExecuteCoreAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        // Get input point cloud data (prefer filtered, fall back to original)
        var cloud = context.Get<PointCloudData>(ExecutionContext.FilteredCloudKey)
                    ?? context.Get<PointCloudData>(ExecutionContext.PointCloudKey);
        if (cloud == null)
            throw new InvalidOperationException("No point cloud found in context. Run Import Point Cloud node first.");

        // Check if filter is enabled
        var enabled = GetParameter<bool>("Enabled");
        if (!enabled)
        {
            // Filter is OFF - pass through original data without filtering
            context.Set(ExecutionContext.FilteredCloudKey, cloud);
            // Still try to get and store ROI for visualization
            var existingRoi = context.Get<ROI3D>(ExecutionContext.ROIKey);
            if (existingRoi != null)
            {
                context.Set(ExecutionContext.ROIKey, existingRoi);
            }
            return Task.CompletedTask;
        }

        // Try to get ROI from the input port first
        var roi = GetInputValue<ROI3D>("ROI");

        // If no ROI from input port, try to get from context (legacy) or create one from parameters
        if (roi == null)
        {
            // Try to get ROI from context (set by ROI Draw node)
            roi = context.Get<ROI3D>(ExecutionContext.ROIKey);

            // If no ROI in context, create one from parameters
            if (roi == null)
            {
                roi = new ROI3D
                {
                    Shape = ROIShape.Box,
                    Center = new Vector3(
                        GetParameter<float>("CenterX"),
                        GetParameter<float>("CenterY"),
                        GetParameter<float>("CenterZ")
                    ),
                    Size = new Vector3(
                        GetParameter<float>("SizeX"),
                        GetParameter<float>("SizeY"),
                        GetParameter<float>("SizeZ")
                    )
                };

                // Auto-center ROI at point cloud centroid if center is at origin
                if (roi.Center == Vector3.Zero && cloud.Points.Count > 0)
                {
                    var centroid = Vector3.Zero;
                    foreach (var pt in cloud.Points)
                        centroid += pt;
                    centroid /= cloud.Points.Count;
                    roi.Center = centroid;
                }
            }
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

        // Store filtered cloud and ROI in context
        context.Set(ExecutionContext.FilteredCloudKey, filtered);
        context.Set(ExecutionContext.ROIKey, roi);

        return Task.CompletedTask;
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
