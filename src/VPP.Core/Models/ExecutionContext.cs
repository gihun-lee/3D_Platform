namespace VPP.Core.Models;

/// <summary>
/// Execution context shared between nodes during graph execution.
/// Provides implicit data sharing without explicit port connections.
/// Uses generic dictionary to avoid dependency on specific plugin types.
/// </summary>
public class ExecutionContext
{
    private readonly Dictionary<string, object?> _data = new();

    // Common well-known keys for standard data types
    public const string PointCloudKey = "PointCloud";
    public const string FilteredCloudKey = "FilteredCloud";
    public const string ROIKey = "ROI";
    public const string CircleResultKey = "CircleResult";

    // Generic data storage
    public void Set<T>(string key, T value) => _data[key] = value;

    public T? Get<T>(string key) => _data.TryGetValue(key, out var value) && value is T typed ? typed : default;

    public bool TryGet<T>(string key, out T? value)
    {
        if (_data.TryGetValue(key, out var obj) && obj is T typed)
        {
            value = typed;
            return true;
        }
        value = default;
        return false;
    }

    // Shorthand property access for common keys
    public object? this[string key]
    {
        get => _data.TryGetValue(key, out var value) ? value : null;
        set => _data[key] = value;
    }

    public void Clear() => _data.Clear();
    public bool Contains(string key) => _data.ContainsKey(key);
    public IEnumerable<string> Keys => _data.Keys;
}
