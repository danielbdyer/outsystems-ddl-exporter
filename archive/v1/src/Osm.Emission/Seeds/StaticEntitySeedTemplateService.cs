using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Osm.Emission.Seeds;

public sealed class StaticEntitySeedTemplateService
{
    private const string Placeholder = "{{STATIC_ENTITY_BLOCKS}}";
    private readonly Lazy<string> _templateContent;

    public StaticEntitySeedTemplateService()
        : this(LoadTemplate)
    {
    }

    internal StaticEntitySeedTemplateService(Func<string> templateLoader)
    {
        if (templateLoader is null)
        {
            throw new ArgumentNullException(nameof(templateLoader));
        }

        _templateContent = new Lazy<string>(() =>
        {
            var content = templateLoader();
            if (string.IsNullOrWhiteSpace(content) || !content.Contains(Placeholder, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Static entity seed template is missing the expected placeholder block.");
            }

            return content;
        });
    }

    public string ApplyBlocks(string blocks)
    {
        if (blocks is null)
        {
            throw new ArgumentNullException(nameof(blocks));
        }

        return _templateContent.Value.Replace(Placeholder, blocks, StringComparison.Ordinal);
    }

    public string GetTemplate() => _templateContent.Value;

    private static string LoadTemplate()
    {
        var assembly = typeof(StaticEntitySeedTemplateService).GetTypeInfo().Assembly;
        const string resourceName = "Osm.Emission.Templates.StaticEntitySeedTemplate.sql";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded static entity seed template '{resourceName}' was not found.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: false);
        return reader.ReadToEnd();
    }
}
