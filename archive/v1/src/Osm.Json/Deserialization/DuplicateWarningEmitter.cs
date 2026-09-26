using System;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;

namespace Osm.Json.Deserialization;

internal sealed class DuplicateWarningEmitter : IDuplicateWarningEmitter
{
    private readonly DocumentMapperContext _context;

    public DuplicateWarningEmitter(DocumentMapperContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public EntityDocumentMapper.HelperResult<EntityDocumentMapper.DuplicateAllowance> EmitWarnings(
        EntityDocumentMapper.MapContext mapContext,
        ImmutableArray<AttributeModel> attributes)
    {
        var options = _context.Options;
        var moduleName = mapContext.ModuleNameValue;
        var entityName = mapContext.EntityNameValue;

        if (options.AllowDuplicateAttributeLogicalNames)
        {
            var duplicateLogicalGroups = attributes
                .GroupBy(static attribute => attribute.LogicalName.Value, StringComparer.Ordinal)
                .Where(static group => group.Count() > 1)
                .Select(static group => new
                {
                    Key = group.Key,
                    Columns = group.Select(static attribute => attribute.ColumnName.Value).ToArray()
                })
                .ToArray();

            if (duplicateLogicalGroups.Length > 0)
            {
                mapContext = mapContext.EnsureSerializedPayload(_context);
                foreach (var group in duplicateLogicalGroups)
                {
                    var columnList = string.Join(", ", group.Columns.Select(static name => $"'{name}'"));
                    _context.AddWarning(
                        $"Entity '{moduleName}::{entityName}' contains duplicate attribute logical name '{group.Key}' mapped to columns {columnList}. Raw payload: {mapContext.SerializedPayload} (Path: {mapContext.AttributesPath})");
                }
            }
        }

        if (options.AllowDuplicateAttributeColumnNames)
        {
            var duplicateColumnGroups = attributes
                .GroupBy(static attribute => attribute.ColumnName.Value, StringComparer.OrdinalIgnoreCase)
                .Where(static group => group.Count() > 1)
                .Select(static group => new
                {
                    Key = group.Key,
                    LogicalNames = group.Select(static attribute => attribute.LogicalName.Value).ToArray()
                })
                .ToArray();

            if (duplicateColumnGroups.Length > 0)
            {
                mapContext = mapContext.EnsureSerializedPayload(_context);
                foreach (var group in duplicateColumnGroups)
                {
                    var attributeList = string.Join(", ", group.LogicalNames.Select(static name => $"'{name}'"));
                    _context.AddWarning(
                        $"Entity '{moduleName}::{entityName}' contains duplicate attribute column name '{group.Key}' shared by attributes {attributeList}. Raw payload: {mapContext.SerializedPayload} (Path: {mapContext.AttributesPath})");
                }
            }
        }

        var allowance = new EntityDocumentMapper.DuplicateAllowance(
            options.AllowDuplicateAttributeLogicalNames,
            options.AllowDuplicateAttributeColumnNames);
        return EntityDocumentMapper.HelperResult<EntityDocumentMapper.DuplicateAllowance>.Success(mapContext, allowance);
    }
}
