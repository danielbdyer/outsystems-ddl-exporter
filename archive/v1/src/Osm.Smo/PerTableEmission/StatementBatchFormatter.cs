using System;
using System.Collections.Generic;
using System.Text;

namespace Osm.Smo.PerTableEmission;

internal sealed class StatementBatchFormatter
{
    public string NormalizeWhitespace(string script)
    {
        if (script is null)
        {
            throw new ArgumentNullException(nameof(script));
        }

        var lines = script.Split(Environment.NewLine);
        var builder = new StringBuilder(script.Length);

        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            builder.Append(lines[i].TrimEnd());
        }

        return builder.ToString();
    }

    public string JoinStatements(IReadOnlyList<string> statements, SmoFormatOptions format)
    {
        if (statements is null)
        {
            throw new ArgumentNullException(nameof(statements));
        }

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        var builder = new StringBuilder();
        for (var i = 0; i < statements.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
                builder.AppendLine("GO");
                builder.AppendLine();
            }

            builder.AppendLine(statements[i]);
        }

        var script = builder.ToString().TrimEnd();
        return format.NormalizeWhitespace ? NormalizeWhitespace(script) : script;
    }
}
