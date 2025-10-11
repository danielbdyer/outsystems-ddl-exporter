namespace Osm.Domain.Model;

public sealed record AttributeOnDiskCheckConstraint(string? Name, string Definition, bool IsNotTrusted)
{
    public static AttributeOnDiskCheckConstraint? Create(string? name, string? definition, bool? isNotTrusted = null)
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            return null;
        }

        var trimmedName = string.IsNullOrWhiteSpace(name) ? null : name!.Trim();
        var normalizedDefinition = definition!.Trim();
        var trusted = isNotTrusted ?? false;

        return new AttributeOnDiskCheckConstraint(trimmedName, normalizedDefinition, trusted);
    }
}
