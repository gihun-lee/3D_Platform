namespace VPP.Core.Interfaces;

public interface INode
{
    string Id { get; }
    string Name { get; }
    string Category { get; }
    string Description { get; }

    // Parameters that can be edited directly in the UI
    IReadOnlyList<IParameter> Parameters { get; }

    // Execute with shared context
    Task<NodeResult> ExecuteAsync(Models.ExecutionContext context, CancellationToken cancellationToken = default);
    bool Validate(out string[] errors);
    void Reset();
}

public interface IParameter
{
    string Name { get; }
    Type Type { get; }
    object? Value { get; set; }
    object? DefaultValue { get; }
    bool IsRequired { get; }
    string? DisplayName { get; }
    string? Description { get; }
}

public class NodeResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan ExecutionTime { get; init; }

    public static NodeResult Ok() => new() { Success = true };
    public static NodeResult Fail(string error) => new() { Success = false, ErrorMessage = error };
}

// Keep for backward compatibility during migration (will be removed)
[Obsolete("Port system is deprecated. Use Parameters instead.")]
public interface IPort
{
    string Id { get; }
    string Name { get; }
    Type DataType { get; }
    PortDirection Direction { get; }
    bool IsConnected { get; }
    object? Value { get; set; }
    object? DefaultValue { get; }
    bool IsRequired { get; }
}

[Obsolete("Port system is deprecated.")]
public enum PortDirection
{
    Input,
    Output
}
