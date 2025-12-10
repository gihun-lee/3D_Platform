using VPP.Core.Models;

namespace VPP.Core.Interfaces;

/// <summary>
/// Optional interface for nodes that need access to the full NodeGraph
/// (e.g. to inspect upstream connections). Implement this in plugin nodes
/// without creating a reverse dependency from Core to plugins.
/// </summary>
public interface IGraphAwareNode
{
    void SetGraph(NodeGraph graph);
}
