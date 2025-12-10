namespace VPP.Core.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class NodeInfoAttribute : Attribute
{
    public string Name { get; }
    public string Category { get; }
    public string Description { get; }
    public string Icon { get; set; } = "";

    public NodeInfoAttribute(string name, string category, string description = "")
    {
        Name = name;
        Category = category;
        Description = description;
    }
}
