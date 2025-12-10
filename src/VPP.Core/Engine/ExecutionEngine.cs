using VPP.Core.Interfaces;
using VPP.Core.Models;
using ExecCtx = VPP.Core.Models.ExecutionContext;

namespace VPP.Core.Engine;

/// <summary>
/// Execution engine for node graphs.
/// Creates a shared ExecutionContext and executes nodes in topological order.
/// </summary>
public class ExecutionEngine
{
    public event EventHandler<NodeExecutionEventArgs>? NodeExecuting;
    public event EventHandler<NodeExecutionEventArgs>? NodeExecuted;
    public event EventHandler<ExecutionCompletedEventArgs>? ExecutionCompleted;

    /// <summary>
    /// Execute the entire node graph with a shared ExecutionContext.
    /// </summary>
    public async Task<ExecutionResult> ExecuteAsync(NodeGraph graph, CancellationToken cancellationToken = default)
    {
        var context = new ExecCtx();
        var results = new Dictionary<string, NodeResult>();
        var executionOrder = graph.GetExecutionOrder().ToList();

        // Store graph reference in context for nodes that need to query connections
        context.Set("__NodeGraph__", graph);

        foreach (var node in executionOrder)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (node is IGraphAwareNode graphAware)
            {
                graphAware.SetGraph(graph);
            }

            NodeExecuting?.Invoke(this, new NodeExecutionEventArgs(node));
            var result = await node.ExecuteAsync(context, cancellationToken);
            results[node.Id] = result;
            NodeExecuted?.Invoke(this, new NodeExecutionEventArgs(node, result));

            if (!result.Success)
            {
                var execResult = new ExecutionResult { Success = false, NodeResults = results, Context = context };
                ExecutionCompleted?.Invoke(this, new ExecutionCompletedEventArgs(execResult));
                return execResult;
            }
        }

        var finalResult = new ExecutionResult { Success = true, NodeResults = results, Context = context };
        ExecutionCompleted?.Invoke(this, new ExecutionCompletedEventArgs(finalResult));
        return finalResult;
    }
}

public class ExecutionResult
{
    public bool Success { get; init; }
    public Dictionary<string, NodeResult> NodeResults { get; init; } = new();
    public ExecCtx? Context { get; init; }
}

public class NodeExecutionEventArgs : EventArgs
{
    public INode Node { get; }
    public NodeResult? Result { get; }
    public NodeExecutionEventArgs(INode node, NodeResult? result = null)
    {
        Node = node;
        Result = result;
    }
}

public class ExecutionCompletedEventArgs : EventArgs
{
    public ExecutionResult Result { get; }
    public ExecutionCompletedEventArgs(ExecutionResult result) => Result = result;
}
