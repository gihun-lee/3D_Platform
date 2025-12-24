using System.Numerics;
using VPP.Core.Attributes;
using VPP.Core.Models;
using VPP.Core.Interfaces;
using VPP.Plugins.PointCloud.Models;
using ExecutionContext = VPP.Core.Models.ExecutionContext;

namespace VPP.Plugins.PointCloud.Nodes;

[NodeInfo("Rigid Transform", "Point Cloud/Transform", "Apply 4x4 rigid transform matrix to the active point cloud (rotation+translation)")]
public class RigidTransformNode : NodeBase, IGraphAwareNode
{
    private NodeGraph? _graph;

    public RigidTransformNode()
    {
        AddParameter<Matrix4x4>("Matrix", Matrix4x4.Identity, required: false, displayName: "Matrix", description: "4x4 transform matrix (row-major). Translation in M14,M24,M34.");
    }

    public void SetGraph(NodeGraph graph) => _graph = graph;

    protected override Task ExecuteCoreAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        var m = GetParameter<Matrix4x4>("Matrix");

        // 1) Require an explicit incoming connection. If none -> just store matrix and exit.
        if (_graph == null)
        {
            context.Set($"RigidTransformMatrix_{Id}", m);
            return Task.CompletedTask;
        }

        var incomingConnection = _graph.Connections.FirstOrDefault(c => c.TargetNodeId == Id);
        if (incomingConnection == null)
        {
            // No link yet: node acts as a pure matrix holder.
            context.Set($"RigidTransformMatrix_{Id}", m);
            return Task.CompletedTask;
        }

        // 2) Resolve upstream source strictly through the connection chain.
        var sourceNodeId = incomingConnection.SourceNodeId;
        var sourceNode = _graph.Nodes.FirstOrDefault(n => n.Id == sourceNodeId);
        PointCloudData? source = null;

        if (sourceNode != null)
        {
            if (sourceNode.Name == "Import Point Cloud")
            {
                // Import node stores cloud per-node key.
                source = context.Get<PointCloudData>($"{ExecutionContext.PointCloudKey}_{sourceNodeId}");
            }
            else if (sourceNode.Name == "Rigid Transform")
            {
                // Upstream transform's output.
                source = context.Get<PointCloudData>($"TransformedCloud_{sourceNodeId}");
            }
            else
            {
                // Other node types are ignored for transform input to prevent unintended duplication.
                source = null;
            }
        }

        if (source == null || source.Points.Count == 0)
        {
            // Connection exists but no data available - this is an error
            var sourceNodeName = sourceNode?.Name ?? "Unknown";
            throw new InvalidOperationException(
                $"Rigid Transform: No point cloud data from upstream node '{sourceNodeName}' (ID: {sourceNodeId}). " +
                $"Ensure the upstream node executed successfully and produced point cloud data."
            );
        }

        // 3) Transform only the linked source.
        var transformed = Transform(source, m);
        context.Set($"TransformedCloud_{Id}", transformed);
        context.Set($"RigidTransformMatrix_{Id}", m);
        return Task.CompletedTask;
    }

    private static PointCloudData Transform(PointCloudData source, Matrix4x4 m)
    {
        // In-place clone transform (no global mutation).
        var result = source.Clone();
        for (int i = 0; i < result.Points.Count; i++)
        {
            var p = result.Points[i];
            var x = m.M11 * p.X + m.M12 * p.Y + m.M13 * p.Z + m.M14;
            var y = m.M21 * p.X + m.M22 * p.Y + m.M23 * p.Z + m.M24;
            var z = m.M31 * p.X + m.M32 * p.Y + m.M33 * p.Z + m.M34;
            result.Points[i] = new Vector3(x, y, z);
        }
        result.ComputeBoundingBox();
        return result;
    }
}
