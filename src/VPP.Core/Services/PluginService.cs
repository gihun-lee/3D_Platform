using System.Reflection;
using VPP.Core.Interfaces;
using VPP.Core.Attributes;

namespace VPP.Core.Services;

public class PluginService
{
    private readonly List<IPlugin> _plugins = new();
    private readonly Dictionary<string, Type> _nodeTypes = new();

    public IReadOnlyList<IPlugin> Plugins => _plugins;
    public IReadOnlyDictionary<string, Type> NodeTypes => _nodeTypes;

    public void LoadPlugins(string pluginPath)
    {
        if (!Directory.Exists(pluginPath)) return;

        foreach (var dll in Directory.GetFiles(pluginPath, "*.dll"))
        {
            try
            {
                var assembly = Assembly.LoadFrom(dll);
                LoadFromAssembly(assembly);
            }
            catch { /* Log and continue */ }
        }
    }

    public void LoadFromAssembly(Assembly assembly)
    {
        // Find plugin classes
        var pluginTypes = assembly.GetTypes()
            .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        foreach (var pluginType in pluginTypes)
        {
            var plugin = (IPlugin)Activator.CreateInstance(pluginType)!;
            plugin.Initialize();
            _plugins.Add(plugin);

            foreach (var nodeType in plugin.NodeTypes)
            {
                var attr = nodeType.GetCustomAttribute<NodeInfoAttribute>();
                var key = attr?.Name ?? nodeType.Name;
                _nodeTypes[key] = nodeType;
            }
        }
    }

    public INode? CreateNode(string typeName)
    {
        if (_nodeTypes.TryGetValue(typeName, out var type))
            return (INode?)Activator.CreateInstance(type);
        return null;
    }

    public IEnumerable<(string Name, string Category, Type Type)> GetAvailableNodes()
    {
        foreach (var (name, type) in _nodeTypes)
        {
            var attr = type.GetCustomAttribute<NodeInfoAttribute>();
            yield return (name, attr?.Category ?? "General", type);
        }
    }
}
