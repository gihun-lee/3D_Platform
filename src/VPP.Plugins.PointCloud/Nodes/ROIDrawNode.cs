using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Models;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("ROI Draw", "Point Cloud/ROI", "Create a 3D region of interest")]
public class ROIDrawNode : NodeBase
{
    public ROIDrawNode()
    {
        AddOutputPort<ROI3D>("ROI", "The created ROI object.");
        AddParameter<float>("CenterX", 0f, required: false, displayName: "Center X",
            description: "ROI center X coordinate");
        AddParameter<float>("CenterY", 0f, required: false, displayName: "Center Y",
            description: "ROI center Y coordinate");
        AddParameter<float>("CenterZ", 0f, required: false, displayName: "Center Z",
            description: "ROI center Z coordinate");
        AddParameter<float>("SizeX", 10f, required: false, displayName: "Size X",
            description: "ROI size in X direction");
        AddParameter<float>("SizeY", 10f, required: false, displayName: "Size Y",
            description: "ROI size in Y direction");
        AddParameter<float>("SizeZ", 10f, required: false, displayName: "Size Z",
            description: "ROI size in Z direction");
        AddParameter<float>("Radius", 5f, required: false, displayName: "Radius",
            description: "Radius for cylinder/sphere shapes");
        AddParameter<string>("Shape", "Box", required: false, displayName: "Shape",
            description: "ROI shape: Box, Cylinder, or Sphere");
    }

    protected override Task ExecuteCoreAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        var centerX = GetParameter<float>("CenterX");
        var centerY = GetParameter<float>("CenterY");
        var centerZ = GetParameter<float>("CenterZ");
        var sizeX = GetParameter<float>("SizeX");
        var sizeY = GetParameter<float>("SizeY");
        var sizeZ = GetParameter<float>("SizeZ");
        var radius = GetParameter<float>("Radius");
        var shapeStr = GetParameter<string>("Shape") ?? "Box";

        var shape = shapeStr.ToLower() switch
        {
            "cylinder" => ROIShape.Cylinder,
            "sphere" => ROIShape.Sphere,
            _ => ROIShape.Box
        };

        var roi = new ROI3D
        {
            Center = new Vector3(centerX, centerY, centerZ),
            Size = new Vector3(sizeX, sizeY, sizeZ),
            Shape = shape,
            Radius = radius
        };

        // Set the output port value
        SetOutputValue("ROI", roi);

        // Store ROI definition with unique key for this node
        context.Set($"{ExecutionContext.ROIKey}_{Id}", roi);

        return Task.CompletedTask;
    }
}
