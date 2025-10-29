using System;
using System.Collections.Generic;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class ModulesResultSetProcessor : ResultSetProcessor<OutsystemsModuleRow>
{
    private static readonly ResultSetReader<OutsystemsModuleRow> Reader = ResultSetReader<OutsystemsModuleRow>.Create(MapRow);

    private static readonly ColumnDefinition<int> EspaceId = Column.Int32(0, "EspaceId");
    private static readonly ColumnDefinition<string> EspaceName = Column.String(1, "EspaceName");
    private static readonly ColumnDefinition<bool> IsSystemModule = Column.Boolean(2, "IsSystemModule");
    private static readonly ColumnDefinition<bool> ModuleIsActive = Column.Boolean(3, "ModuleIsActive");
    private static readonly ColumnDefinition<string?> EspaceKind = Column.StringOrNull(4, "EspaceKind");
    private static readonly ColumnDefinition<Guid?> EspaceSsKey = Column.GuidOrNull(5, "EspaceSSKey");

    public ModulesResultSetProcessor()
        : base("Modules", order: 0)
    {
    }

    protected override ResultSetReader<OutsystemsModuleRow> CreateReader(ResultSetProcessingContext context) => Reader;

    protected override void Assign(MetadataAccumulator accumulator, List<OutsystemsModuleRow> rows)
        => accumulator.SetModules(rows);

    private static OutsystemsModuleRow MapRow(DbRow row) => new(
        EspaceId.Read(row),
        EspaceName.Read(row),
        IsSystemModule.Read(row),
        ModuleIsActive.Read(row),
        EspaceKind.Read(row),
        EspaceSsKey.Read(row));
}
