using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.Dac;

namespace DbChangeLite;

public sealed record PublishProfile(string Path, string Hash, DacDeployOptions Options, IReadOnlyDictionary<string, string> SqlCmd);

public static class PublishProfiles
{
    public static PublishProfile Load(string root, string relativePath)
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relativePath));
        if (!File.Exists(path))
        {
            throw Stop.Of("configuration-refused", "profile.missing", relativePath, "The publish profile " + relativePath + " does not exist.",
                "Commit the deployment step's profile at " + relativePath + ", its connection string removed.");
        }

        var bytes = File.ReadAllBytes(path);
        var text = Fingerprint.Canonical(bytes);
        if (text.Contains("password=", StringComparison.OrdinalIgnoreCase) || text.Contains("pwd=", StringComparison.OrdinalIgnoreCase))
        {
            throw Stop.Of("configuration-refused", "profile.credential", relativePath, "The publish profile " + relativePath + " holds a password.",
                "Remove the connection string from the profile; the environments file references the connection.");
        }

        DacProfile profile;
        try
        {
            profile = DacProfile.Load(path);
        }
        catch (Exception e) when (e is DacServicesException or System.Xml.XmlException or InvalidOperationException)
        {
            throw Stop.Of("unparsed-input", "profile.unreadable", relativePath, "DacFx does not read " + relativePath + " as a publish profile: " + e.Message,
                "Correct the profile, or export it again from the deployment step.");
        }

        var options = profile.DeployOptions;
        var sqlcmd = options.SqlCommandVariableValues.ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
        return new PublishProfile(relativePath.Replace('\\', '/'), Fingerprint.OfText(bytes), options, sqlcmd);
    }

    public static DacDeployOptions ForProof(DacDeployOptions options)
    {
        var proof = Copy(options);
        proof.BlockOnPossibleDataLoss = true;
        return proof;
    }

    public static DacDeployOptions Copy(DacDeployOptions options)
    {
        var copy = new DacDeployOptions();
        foreach (var property in typeof(DacDeployOptions).GetProperties().Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0))
        {
            property.SetValue(copy, property.GetValue(options));
        }

        foreach (var value in options.SqlCommandVariableValues)
        {
            copy.SqlCommandVariableValues[value.Key] = value.Value;
        }

        return copy;
    }
}
