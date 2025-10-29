using System.Collections.Generic;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class ModuleJsonResultSetProcessor : ResultSetProcessor<OutsystemsModuleJsonRow>
{
    private static readonly ResultSetReader<OutsystemsModuleJsonRow> Reader = ResultSetReader<OutsystemsModuleJsonRow>.Create(MapRow);

    private static readonly ColumnDefinition<string> ModuleName = Column.String(0, "ModuleName");
    private static readonly ColumnDefinition<bool> IsSystem = Column.Boolean(1, "IsSystem");
    private static readonly ColumnDefinition<bool> IsActive = Column.Boolean(2, "IsActive");
    private static readonly ColumnDefinition<string> ModuleEntitiesJson = Column.String(3, "ModuleEntitiesJson");

    public ModuleJsonResultSetProcessor()
        : base("ModuleJson", order: 22)
    {
    }

    protected override ResultSetReader<OutsystemsModuleJsonRow> CreateReader(ResultSetProcessingContext context) => Reader;

    protected override void Assign(MetadataAccumulator accumulator, List<OutsystemsModuleJsonRow> rows)
        => accumulator.SetModuleJson(rows);

    private static OutsystemsModuleJsonRow MapRow(DbRow row) => new(
        ModuleName.Read(row),
        IsSystem.Read(row),
        IsActive.Read(row),
        ModuleEntitiesJson.Read(row));
}
