using VPP.Core.Interfaces;

namespace VPP.Core.Models;

public class NodeGraph
{
    public string Id { get; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Untitled";
    public string Version { get; set; } = "1.0";

    private readonly List<INode> _nodes = new();
    private readonly List<Connection> _connections = new();

    public IReadOnlyList<INode> Nodes => _nodes;
    public IReadOnlyList<Connection> Connections => _connections;

    public void AddNode(INode node) => _nodes.Add(node);

    public void RemoveNode(INode node)
    {
        _connections.RemoveAll(c => c.SourceNodeId == node.Id || c.TargetNodeId == node.Id);
        _nodes.Remove(node);
    }

    /// <summary>
    /// Create a simple execution order connection between two nodes.
    /// No port validation needed - just ensures execution order.
    /// </summary>
    public bool Connect(INode sourceNode, INode targetNode)
    {
        if (WouldCreateCycle(sourceNode, targetNode))
            return false;

        var conn = new Connection
        {
            SourceNodeId = sourceNode.Id,
            TargetNodeId = targetNode.Id
        };
        _connections.Add(conn);
        return true;
    }

    #region Backward Compatibility - Port-based connections

    [Obsolete("Use Connect(sourceNode, targetNode) instead. Port-based connections are deprecated.")]
    public bool Connect(INode sourceNode, IPort sourcePort, INode targetNode, IPort targetPort)
    {
        if (sourcePort.Direction != PortDirection.Output || targetPort.Direction != PortDirection.Input)
            return false;
        if (!targetPort.DataType.IsAssignableFrom(sourcePort.DataType))
            return false;
        if (WouldCreateCycle(sourceNode, targetNode))
            return false;

        var conn = new Connection
        {
            SourceNodeId = sourceNode.Id,
            SourcePortId = sourcePort.Id,
            TargetNodeId = targetNode.Id,
            TargetPortId = targetPort.Id
        };
        _connections.Add(conn);
        ((Port)sourcePort).IsConnected = true;
        ((Port)targetPort).IsConnected = true;
        return true;
    }

    #endregion

    public void Disconnect(Connection connection)
    {
        _connections.Remove(connection);
    }

    private bool WouldCreateCycle(INode source, INode target)
    {
        var visited = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(source.Id);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == target.Id) return true;
            if (!visited.Add(current)) continue;

            foreach (var conn in _connections.Where(c => c.TargetNodeId == current))
                queue.Enqueue(conn.SourceNodeId);
        }
        return false;
    }

    public IEnumerable<INode> GetExecutionOrder()
    {
        var inDegree = _nodes.ToDictionary(n => n.Id, _ => 0);
        foreach (var conn in _connections)
            inDegree[conn.TargetNodeId]++;

        var queue = new Queue<INode>(_nodes.Where(n => inDegree[n.Id] == 0));
        var result = new List<INode>();

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            result.Add(node);
            foreach (var conn in _connections.Where(c => c.SourceNodeId == node.Id))
            {
                if (--inDegree[conn.TargetNodeId] == 0)
                    queue.Enqueue(_nodes.First(n => n.Id == conn.TargetNodeId));
            }
        }
        return result;
    }
}
