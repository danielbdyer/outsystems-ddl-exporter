using System.Text.Json;

namespace Osm.Validation.Tightening;

public static class PolicyDecisionReportJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static JsonSerializerOptions GetSerializerOptions() => SerializerOptions;
}
