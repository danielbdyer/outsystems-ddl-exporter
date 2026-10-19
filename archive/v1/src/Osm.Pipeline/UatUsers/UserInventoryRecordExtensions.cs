using System;

namespace Osm.Pipeline.UatUsers;

internal static class UserInventoryRecordExtensions
{
    public static string? GetAttributeValue(this UserInventoryRecord record, string? attribute)
    {
        if (record is null || string.IsNullOrWhiteSpace(attribute))
        {
            return null;
        }

        var normalized = Normalize(attribute);
        return normalized switch
        {
            "username" => record.Username,
            "email" => record.Email,
            "name" => record.Name,
            "externalid" => record.ExternalId,
            "isactive" => record.IsActive,
            "creationdate" => record.CreationDate,
            "lastlogin" => record.LastLogin,
            _ => null
        };
    }

    private static string Normalize(string attribute)
    {
        if (attribute.Length == 0)
        {
            return string.Empty;
        }

        var buffer = new char[attribute.Length];
        var index = 0;
        foreach (var ch in attribute)
        {
            if (char.IsWhiteSpace(ch) || ch == '_' || ch == '-')
            {
                continue;
            }

            if (index >= buffer.Length)
            {
                Array.Resize(ref buffer, buffer.Length * 2);
            }

            buffer[index++] = char.ToLowerInvariant(ch);
        }

        return new string(buffer, 0, index);
    }
}
