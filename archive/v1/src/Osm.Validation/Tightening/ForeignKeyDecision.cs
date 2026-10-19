using System.Collections.Immutable;

namespace Osm.Validation.Tightening;

public sealed record ForeignKeyDecision(
    ColumnCoordinate Column,
    bool CreateConstraint,
    bool ScriptWithNoCheck,
    ImmutableArray<string> Rationales)
{
    public static ForeignKeyDecision Create(
        ColumnCoordinate column,
        bool createConstraint,
        bool scriptWithNoCheck,
        ImmutableArray<string> rationales)
    {
        if (rationales.IsDefault)
        {
            rationales = ImmutableArray<string>.Empty;
        }

        return new ForeignKeyDecision(column, createConstraint, scriptWithNoCheck, rationales);
    }
}
