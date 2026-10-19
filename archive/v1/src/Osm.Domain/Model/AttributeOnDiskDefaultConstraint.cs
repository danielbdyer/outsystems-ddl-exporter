namespace Osm.Domain.Model;

public sealed record AttributeOnDiskDefaultConstraint(string? Name, string Definition, bool IsNotTrusted)
{
    public static AttributeOnDiskDefaultConstraint? Create(string? name, string? definition, bool? isNotTrusted = null)
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            return null;
        }

        var trimmedName = string.IsNullOrWhiteSpace(name) ? null : name!.Trim();
        var normalizedDefinition = definition!.Trim();
        var trusted = isNotTrusted ?? false;

        return new AttributeOnDiskDefaultConstraint(trimmedName, normalizedDefinition, trusted);
    }
}
