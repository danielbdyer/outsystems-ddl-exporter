using System.Text;

namespace Osm.Json.Deserialization;

internal readonly record struct DocumentPathContext(string Value)
{
    public static DocumentPathContext Root { get; } = new("$");

    public DocumentPathContext Property(string name)
        => new($"{Value}[{Quote(name)}]");

    public DocumentPathContext Element(string name, int index)
        => Property(name).Index(index);

    public DocumentPathContext Index(int index)
        => new($"{Value}[{index}]");

    public override string ToString() => Value;

    private static string Quote(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "''";
        }

        var builder = new StringBuilder(name.Length + 2);
        builder.Append('\'');
        foreach (var ch in name)
        {
            builder.Append(ch == '\'' ? "\\'" : ch);
        }

        builder.Append('\'');
        return builder.ToString();
    }
}
