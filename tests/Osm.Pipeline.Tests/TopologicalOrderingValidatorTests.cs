using System;
using System.Collections.Immutable;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Emission.Seeds;
using Osm.Pipeline.Orchestration;
using Xunit;

namespace Osm.Pipeline.Tests;

/// <summary>
/// Comprehensive test suite for TopologicalOrderingValidator.
/// Tests validation logic for detecting ordering violations.
/// </summary>
public sealed class TopologicalOrderingValidatorTests
{
    #region Happy Path Tests

    [Fact]
    public void Validate_EmptyInput_ReturnsValidWithZeroCounts()
    {
        // Arrange
        var validator = new TopologicalOrderingValidator();
        var emptyTables = ImmutableArray<StaticEntityTableData>.Empty;

        // Act
        var result = validator.Validate(emptyTables, model: null);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
        Assert.Equal(0, result.TotalEntities);
        Assert.Equal(0, result.TotalForeignKeys);
        Assert.Equal(0, result.MissingEdges);
        Assert.False(result.CycleDetected);
    }

    [Fact]
    public void Validate_DefaultImmutableArray_ReturnsValidWithZeroCounts()
    {
        // Arrange
        var validator = new TopologicalOrderingValidator();
        var defaultTables = default(ImmutableArray<StaticEntityTableData>);

        // Act
        var result = validator.Validate(defaultTables, model: null);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
        Assert.Equal(0, result.TotalEntities);
        Assert.Equal(0, result.TotalForeignKeys);
        Assert.Equal(0, result.MissingEdges);
        Assert.False(result.CycleDetected);
    }

    [Fact]
    public void Validate_SingleEntityNoRelationships_ReturnsValid()
    {
        // Arrange
        var validator = new TopologicalOrderingValidator();
        var definition = CreateTableDefinition("Parent", "OSUSR_PARENT");
        var table = new StaticEntityTableData(definition, ImmutableArray<StaticEntityRow>.Empty);
        var tables = ImmutableArray.Create(table);

        var entity = CreateEntity("Parent", "OSUSR_PARENT", Array.Empty<RelationshipModel>());
        var model = CreateModel(entity);

        // Act
        var result = validator.Validate(tables, model, NamingOverrideOptions.Empty);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
        Assert.Equal(1, result.TotalEntities);
        Assert.Equal(0, result.TotalForeignKeys);
        Assert.Equal(0, result.MissingEdges);
        Assert.False(result.CycleDetected);
    }

    [Fact]
    public void Validate_CorrectParentBeforeChild_ReturnsValid()
    {
        // Arrange
        var validator = new TopologicalOrderingValidator();

        var parentDefinition = CreateTableDefinition("Parent", "OSUSR_PARENT");
        var childDefinition = CreateTableDefinitionWithFk("Child", "OSUSR_CHILD");

        // Correct order: Parent at index 0, Child at index 1
        var tables = ImmutableArray.Create(
            new StaticEntityTableData(parentDefinition, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(childDefinition, ImmutableArray<StaticEntityRow>.Empty));

        var parentEntity = CreateEntity("Parent", "OSUSR_PARENT", Array.Empty<RelationshipModel>());
        var relationship = CreateRelationship("ParentId", "Parent", "OSUSR_PARENT", "FK_CHILD_PARENT");
        var childEntity = CreateEntity("Child", "OSUSR_CHILD", new[] { relationship });

        var model = CreateModel(parentEntity, childEntity);

        // Act
        var result = validator.Validate(tables, model, NamingOverrideOptions.Empty);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
        Assert.Equal(2, result.TotalEntities);
        Assert.Equal(1, result.TotalForeignKeys);
        Assert.Equal(0, result.MissingEdges);
        Assert.False(result.CycleDetected);
    }

    [Fact]
    public void Validate_ComplexHierarchy_ReturnsValid()
    {
        // Arrange: Grandparent -> Parent -> Child (3-level hierarchy)
        var validator = new TopologicalOrderingValidator();

        var grandparentDef = CreateTableDefinition("Grandparent", "OSUSR_GRANDPARENT");
        var parentDef = CreateTableDefinitionWithFk("Parent", "OSUSR_PARENT");
        var childDef = CreateTableDefinitionWithFk("Child", "OSUSR_CHILD");

        // Correct order: Grandparent, Parent, Child
        var tables = ImmutableArray.Create(
            new StaticEntityTableData(grandparentDef, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(parentDef, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(childDef, ImmutableArray<StaticEntityRow>.Empty));

        var grandparentEntity = CreateEntity("Grandparent", "OSUSR_GRANDPARENT", Array.Empty<RelationshipModel>());
        var parentRelationship = CreateRelationship("GrandparentId", "Grandparent", "OSUSR_GRANDPARENT", "FK_PARENT_GRANDPARENT");
        var parentEntity = CreateEntity("Parent", "OSUSR_PARENT", new[] { parentRelationship });
        var childRelationship = CreateRelationship("ParentId", "Parent", "OSUSR_PARENT", "FK_CHILD_PARENT");
        var childEntity = CreateEntity("Child", "OSUSR_CHILD", new[] { childRelationship });

        var model = CreateModel(grandparentEntity, parentEntity, childEntity);

        // Act
        var result = validator.Validate(tables, model, NamingOverrideOptions.Empty);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
        Assert.Equal(3, result.TotalEntities);
        Assert.Equal(2, result.TotalForeignKeys);
        Assert.Equal(0, result.MissingEdges);
        Assert.False(result.CycleDetected);
    }

    [Fact]
    public void Validate_MultipleIndependentTrees_ReturnsValid()
    {
        // Arrange: Tree1 (A -> B) and Tree2 (X -> Y), no relationships between trees
        var validator = new TopologicalOrderingValidator();

        var aDef = CreateTableDefinition("A", "OSUSR_A");
        var bDef = CreateTableDefinitionWithFk("B", "OSUSR_B");
        var xDef = CreateTableDefinition("X", "OSUSR_X");
        var yDef = CreateTableDefinitionWithFk("Y", "OSUSR_Y");

        var tables = ImmutableArray.Create(
            new StaticEntityTableData(aDef, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(bDef, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(xDef, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(yDef, ImmutableArray<StaticEntityRow>.Empty));

        var aEntity = CreateEntity("A", "OSUSR_A", Array.Empty<RelationshipModel>());
        var bRelationship = CreateRelationship("AId", "A", "OSUSR_A", "FK_B_A");
        var bEntity = CreateEntity("B", "OSUSR_B", new[] { bRelationship });
        var xEntity = CreateEntity("X", "OSUSR_X", Array.Empty<RelationshipModel>());
        var yRelationship = CreateRelationship("XId", "X", "OSUSR_X", "FK_Y_X");
        var yEntity = CreateEntity("Y", "OSUSR_Y", new[] { yRelationship });

        var model = CreateModel(aEntity, bEntity, xEntity, yEntity);

        // Act
        var result = validator.Validate(tables, model, NamingOverrideOptions.Empty);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
        Assert.Equal(4, result.TotalEntities);
        Assert.Equal(2, result.TotalForeignKeys);
        Assert.Equal(0, result.MissingEdges);
        Assert.False(result.CycleDetected);
    }

    #endregion

    #region Child-Before-Parent Violation Tests

    [Fact]
    public void Validate_ChildBeforeParent_ReturnsInvalidWithViolation()
    {
        // Arrange
        var validator = new TopologicalOrderingValidator();

        var parentDefinition = CreateTableDefinition("Parent", "OSUSR_PARENT");
        var childDefinition = CreateTableDefinitionWithFk("Child", "OSUSR_CHILD");

        // INCORRECT ORDER: Child at index 0, Parent at index 1
        var tables = ImmutableArray.Create(
            new StaticEntityTableData(childDefinition, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(parentDefinition, ImmutableArray<StaticEntityRow>.Empty));

        var parentEntity = CreateEntity("Parent", "OSUSR_PARENT", Array.Empty<RelationshipModel>());
        var relationship = CreateRelationship("ParentId", "Parent", "OSUSR_PARENT", "FK_CHILD_PARENT");
        var childEntity = CreateEntity("Child", "OSUSR_CHILD", new[] { relationship });

        var model = CreateModel(parentEntity, childEntity);

        // Act
        var result = validator.Validate(tables, model, NamingOverrideOptions.Empty);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Violations);
        Assert.True(result.CycleDetected);
        Assert.Equal(2, result.TotalEntities);
        Assert.Equal(1, result.TotalForeignKeys);

        var violation = result.Violations[0];
        Assert.Equal("OSUSR_CHILD", violation.ChildTable);
        Assert.Equal("OSUSR_PARENT", violation.ParentTable);
        Assert.Equal("FK_CHILD_PARENT", violation.ForeignKeyName);
        Assert.Equal(0, violation.ChildPosition);
        Assert.Equal(1, violation.ParentPosition);
        Assert.Equal("ChildBeforeParent", violation.ViolationType);
    }

    [Fact]
    public void Validate_MultipleChildBeforeParentViolations_ReturnsAllViolations()
    {
        // Arrange: Both B and C reference A, but A comes last
        var validator = new TopologicalOrderingValidator();

        var aDef = CreateTableDefinition("A", "OSUSR_A");
        var bDef = CreateTableDefinitionWithFk("B", "OSUSR_B");
        var cDef = CreateTableDefinitionWithFk("C", "OSUSR_C");

        // INCORRECT ORDER: B, C, A (should be A, B, C)
        var tables = ImmutableArray.Create(
            new StaticEntityTableData(bDef, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(cDef, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(aDef, ImmutableArray<StaticEntityRow>.Empty));

        var aEntity = CreateEntity("A", "OSUSR_A", Array.Empty<RelationshipModel>());
        var bRelationship = CreateRelationship("AId", "A", "OSUSR_A", "FK_B_A");
        var bEntity = CreateEntity("B", "OSUSR_B", new[] { bRelationship });
        var cRelationship = CreateRelationship("AId", "A", "OSUSR_A", "FK_C_A");
        var cEntity = CreateEntity("C", "OSUSR_C", new[] { cRelationship });

        var model = CreateModel(aEntity, bEntity, cEntity);

        // Act
        var result = validator.Validate(tables, model, NamingOverrideOptions.Empty);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Violations.Length);
        Assert.True(result.CycleDetected);
        Assert.All(result.Violations, v => Assert.Equal("ChildBeforeParent", v.ViolationType));
        Assert.All(result.Violations, v => Assert.Equal(2, v.ParentPosition)); // A at position 2
    }

    [Fact]
    public void Validate_GrandchildBeforeGrandparent_DetectsViolation()
    {
        // Arrange: Child -> Parent -> Grandparent (completely reversed)
        var validator = new TopologicalOrderingValidator();

        var grandparentDef = CreateTableDefinition("Grandparent", "OSUSR_GRANDPARENT");
        var parentDef = CreateTableDefinitionWithFk("Parent", "OSUSR_PARENT");
        var childDef = CreateTableDefinitionWithFk("Child", "OSUSR_CHILD");

        // INCORRECT ORDER: Child, Parent, Grandparent (reversed)
        var tables = ImmutableArray.Create(
            new StaticEntityTableData(childDef, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(parentDef, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(grandparentDef, ImmutableArray<StaticEntityRow>.Empty));

        var grandparentEntity = CreateEntity("Grandparent", "OSUSR_GRANDPARENT", Array.Empty<RelationshipModel>());
        var parentRelationship = CreateRelationship("GrandparentId", "Grandparent", "OSUSR_GRANDPARENT", "FK_PARENT_GRANDPARENT");
        var parentEntity = CreateEntity("Parent", "OSUSR_PARENT", new[] { parentRelationship });
        var childRelationship = CreateRelationship("ParentId", "Parent", "OSUSR_PARENT", "FK_CHILD_PARENT");
        var childEntity = CreateEntity("Child", "OSUSR_CHILD", new[] { childRelationship });

        var model = CreateModel(grandparentEntity, parentEntity, childEntity);

        // Act
        var result = validator.Validate(tables, model, NamingOverrideOptions.Empty);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Violations.Length); // Parent-Grandparent + Child-Parent violations
        Assert.True(result.CycleDetected);
    }

    [Fact]
    public void Validate_SelfReferencingTable_IsValid()
    {
        // Arrange: Employee references itself (ManagerId → Employee)
        var validator = new TopologicalOrderingValidator();

        var employeeDef = new StaticEntitySeedTableDefinition(
            "HR",
            "Employee",
            "dbo",
            "OSUSR_EMPLOYEE",
            "Employee", // EffectiveName = logicalName when no naming overrides
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null,
                    IsPrimaryKey: true, IsIdentity: false, IsNullable: false),
                new StaticEntitySeedColumn("ManagerId", "MANAGERID", "ManagerId", "int", null, null, null,
                    IsPrimaryKey: false, IsIdentity: false, IsNullable: true)));

        var tables = ImmutableArray.Create(
            new StaticEntityTableData(employeeDef, ImmutableArray<StaticEntityRow>.Empty));

        // Self-referencing relationship
        var selfRelationship = CreateRelationship("ManagerId", "Employee", "OSUSR_EMPLOYEE", "FK_EMPLOYEE_MANAGER");
        var employeeEntity = CreateEntity("Employee", "OSUSR_EMPLOYEE", new[] { selfRelationship });

        var model = CreateModel(employeeEntity);

        // Act
        var result = validator.Validate(tables, model);

        // Assert - Self-reference should be VALID (not a violation)
        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
        Assert.Equal(1, result.TotalForeignKeys);
        Assert.False(result.CycleDetected);
    }

    [Fact]
    public void Validate_MultipleFksToSameParent_CountsAllCorrectly()
    {
        // Arrange: Order has multiple FKs to Customer (BillToCustomer, ShipToCustomer)
        var validator = new TopologicalOrderingValidator();

        var customerDef = CreateTableDefinition("Customer", "OSUSR_CUSTOMER");
        var orderDef = new StaticEntitySeedTableDefinition(
            "Sales",
            "Order",
            "dbo",
            "OSUSR_ORDER",
            "Order", // EffectiveName = logicalName when no naming overrides
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null,
                    IsPrimaryKey: true, IsIdentity: false, IsNullable: false),
                new StaticEntitySeedColumn("BillToCustomerId", "BILLTOCUSTOMERID", "BillToCustomerId", "int", null, null, null,
                    IsPrimaryKey: false, IsIdentity: false, IsNullable: false),
                new StaticEntitySeedColumn("ShipToCustomerId", "SHIPTOCUSTOMERID", "ShipToCustomerId", "int", null, null, null,
                    IsPrimaryKey: false, IsIdentity: false, IsNullable: false)));

        var tables = ImmutableArray.Create(
            new StaticEntityTableData(customerDef, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(orderDef, ImmutableArray<StaticEntityRow>.Empty));

        var customerEntity = CreateEntity("Customer", "OSUSR_CUSTOMER", Array.Empty<RelationshipModel>());

        var billToRelationship = CreateRelationship("BillToCustomerId", "Customer", "OSUSR_CUSTOMER", "FK_ORDER_BILLTO");
        var shipToRelationship = CreateRelationship("ShipToCustomerId", "Customer", "OSUSR_CUSTOMER", "FK_ORDER_SHIPTO");
        var orderEntity = CreateEntity("Order", "OSUSR_ORDER", new[] { billToRelationship, shipToRelationship });

        var model = CreateModel(customerEntity, orderEntity);

        // Act
        var result = validator.Validate(tables, model);

        // Assert - Both FKs should be counted
        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
        Assert.Equal(2, result.TotalForeignKeys); // Both FKs counted
    }

    [Fact]
    public void Validate_TwoWayCycle_DetectsViolation()
    {
        // Arrange: A → B and B → A (true cycle)
        // When ordered as [A, B], validator detects that A comes before B (its parent)
        var validator = new TopologicalOrderingValidator();

        var aDef = new StaticEntitySeedTableDefinition(
            "Sample",
            "A",
            "dbo",
            "OSUSR_A",
            "A", // EffectiveName = logicalName when no naming overrides
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null,
                    IsPrimaryKey: true, IsIdentity: false, IsNullable: false),
                new StaticEntitySeedColumn("BId", "BID", "BId", "int", null, null, null,
                    IsPrimaryKey: false, IsIdentity: false, IsNullable: true)));

        var bDef = new StaticEntitySeedTableDefinition(
            "Sample",
            "B",
            "dbo",
            "OSUSR_B",
            "B", // EffectiveName = logicalName when no naming overrides
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null,
                    IsPrimaryKey: true, IsIdentity: false, IsNullable: false),
                new StaticEntitySeedColumn("AId", "AID", "AId", "int", null, null, null,
                    IsPrimaryKey: false, IsIdentity: false, IsNullable: true)));

        // Order: A, B - both orderings are "valid" for a cycle from validator's perspective
        // because validator checks if parent comes AFTER child, and both do
        var tables = ImmutableArray.Create(
            new StaticEntityTableData(aDef, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(bDef, ImmutableArray<StaticEntityRow>.Empty));

        var aToB = CreateRelationship("BId", "B", "OSUSR_B", "FK_A_B");
        var aEntity = CreateEntity("A", "OSUSR_A", new[] { aToB });

        var bToA = CreateRelationship("AId", "A", "OSUSR_A", "FK_B_A");
        var bEntity = CreateEntity("B", "OSUSR_B", new[] { bToA });

        var model = CreateModel(aEntity, bEntity);

        // Act
        var result = validator.Validate(tables, model);

        // Assert - Ordering [A, B] creates a violation:
        // - A references B (parentIndex=1 > childIndex=0) → VIOLATION (A comes before its parent B)
        // - B references A (parentIndex=0 < childIndex=1) → Valid (parent A comes before child B)
        // Result: 1 violation detected (A before B is wrong since A depends on B)
        Assert.False(result.IsValid);
        Assert.Single(result.Violations);
        Assert.True(result.CycleDetected);

        var violation = result.Violations[0];
        Assert.Equal("OSUSR_A", violation.ChildTable);  // A is the problem (comes before its parent)
        Assert.Equal("OSUSR_B", violation.ParentTable); // B is A's parent
        Assert.Equal("ChildBeforeParent", violation.ViolationType);

        // NEW: Verify cycle diagnostic information
        Assert.Single(result.Cycles);
        var cycle = result.Cycles[0];
        Assert.Contains("A", cycle.TablesInCycle);
        Assert.Contains("B", cycle.TablesInCycle);
        Assert.NotEmpty(cycle.CyclePath);
        Assert.NotEmpty(cycle.ForeignKeys);
    }

    [Fact]
    public void Validate_LargeNumberOfEntities_PerformsWell()
    {
        // Arrange: Create a long chain of 100 entities
        var validator = new TopologicalOrderingValidator();

        var tableDefs = new List<StaticEntityTableData>();
        var entities = new List<EntityModel>();

        // Create chain: E0 → E1 → E2 → ... → E99
        for (int i = 0; i < 100; i++)
        {
            var tableName = $"OSUSR_ENTITY_{i}";
            var columns = new List<StaticEntitySeedColumn>
            {
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null,
                    IsPrimaryKey: true, IsIdentity: false, IsNullable: false)
            };

            if (i > 0)
            {
                columns.Add(new StaticEntitySeedColumn("ParentId", "PARENTID", "ParentId", "int", null, null, null,
                    IsPrimaryKey: false, IsIdentity: false, IsNullable: false));
            }

            var def = new StaticEntitySeedTableDefinition(
                "Sample",
                $"Entity{i}",
                "dbo",
                tableName,
                $"Entity{i}", // EffectiveName = logicalName when no naming overrides
                columns.ToImmutableArray());

            tableDefs.Add(new StaticEntityTableData(def, ImmutableArray<StaticEntityRow>.Empty));

            if (i == 0)
            {
                entities.Add(CreateEntity($"Entity{i}", tableName, Array.Empty<RelationshipModel>()));
            }
            else
            {
                var relationship = CreateRelationship("ParentId", $"Entity{i - 1}", $"OSUSR_ENTITY_{i - 1}", $"FK_E{i}_E{i - 1}");
                entities.Add(CreateEntity($"Entity{i}", tableName, new[] { relationship }));
            }
        }

        var tables = tableDefs.ToImmutableArray();
        var model = CreateModel(entities.ToArray());

        // Act
        var startTime = DateTime.UtcNow;
        var result = validator.Validate(tables, model);
        var duration = DateTime.UtcNow - startTime;

        // Assert - Should complete quickly (< 100ms for 100 entities)
        Assert.True(duration.TotalMilliseconds < 100, $"Validation took {duration.TotalMilliseconds}ms (expected < 100ms)");
        Assert.True(result.IsValid);
        Assert.Equal(99, result.TotalForeignKeys); // 99 FKs in the chain
        Assert.Equal(100, result.TotalEntities);
    }

    #endregion

    #region Missing Parent Tests

    [Fact]
    public void Validate_ParentNotInOrderedList_ReportsMissingParentViolation()
    {
        // Arrange
        var validator = new TopologicalOrderingValidator();

        var childDefinition = CreateTableDefinitionWithFk("Child", "OSUSR_CHILD");
        var tables = ImmutableArray.Create(
            new StaticEntityTableData(childDefinition, ImmutableArray<StaticEntityRow>.Empty));

        // Parent entity exists in model but NOT in ordered tables list
        var parentEntity = CreateEntity("Parent", "OSUSR_PARENT", Array.Empty<RelationshipModel>());
        var relationship = CreateRelationship("ParentId", "Parent", "OSUSR_PARENT", "FK_CHILD_PARENT");
        var childEntity = CreateEntity("Child", "OSUSR_CHILD", new[] { relationship });

        var model = CreateModel(parentEntity, childEntity);

        // Act
        var result = validator.Validate(tables, model, NamingOverrideOptions.Empty);

        // Assert
        Assert.True(result.IsValid); // Missing parents don't invalidate (just counted)
        Assert.Single(result.Violations);
        Assert.Equal(1, result.MissingEdges);
        Assert.False(result.CycleDetected);

        var violation = result.Violations[0];
        Assert.Equal("OSUSR_CHILD", violation.ChildTable);
        Assert.Equal("OSUSR_PARENT", violation.ParentTable);
        Assert.Equal("FK_CHILD_PARENT", violation.ForeignKeyName);
        Assert.Equal(0, violation.ChildPosition);
        Assert.Equal(-1, violation.ParentPosition); // -1 indicates missing
        Assert.Equal("MissingParent", violation.ViolationType);
    }

    [Fact]
    public void Validate_MultipleMissingParents_CountsAllMissing()
    {
        // Arrange
        var validator = new TopologicalOrderingValidator();

        var childDef = CreateTableDefinitionWithFk("Child", "OSUSR_CHILD");
        var tables = ImmutableArray.Create(
            new StaticEntityTableData(childDef, ImmutableArray<StaticEntityRow>.Empty));

        var parent1Entity = CreateEntity("Parent1", "OSUSR_PARENT1", Array.Empty<RelationshipModel>());
        var parent2Entity = CreateEntity("Parent2", "OSUSR_PARENT2", Array.Empty<RelationshipModel>());

        var relationship1 = CreateRelationship("Parent1Id", "Parent1", "OSUSR_PARENT1", "FK_CHILD_PARENT1");
        var relationship2 = CreateRelationship("Parent2Id", "Parent2", "OSUSR_PARENT2", "FK_CHILD_PARENT2");

        var childEntity = CreateEntity("Child", "OSUSR_CHILD", new[] { relationship1, relationship2 });

        var model = CreateModel(parent1Entity, parent2Entity, childEntity);

        // Act
        var result = validator.Validate(tables, model, NamingOverrideOptions.Empty);

        // Assert
        Assert.True(result.IsValid); // Only MissingParent violations
        Assert.Equal(2, result.Violations.Length);
        Assert.Equal(2, result.MissingEdges);
        Assert.False(result.CycleDetected);
        Assert.All(result.Violations, v => Assert.Equal("MissingParent", v.ViolationType));
        Assert.All(result.Violations, v => Assert.Equal(-1, v.ParentPosition));
    }

    [Fact]
    public void Validate_MixedChildBeforeParentAndMissingParent_ReturnsInvalid()
    {
        // Arrange: One FK violation + one missing parent
        var validator = new TopologicalOrderingValidator();

        var aDef = CreateTableDefinition("A", "OSUSR_A");
        var bDef = CreateTableDefinitionWithFk("B", "OSUSR_B");

        // B comes before A (child-before-parent), and B also references missing C
        var tables = ImmutableArray.Create(
            new StaticEntityTableData(bDef, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(aDef, ImmutableArray<StaticEntityRow>.Empty));

        var aEntity = CreateEntity("A", "OSUSR_A", Array.Empty<RelationshipModel>());
        var cEntity = CreateEntity("C", "OSUSR_C", Array.Empty<RelationshipModel>()); // C not in tables

        var relationshipA = CreateRelationship("AId", "A", "OSUSR_A", "FK_B_A");
        var relationshipC = CreateRelationship("CId", "C", "OSUSR_C", "FK_B_C");
        var bEntity = CreateEntity("B", "OSUSR_B", new[] { relationshipA, relationshipC });

        var model = CreateModel(aEntity, bEntity, cEntity);

        // Act
        var result = validator.Validate(tables, model, NamingOverrideOptions.Empty);

        // Assert
        Assert.False(result.IsValid); // ChildBeforeParent makes it invalid
        Assert.Equal(2, result.Violations.Length);
        Assert.Equal(1, result.MissingEdges);
        Assert.True(result.CycleDetected);

        var childBeforeParent = result.Violations.Single(v => v.ViolationType == "ChildBeforeParent");
        Assert.Equal("OSUSR_A", childBeforeParent.ParentTable);

        var missingParent = result.Violations.Single(v => v.ViolationType == "MissingParent");
        Assert.Equal("OSUSR_C", missingParent.ParentTable);
        Assert.Equal(-1, missingParent.ParentPosition);
    }

    #endregion

    #region Case Insensitivity Tests

    [Fact]
    public void Validate_CaseInsensitiveTableNameLookup_WorksCorrectly()
    {
        // Arrange: Model uses uppercase, table definitions use mixed case
        var validator = new TopologicalOrderingValidator();

        var parentDefinition = CreateTableDefinition("Parent", "osusr_parent"); // lowercase
        var childDefinition = CreateTableDefinitionWithFk("Child", "OSUSR_CHILD"); // uppercase

        var tables = ImmutableArray.Create(
            new StaticEntityTableData(parentDefinition, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(childDefinition, ImmutableArray<StaticEntityRow>.Empty));

        // Model uses different casing
        var parentEntity = CreateEntity("Parent", "OSUSR_PARENT", Array.Empty<RelationshipModel>()); // uppercase
        var relationship = CreateRelationship("ParentId", "Parent", "OSUSR_PARENT", "FK_CHILD_PARENT");
        var childEntity = CreateEntity("Child", "osusr_child", new[] { relationship }); // lowercase

        var model = CreateModel(parentEntity, childEntity);

        // Act
        var result = validator.Validate(tables, model, NamingOverrideOptions.Empty);

        // Assert - Should match case-insensitively
        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
        Assert.Equal(1, result.TotalForeignKeys);
        Assert.Equal(0, result.MissingEdges);
    }

    #endregion

    #region Entity Not In Model Tests

    [Fact]
    public void Validate_TableNotInModel_SkipsEntity()
    {
        // Arrange: Table exists in ordered list but not in model
        var validator = new TopologicalOrderingValidator();

        var orphanDef = CreateTableDefinition("Orphan", "OSUSR_ORPHAN");
        var tables = ImmutableArray.Create(
            new StaticEntityTableData(orphanDef, ImmutableArray<StaticEntityRow>.Empty));

        // Act - Use null model (no entities)
        var result = validator.Validate(tables, model: null);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
        Assert.Equal(1, result.TotalEntities); // Counted in total
        Assert.Equal(0, result.TotalForeignKeys); // But no FKs processed
    }

    #endregion

    #region Non-FK Relationship Tests

    [Fact]
    public void Validate_NonForeignKeyRelationship_Ignored()
    {
        // Arrange: Entity has relationship but IsForeignKey = false
        var validator = new TopologicalOrderingValidator();

        var aDef = CreateTableDefinition("A", "OSUSR_A");
        var bDef = CreateTableDefinition("B", "OSUSR_B");

        var tables = ImmutableArray.Create(
            new StaticEntityTableData(bDef, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(aDef, ImmutableArray<StaticEntityRow>.Empty));

        var aEntity = CreateEntity("A", "OSUSR_A", Array.Empty<RelationshipModel>());

        // Create non-FK relationship (IsForeignKey = false)
        var nonFkRelationship = CreateNonFkRelationship("AId", "A", "OSUSR_A");
        var bEntity = CreateEntity("B", "OSUSR_B", new[] { nonFkRelationship });

        var model = CreateModel(aEntity, bEntity);

        // Act
        var result = validator.Validate(tables, model, NamingOverrideOptions.Empty);

        // Assert - Non-FK relationship should be ignored
        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
        Assert.Equal(0, result.TotalForeignKeys); // Not counted
    }

    [Fact]
    public void Validate_RelationshipWithEmptyActualConstraints_Ignored()
    {
        // Arrange: FK relationship with empty ActualConstraints
        var validator = new TopologicalOrderingValidator();

        var aDef = CreateTableDefinition("A", "OSUSR_A");
        var tables = ImmutableArray.Create(
            new StaticEntityTableData(aDef, ImmutableArray<StaticEntityRow>.Empty));

        // Relationship with HasDatabaseConstraint=true but empty ActualConstraints
        var relationshipWithNoConstraints = RelationshipModel.Create(
            new AttributeName("SomeId"),
            new EntityName("Unknown"),
            new TableName("OSUSR_UNKNOWN"),
            deleteRuleCode: "Cascade",
            hasDatabaseConstraint: true,
            actualConstraints: Array.Empty<RelationshipActualConstraint>()).Value;

        var aEntity = CreateEntity("A", "OSUSR_A", new[] { relationshipWithNoConstraints });

        var model = CreateModel(aEntity);

        // Act
        var result = validator.Validate(tables, model);

        // Assert - Should be ignored because ActualConstraints is empty
        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
        Assert.Equal(0, result.TotalForeignKeys); // Not counted
        Assert.Equal(0, result.ValidatedConstraints);
        Assert.Equal(0, result.SkippedConstraints);
    }

    [Fact]
    public void Validate_RelationshipWithUnhydratedColumns_SkipsConstraint()
    {
        // Arrange: FK relationship where constraint columns are not hydrated
        var validator = new TopologicalOrderingValidator();

        var parentDef = CreateTableDefinition("Parent", "OSUSR_PARENT");
        var childDef = CreateTableDefinitionWithFk("Child", "OSUSR_CHILD");

        var tables = ImmutableArray.Create(
            new StaticEntityTableData(childDef, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(parentDef, ImmutableArray<StaticEntityRow>.Empty));

        var parentEntity = CreateEntity("Parent", "OSUSR_PARENT", Array.Empty<RelationshipModel>());

        var unhydratedConstraint = RelationshipActualConstraint.Create(
            "FK_CHILD_PARENT",
            referencedSchema: "dbo",
            referencedTable: "OSUSR_PARENT",
            onDeleteAction: "NO_ACTION",
            onUpdateAction: "NO_ACTION",
            new[]
            {
                RelationshipActualConstraintColumn.Create(ownerColumn: string.Empty, ownerAttribute: "ParentId", referencedColumn: string.Empty, referencedAttribute: "Id", ordinal: 0)
            });

        var relationship = RelationshipModel.Create(
            new AttributeName("ParentId"),
            new EntityName("Parent"),
            new TableName("OSUSR_PARENT"),
            deleteRuleCode: "Cascade",
            hasDatabaseConstraint: true,
            actualConstraints: new[] { unhydratedConstraint }).Value;

        var childEntity = CreateEntity("Child", "OSUSR_CHILD", new[] { relationship });

        var model = CreateModel(parentEntity, childEntity);

        // Act
        var result = validator.Validate(tables, model, NamingOverrideOptions.Empty);

        // Assert - Constraint skipped because columns are not hydrated
        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
        Assert.Equal(0, result.TotalForeignKeys);
        Assert.Equal(0, result.ValidatedConstraints);
        Assert.Equal(1, result.SkippedConstraints);
    }

    #endregion

    #region Unnamed FK Tests

    [Fact]
    public void Validate_UnnamedForeignKey_ShowsPlaceholder()
    {
        // Arrange
        var validator = new TopologicalOrderingValidator();

        var parentDefinition = CreateTableDefinition("Parent", "OSUSR_PARENT");
        var childDefinition = CreateTableDefinitionWithFk("Child", "OSUSR_CHILD");

        var tables = ImmutableArray.Create(
            new StaticEntityTableData(childDefinition, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(parentDefinition, ImmutableArray<StaticEntityRow>.Empty));

        var parentEntity = CreateEntity("Parent", "OSUSR_PARENT", Array.Empty<RelationshipModel>());
        var relationship = CreateRelationship("ParentId", "Parent", "OSUSR_PARENT", foreignKeyName: null); // Unnamed
        var childEntity = CreateEntity("Child", "OSUSR_CHILD", new[] { relationship });

        var model = CreateModel(parentEntity, childEntity);

        // Act
        var result = validator.Validate(tables, model, NamingOverrideOptions.Empty);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Violations);

        var violation = result.Violations[0];
        Assert.Equal("<unnamed>", violation.ForeignKeyName); // Placeholder for null name
    }

    #endregion

    #region Naming Override Tests

    [Fact]
    public void Validate_WithNamingOverrides_ResolvesCorrectly()
    {
        // Arrange: Physical names differ from table definitions due to naming overrides
        var validator = new TopologicalOrderingValidator();

        // Table definitions use sanitized names (as they appear in sorted output)
        // EffectiveName should match what GetEffectiveTableName returns with overrides
        var parentDef = new StaticEntitySeedTableDefinition(
            "Sample",
            "Parent",
            "dbo",
            "USR_PARENT_SAN",
            "USR_PARENT_SAN", // EffectiveName = sanitized name after override
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null,
                    IsPrimaryKey: true, IsIdentity: false, IsNullable: false)));

        var childDef = new StaticEntitySeedTableDefinition(
            "Sample",
            "Child",
            "dbo",
            "USR_CHILD_SAN",
            "USR_CHILD_SAN", // EffectiveName = sanitized name after override
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null,
                    IsPrimaryKey: true, IsIdentity: false, IsNullable: false),
                new StaticEntitySeedColumn("ParentId", "PARENTID", "ParentId", "int", null, null, null,
                    IsPrimaryKey: false, IsIdentity: false, IsNullable: false)));

        var tables = ImmutableArray.Create(
            new StaticEntityTableData(parentDef, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(childDef, ImmutableArray<StaticEntityRow>.Empty));

        // Model uses original names (before override)
        var parentEntity = CreateEntity("Parent", "OSUSR_PARENT", Array.Empty<RelationshipModel>());
        var relationship = CreateRelationship("ParentId", "Parent", "OSUSR_PARENT", "FK_CHILD_PARENT");
        var childEntity = CreateEntity("Child", "OSUSR_CHILD", new[] { relationship });

        var model = CreateModel(parentEntity, childEntity);

        // Naming overrides map original -> sanitized
        var parentOverride = NamingOverrideRule.Create("dbo", "OSUSR_PARENT", null, null, "USR_PARENT_SAN").Value;
        var childOverride = NamingOverrideRule.Create("dbo", "OSUSR_CHILD", null, null, "USR_CHILD_SAN").Value;
        var namingOverrides = NamingOverrideOptions.Create(new[] { parentOverride, childOverride }).Value;

        // Act
        var result = validator.Validate(tables, model, namingOverrides);

        // Assert - Should resolve names correctly via overrides
        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
        Assert.Equal(1, result.TotalForeignKeys);
        Assert.Equal(0, result.MissingEdges);
    }

    [Fact]
    public void Validate_WithNamingOverrides_DetectsViolations()
    {
        // Arrange: Naming overrides applied, but ordering is wrong
        var validator = new TopologicalOrderingValidator();

        // Table definitions use sanitized names - WRONG ORDER (child before parent)
        var childDef = new StaticEntitySeedTableDefinition(
            "Sample",
            "Child",
            "dbo",
            "USR_CHILD_SAN",
            "USR_CHILD_SAN", // EffectiveName = sanitized name after override
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null,
                    IsPrimaryKey: true, IsIdentity: false, IsNullable: false),
                new StaticEntitySeedColumn("ParentId", "PARENTID", "ParentId", "int", null, null, null,
                    IsPrimaryKey: false, IsIdentity: false, IsNullable: false)));

        var parentDef = new StaticEntitySeedTableDefinition(
            "Sample",
            "Parent",
            "dbo",
            "USR_PARENT_SAN",
            "USR_PARENT_SAN", // EffectiveName = sanitized name after override
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null,
                    IsPrimaryKey: true, IsIdentity: false, IsNullable: false)));

        var tables = ImmutableArray.Create(
            new StaticEntityTableData(childDef, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(parentDef, ImmutableArray<StaticEntityRow>.Empty));

        // Model uses original names
        var parentEntity = CreateEntity("Parent", "OSUSR_PARENT", Array.Empty<RelationshipModel>());
        var relationship = CreateRelationship("ParentId", "Parent", "OSUSR_PARENT", "FK_CHILD_PARENT");
        var childEntity = CreateEntity("Child", "OSUSR_CHILD", new[] { relationship });

        var model = CreateModel(parentEntity, childEntity);

        // Naming overrides map original -> sanitized
        var parentOverride = NamingOverrideRule.Create("dbo", "OSUSR_PARENT", null, null, "USR_PARENT_SAN").Value;
        var childOverride = NamingOverrideRule.Create("dbo", "OSUSR_CHILD", null, null, "USR_CHILD_SAN").Value;
        var namingOverrides = NamingOverrideOptions.Create(new[] { parentOverride, childOverride }).Value;

        // Act
        var result = validator.Validate(tables, model, namingOverrides);

        // Assert - Should detect violation even with naming overrides
        Assert.False(result.IsValid);
        Assert.Single(result.Violations);
        Assert.True(result.CycleDetected);

        var violation = result.Violations[0];
        Assert.Equal("USR_CHILD_SAN", violation.ChildTable);
        Assert.Equal("OSUSR_PARENT", violation.ParentTable); // Violation uses physical name from constraint
        Assert.Equal("ChildBeforeParent", violation.ViolationType);
    }

    #endregion

    #region Metric Accuracy Tests

    [Fact]
    public void Validate_CountsMetricsAccurately()
    {
        // Arrange: Complex scenario with multiple FK types
        var validator = new TopologicalOrderingValidator();

        var aDef = CreateTableDefinition("A", "OSUSR_A");
        var bDef = CreateTableDefinitionWithFk("B", "OSUSR_B");
        var cDef = CreateTableDefinitionWithFk("C", "OSUSR_C");

        var tables = ImmutableArray.Create(
            new StaticEntityTableData(aDef, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(bDef, ImmutableArray<StaticEntityRow>.Empty),
            new StaticEntityTableData(cDef, ImmutableArray<StaticEntityRow>.Empty));

        var aEntity = CreateEntity("A", "OSUSR_A", Array.Empty<RelationshipModel>());

        // B has 1 valid FK to A
        var bRelationship = CreateRelationship("AId", "A", "OSUSR_A", "FK_B_A");
        var bEntity = CreateEntity("B", "OSUSR_B", new[] { bRelationship });

        // C has 2 FKs: 1 to B (valid) + 1 to missing entity X
        var cRelationshipB = CreateRelationship("BId", "B", "OSUSR_B", "FK_C_B");
        var cRelationshipX = CreateRelationship("XId", "X", "OSUSR_X", "FK_C_X");
        var cEntity = CreateEntity("C", "OSUSR_C", new[] { cRelationshipB, cRelationshipX });

        var xEntity = CreateEntity("X", "OSUSR_X", Array.Empty<RelationshipModel>()); // X not in tables

        var model = CreateModel(aEntity, bEntity, cEntity, xEntity);

        // Act
        var result = validator.Validate(tables, model, NamingOverrideOptions.Empty);

        // Assert
        Assert.Equal(3, result.TotalEntities);
        Assert.Equal(3, result.TotalForeignKeys); // B->A, C->B, C->X
        Assert.Equal(1, result.MissingEdges); // C->X
        Assert.Equal(3, result.ValidatedConstraints);
        Assert.Equal(0, result.SkippedConstraints);
        Assert.Single(result.Violations); // Only missing parent
        Assert.True(result.IsValid); // Missing parent doesn't invalidate
    }

    #endregion

    #region Helper Methods

    private static StaticEntitySeedTableDefinition CreateTableDefinition(string logicalName, string physicalName)
    {
        return new StaticEntitySeedTableDefinition(
            "Sample",
            logicalName,
            "dbo",
            physicalName,
            logicalName, // EffectiveName = logicalName when no naming overrides
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null,
                    IsPrimaryKey: true, IsIdentity: false, IsNullable: false)));
    }

    private static StaticEntitySeedTableDefinition CreateTableDefinitionWithFk(string logicalName, string physicalName)
    {
        return new StaticEntitySeedTableDefinition(
            "Sample",
            logicalName,
            "dbo",
            physicalName,
            logicalName, // EffectiveName = logicalName when no naming overrides
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null,
                    IsPrimaryKey: true, IsIdentity: false, IsNullable: false),
                new StaticEntitySeedColumn("ParentId", "PARENTID", "ParentId", "int", null, null, null,
                    IsPrimaryKey: false, IsIdentity: false, IsNullable: false)));
    }

    private static EntityModel CreateEntity(string logicalName, string physicalTableName, RelationshipModel[] relationships)
    {
        return EntityModel.Create(
            new ModuleName("Sample"),
            new EntityName(logicalName),
            new TableName(physicalTableName),
            new SchemaName("dbo"),
            catalog: null,
            isStatic: true,
            isExternal: false,
            isActive: true,
            attributes: new[] { CreateAttribute("Id", "ID", isIdentifier: true) },
            relationships: relationships).Value;
    }

    private static RelationshipModel CreateRelationship(string attributeName, string targetEntityName,
        string targetTableName, string? foreignKeyName)
    {
        return RelationshipModel.Create(
            new AttributeName(attributeName),
            new EntityName(targetEntityName),
            new TableName(targetTableName),
            deleteRuleCode: "Cascade",
            hasDatabaseConstraint: true,
            actualConstraints: new[]
            {
                RelationshipActualConstraint.Create(
                    foreignKeyName,  // Pass through null if that's what was provided
                    referencedSchema: "dbo",
                    referencedTable: targetTableName,
                    onDeleteAction: "NO_ACTION",
                    onUpdateAction: "NO_ACTION",
                    new[] { RelationshipActualConstraintColumn.Create("PARENTID", "ParentId", "ID", "Id", 0) })
            }).Value;
    }

    private static RelationshipModel CreateNonFkRelationship(string attributeName, string targetEntityName,
        string targetTableName)
    {
        // Create relationship with HasDatabaseConstraint = false (non-FK)
        return RelationshipModel.Create(
            new AttributeName(attributeName),
            new EntityName(targetEntityName),
            new TableName(targetTableName),
            deleteRuleCode: "Ignore",
            hasDatabaseConstraint: false, // Non-FK
            actualConstraints: Array.Empty<RelationshipActualConstraint>()).Value;
    }


    private static AttributeModel CreateAttribute(string logicalName, string columnName, bool isIdentifier = false)
    {
        return AttributeModel.Create(
            new AttributeName(logicalName),
            new ColumnName(columnName),
            dataType: "INT",
            isMandatory: isIdentifier,
            isIdentifier: isIdentifier,
            isAutoNumber: false,
            isActive: true,
            reality: new AttributeReality(null, null, null, null, IsPresentButInactive: false),
            metadata: AttributeMetadata.Empty,
            onDisk: AttributeOnDiskMetadata.Empty).Value;
    }

    private static OsmModel CreateModel(params EntityModel[] entities)
    {
        var module = ModuleModel.Create(
            new ModuleName("Sample"),
            isSystemModule: false,
            isActive: true,
            entities: entities).Value;

        return OsmModel.Create(DateTime.UtcNow, new[] { module }).Value;
    }

    #endregion
}
