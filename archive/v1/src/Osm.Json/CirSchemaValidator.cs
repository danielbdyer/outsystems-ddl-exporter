using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

using Json.Schema;

using Osm.Domain.Abstractions;

namespace Osm.Json;

internal static class CirSchemaValidator
{
    private static readonly Lazy<JsonSchema> Schema = new(LoadSchema);

    public static Result<bool> Validate(JsonElement root)
    {
        var evaluation = Schema.Value.Evaluate(root, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List
        });

        if (evaluation.IsValid)
        {
            return true;
        }

        var errors = CollectErrors(evaluation).ToList();
        if (errors.Count == 0)
        {
            errors.Add(ValidationError.Create("json.schema.validation", "Payload does not conform to the canonical schema."));
        }

        return Result<bool>.Failure(errors);
    }

    private static IEnumerable<ValidationError> CollectErrors(EvaluationResults evaluation)
    {
        if (evaluation.Errors is { Count: > 0 })
        {
            foreach (var error in evaluation.Errors)
            {
                var location = evaluation.InstanceLocation.ToString();
                var message = string.IsNullOrWhiteSpace(location)
                    ? error.Value
                    : $"{location}: {error.Value}";
                yield return ValidationError.Create("json.schema.validation", message);
            }
        }

        if (evaluation.Details is { Count: > 0 })
        {
            foreach (var detail in evaluation.Details)
            {
                foreach (var error in CollectErrors(detail))
                {
                    yield return error;
                }
            }
        }
    }

    private static JsonSchema LoadSchema()
    {
        var assembly = typeof(CirSchemaValidator).GetTypeInfo().Assembly;
        using var stream = assembly.GetManifestResourceStream("Osm.Json.Schema.cir-v1.json");
        if (stream is null)
        {
            throw new InvalidOperationException("Embedded CIR schema 'cir-v1.json' could not be found.");
        }

        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        return JsonSchema.FromText(text);
    }
}
