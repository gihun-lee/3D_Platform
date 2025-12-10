namespace VPP.Core.Interfaces;

public interface IPlugin
{
    string Id { get; }
    string Name { get; }
    string Version { get; }
    string Author { get; }
    string Description { get; }

    IReadOnlyList<Type> NodeTypes { get; }

    void Initialize();
    void Shutdown();
}
