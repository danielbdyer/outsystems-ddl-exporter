namespace Projection.Core

/// Primitive scalar types. Concrete mapping to a target surface scalar is
/// policy (A13); the IR holds the abstract type only.
///
/// Extracted from `Catalog.fs` at chapter A.0' slice ε so that
/// `SqlLiteral` (which references `PrimitiveType` in its `ofRaw` /
/// `formatRaw` projections) can compile BEFORE `Catalog.fs`. The new
/// `Attribute.DefaultValue : SqlLiteral option` field requires the
/// dependency to flow `PrimitiveType` → `SqlLiteral` → `Catalog`.
type PrimitiveType =
    | Integer
    | Decimal
    | Text
    | Boolean
    | DateTime
    | Date
    | Time
    | Binary
    | Guid
