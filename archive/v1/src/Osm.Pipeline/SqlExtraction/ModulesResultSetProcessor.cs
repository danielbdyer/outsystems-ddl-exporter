using System;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class ModulesResultSetProcessor : ResultSetProcessor<OutsystemsModuleRow>
{
    private static readonly ColumnDefinition<int> EspaceId = Column.Int32(0, "EspaceId");
    private static readonly ColumnDefinition<string> EspaceName = Column.String(1, "EspaceName");
    private static readonly ColumnDefinition<bool> IsSystemModule = Column.Boolean(2, "IsSystemModule");
    private static readonly ColumnDefinition<bool> ModuleIsActive = Column.Boolean(3, "ModuleIsActive");
    private static readonly ColumnDefinition<string?> EspaceKind = Column.StringOrNull(4, "EspaceKind");
    private static readonly ColumnDefinition<Guid?> EspaceSsKey = Column.GuidOrNull(5, "EspaceSSKey");

    internal static ResultSetDescriptor<OutsystemsModuleRow> Descriptor { get; } = ResultSetDescriptorFactory.Create<OutsystemsModuleRow>(
        "Modules",
        order: 0,
        builder => builder
            .Columns(
                EspaceId,
                EspaceName,
                IsSystemModule,
                ModuleIsActive,
                EspaceKind,
                EspaceSsKey)
            .Map(MapRow)
            .Assign(static (accumulator, rows) => accumulator.SetModules(rows)));

    public ModulesResultSetProcessor()
        : base(Descriptor)
    {
    }

    private static OutsystemsModuleRow MapRow(DbRow row) => new(
        EspaceId.Read(row),
        EspaceName.Read(row),
        IsSystemModule.Read(row),
        ModuleIsActive.Read(row),
        EspaceKind.Read(row),
        EspaceSsKey.Read(row));
}
