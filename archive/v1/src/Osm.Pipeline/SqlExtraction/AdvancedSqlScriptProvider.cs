using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Osm.Pipeline.SqlExtraction;

public interface IAdvancedSqlScriptProvider
{
    string GetScript();
}

public sealed class EmbeddedAdvancedSqlScriptProvider : IAdvancedSqlScriptProvider
{
    private const string ResourceName = "Osm.Pipeline.AdvancedSql.outsystems_model_export.sql";
    private readonly Lazy<string> _script;

    public EmbeddedAdvancedSqlScriptProvider()
    {
        _script = new Lazy<string>(LoadScript, isThreadSafe: true);
    }

    public string GetScript() => _script.Value;

    private static string LoadScript()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: false);
        return reader.ReadToEnd();
    }
}
