using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;

namespace Osm.Json.Deserialization;

internal sealed class AttributeDeduplicator : IAttributeDeduplicator
{
    private readonly DocumentMapperContext _context;

    public AttributeDeduplicator(DocumentMapperContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public EntityDocumentMapper.HelperResult<ImmutableArray<AttributeModel>> Deduplicate(
        EntityDocumentMapper.MapContext mapContext,
        DocumentPathContext attributesPath,
        ImmutableArray<AttributeModel> attributes,
        ModelJsonDeserializer.AttributeDocument[] sourceDocuments)
    {
        if (sourceDocuments is null)
        {
            throw new ArgumentNullException(nameof(sourceDocuments));
        }

        if (attributes.Length != sourceDocuments.Length)
        {
            throw new ArgumentException(
                "Attribute model count does not match source document count.",
                nameof(sourceDocuments));
        }

        if (attributes.Length == 0)
        {
            return EntityDocumentMapper.HelperResult<ImmutableArray<AttributeModel>>.Success(mapContext, attributes);
        }

        var entries = new AttributeEntry[attributes.Length];
        for (var i = 0; i < attributes.Length; i++)
        {
            entries[i] = new AttributeEntry(attributes[i], sourceDocuments[i]);
        }

        var removed = new HashSet<int>();
        var context = mapContext;

        var error = ProcessDuplicates(
            ref context,
            entries,
            static entry => entry.Model.LogicalName.Value,
            StringComparer.Ordinal,
            "logical name",
            attributesPath,
            removed);
        if (error is not null)
        {
            return EntityDocumentMapper.HelperResult<ImmutableArray<AttributeModel>>.Failure(context, error.Value);
        }

        error = ProcessDuplicates(
            ref context,
            entries,
            static entry => entry.Model.ColumnName.Value,
            StringComparer.OrdinalIgnoreCase,
            "column name",
            attributesPath,
            removed);
        if (error is not null)
        {
            return EntityDocumentMapper.HelperResult<ImmutableArray<AttributeModel>>.Failure(context, error.Value);
        }

        if (removed.Count == 0)
        {
            return EntityDocumentMapper.HelperResult<ImmutableArray<AttributeModel>>.Success(context, attributes);
        }

        var builder = ImmutableArray.CreateBuilder<AttributeModel>(attributes.Length - removed.Count);
        for (var i = 0; i < entries.Length; i++)
        {
            if (removed.Contains(i))
            {
                continue;
            }

            builder.Add(entries[i].Model);
        }

        return EntityDocumentMapper.HelperResult<ImmutableArray<AttributeModel>>.Success(context, builder.ToImmutable());
    }

    private ValidationError? ProcessDuplicates(
        ref EntityDocumentMapper.MapContext mapContext,
        AttributeEntry[] entries,
        Func<AttributeEntry, string> keySelector,
        IEqualityComparer<string> comparer,
        string dimension,
        DocumentPathContext attributesPath,
        HashSet<int> removedIndices)
    {
        var moduleName = mapContext.ModuleNameValue;
        var entityName = mapContext.EntityNameValue;

        var groups = entries
            .Select((entry, index) => (Entry: entry, Index: index))
            .Where(item => !removedIndices.Contains(item.Index))
            .GroupBy(item => keySelector(item.Entry), comparer);

        foreach (var group in groups)
        {
            var candidateIndices = group
                .Select(item => item.Index)
                .Where(index => !removedIndices.Contains(index))
                .ToArray();

            if (candidateIndices.Length <= 1)
            {
                continue;
            }

            var result = ResolveGroup(
                ref mapContext,
                entries,
                candidateIndices,
                dimension,
                group.Key,
                attributesPath,
                moduleName,
                entityName,
                removedIndices);

            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private ValidationError? ResolveGroup(
        ref EntityDocumentMapper.MapContext mapContext,
        AttributeEntry[] entries,
        int[] candidateIndices,
        string dimension,
        string key,
        DocumentPathContext attributesPath,
        string moduleName,
        string entityName,
        HashSet<int> removedIndices)
    {
        var activeIndices = new List<int>();
        var inactiveIndices = new List<int>();
        var unknownCount = 0;

        foreach (var index in candidateIndices)
        {
            var status = entries[index].Document.ReferenceEntityIsActive;
            if (status is true)
            {
                activeIndices.Add(index);
            }
            else if (status is false)
            {
                inactiveIndices.Add(index);
            }
            else
            {
                unknownCount++;
            }
        }

        if (activeIndices.Count == 1 && inactiveIndices.Count == candidateIndices.Length - 1 && unknownCount == 0)
        {
            var keepIndex = activeIndices[0];
            foreach (var index in candidateIndices)
            {
                if (index != keepIndex)
                {
                    removedIndices.Add(index);
                }
            }

            mapContext = mapContext.EnsureSerializedPayload(_context);
            var keptDescription = DescribeEntry(entries[keepIndex]);
            var removedDescription = string.Join(
                ", ",
                candidateIndices
                    .Where(index => index != keepIndex)
                    .Select(index => DescribeEntry(entries[index])));

            _context.AddWarning(
                $"Entity '{moduleName}::{entityName}' deduplicated attribute {dimension} '{key}' by keeping {keptDescription} and discarding {removedDescription}. Raw payload: {mapContext.SerializedPayload} (Path: {attributesPath})");

            return null;
        }

        if (unknownCount > 0)
        {
            return null;
        }

        string reason;
        if (activeIndices.Count == 0)
        {
            reason = "all referenced entities are inactive";
        }
        else if (activeIndices.Count > 1)
        {
            reason = "multiple referenced entities are active";
        }
        else
        {
            reason = "referenced entity activity metadata is inconsistent";
        }

        mapContext = mapContext.EnsureSerializedPayload(_context);
        var summary = string.Join(
            ", ",
            candidateIndices.Select(index => DescribeEntry(entries[index])));

        var message =
            $"Entity '{moduleName}::{entityName}' contains duplicate attribute {dimension} '{key}' but {reason}: {summary}. Raw payload: {mapContext.SerializedPayload}";

        return _context.CreateError("entity.attributes.duplicateAmbiguous", message, attributesPath);
    }

    private static string DescribeEntry(AttributeEntry entry)
    {
        var targetName = entry.Document.ReferenceEntityName
            ?? entry.Document.ReferenceEntityPhysicalName
            ?? (entry.Document.ReferenceEntityId?.ToString(CultureInfo.InvariantCulture) ?? "unknown reference");

        var status = entry.Document.ReferenceEntityIsActive switch
        {
            true => "True",
            false => "False",
            _ => "Unknown"
        };

        return $"column '{entry.Model.ColumnName.Value}' referencing '{targetName}' (refEntity.IsActive={status})";
    }

    private readonly record struct AttributeEntry(
        AttributeModel Model,
        ModelJsonDeserializer.AttributeDocument Document);
}
