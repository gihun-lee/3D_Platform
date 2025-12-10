namespace VPP.Core.Models;

/// <summary>
/// Simplified connection representing execution order only.
/// No longer transfers data between ports - data is shared via ExecutionContext.
/// </summary>
public class Connection
{
    public string Id { get; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The node that executes first.
    /// </summary>
    public string SourceNodeId { get; init; } = "";

    /// <summary>
    /// The node that executes after the source node completes.
    /// </summary>
    public string TargetNodeId { get; init; } = "";

    #region Backward Compatibility (Deprecated)
    // Kept for backward compatibility during migration
    // Port connections are no longer used but kept to avoid breaking existing code

    [Obsolete("Port-based connections are deprecated. Connection now represents execution order only.")]
    public string SourcePortId { get; init; } = "";

    [Obsolete("Port-based connections are deprecated. Connection now represents execution order only.")]
    public string TargetPortId { get; init; } = "";

    #endregion
}
