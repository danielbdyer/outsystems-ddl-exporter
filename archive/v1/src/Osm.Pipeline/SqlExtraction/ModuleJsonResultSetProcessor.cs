
namespace Osm.Pipeline.SqlExtraction;

internal sealed class ModuleJsonResultSetProcessor : ResultSetProcessor<OutsystemsModuleJsonRow>
{
    private static readonly ColumnDefinition<string> ModuleName = Column.String(0, "ModuleName");
    private static readonly ColumnDefinition<bool> IsSystem = Column.Boolean(1, "IsSystem");
    private static readonly ColumnDefinition<bool> IsActive = Column.Boolean(2, "IsActive");
    private static readonly ColumnDefinition<string> ModuleEntitiesJson = Column.String(3, "ModuleEntitiesJson");

    internal static ResultSetDescriptor<OutsystemsModuleJsonRow> Descriptor { get; } = ResultSetDescriptorFactory.Create<OutsystemsModuleJsonRow>(
        "ModuleJson",
        order: 22,
        builder => builder
            .Columns(ModuleName, IsSystem, IsActive, ModuleEntitiesJson)
            .Map(MapRow)
            .Assign(static (accumulator, rows) => accumulator.SetModuleJson(rows)));

    public ModuleJsonResultSetProcessor()
        : base(Descriptor)
    {
    }

    private static OutsystemsModuleJsonRow MapRow(DbRow row) => new(
        ModuleName.Read(row),
        IsSystem.Read(row),
        IsActive.Read(row),
        ModuleEntitiesJson.Read(row));
}
