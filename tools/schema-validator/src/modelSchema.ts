import { z, ZodError } from "zod";

export interface AttributeReality {
  readonly isNullableInDatabase: boolean | null;
  readonly hasNulls: boolean | null;
  readonly hasDuplicates: boolean | null;
  readonly hasOrphans: boolean | null;
  readonly isPresentButInactive: boolean;
}

export interface AttributeReference {
  readonly isReference: boolean;
  readonly targetEntityId: number | null;
  readonly targetEntityName: string | null;
  readonly targetEntityPhysicalName: string | null;
  readonly deleteRuleCode: string | null;
  readonly hasDbConstraint: boolean;
}

export interface NormalizedAttribute {
  readonly name: string;
  readonly physicalName: string;
  readonly originalName: string | null;
  readonly dataType: string;
  readonly length: number | null;
  readonly precision: number | null;
  readonly scale: number | null;
  readonly defaultValue: string | null;
  readonly isMandatory: boolean;
  readonly isIdentifier: boolean;
  readonly isAutoNumber: boolean;
  readonly isActive: boolean;
  readonly externalDbType: string | null;
  readonly reference: AttributeReference;
  readonly reality: AttributeReality;
}

export interface NormalizedIndexColumn {
  readonly attribute: string;
  readonly physicalColumn: string;
  readonly ordinal: number;
}

export interface NormalizedIndex {
  readonly name: string;
  readonly isUnique: boolean;
  readonly isPrimary: boolean;
  readonly isPlatformAuto: boolean;
  readonly columns: NormalizedIndexColumn[];
}

export interface NormalizedRelationship {
  readonly viaAttributeName: string;
  readonly toEntityName: string;
  readonly toEntityPhysicalName: string;
  readonly deleteRuleCode: string;
  readonly hasDbConstraint: boolean;
}

export interface NormalizedEntity {
  readonly name: string;
  readonly physicalName: string;
  readonly schema: string;
  readonly catalog: string | null;
  readonly isStatic: boolean;
  readonly isExternal: boolean;
  readonly isActive: boolean;
  readonly attributes: NormalizedAttribute[];
  readonly indexes: NormalizedIndex[];
  readonly relationships: NormalizedRelationship[];
}

export interface NormalizedModule {
  readonly name: string;
  readonly isSystem: boolean;
  readonly isActive: boolean;
  readonly entities: NormalizedEntity[];
}

export interface NormalizedModel {
  readonly exportedAtUtc: Date | null;
  readonly modules: NormalizedModule[];
}

const BOOL_ZERO_ONE = [0, 1] as const;

type BoolZeroOne = (typeof BOOL_ZERO_ONE)[number];

const boolish = z
  .union([z.boolean(), z.number().int().refine((value) => BOOL_ZERO_ONE.includes(value as BoolZeroOne), {
    message: "Expected 0 or 1 for boolean flag."
  })])
  .transform((value) => (typeof value === "number" ? value === 1 : value));

const optionalBoolean = z
  .union([boolish, z.null(), z.undefined()])
  .transform((value) => (value === null || value === undefined ? null : value));

function optionalTrimmedString(label: string, options?: { maxLength?: number }) {
  const maxLength = options?.maxLength ?? 512;
  return z
    .union([z.string(), z.number(), z.null(), z.undefined()])
    .transform((value, ctx) => {
      if (value === null || value === undefined) {
        return null;
      }

      const stringValue = typeof value === "number" ? value.toString() : value;
      const trimmed = stringValue.trim();
      if (trimmed.length === 0) {
        return null;
      }

      if (trimmed.length > maxLength) {
        ctx.addIssue({
          code: z.ZodIssueCode.too_big,
          maximum: maxLength,
          type: "string",
          inclusive: true,
          message: `${label} must be ${maxLength} characters or fewer.`
        });
        return z.NEVER;
      }

      return trimmed;
    });
}

function requiredIdentifier(label: string, options?: { maxLength?: number }) {
  const maxLength = options?.maxLength ?? 256;
  return z
    .string({ required_error: `${label} is required.` })
    .trim()
    .min(1, { message: `${label} is required.` })
    .max(maxLength, { message: `${label} must be ${maxLength} characters or fewer.` });
}

function optionalNonNegativeInt(label: string) {
  return z
    .union([z.number(), z.string(), z.null(), z.undefined()])
    .transform((value, ctx) => {
      if (value === null || value === undefined || value === "") {
        return null;
      }

      const numeric = typeof value === "string" ? Number(value) : value;
      if (!Number.isInteger(numeric)) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: `${label} must be an integer.`
        });
        return z.NEVER;
      }

      if (numeric < 0) {
        ctx.addIssue({
          code: z.ZodIssueCode.too_small,
          minimum: 0,
          type: "number",
          inclusive: true,
          message: `${label} must be zero or positive.`
        });
        return z.NEVER;
      }

      return numeric;
    });
}

function positiveInt(label: string) {
  return z
    .union([z.number(), z.string()])
    .transform((value, ctx) => {
      const numeric = typeof value === "string" ? Number(value) : value;
      if (!Number.isInteger(numeric)) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: `${label} must be an integer.`
        });
        return z.NEVER;
      }

      if (numeric <= 0) {
        ctx.addIssue({
          code: z.ZodIssueCode.too_small,
          minimum: 1,
          type: "number",
          inclusive: true,
          message: `${label} must be greater than zero.`
        });
        return z.NEVER;
      }

      return numeric;
    });
}

function optionalArray<T extends z.ZodTypeAny>(schema: T) {
  return z.union([z.array(schema), z.null(), z.undefined()]).transform((value) => {
    if (Array.isArray(value)) {
      return value;
    }

    return [] as z.infer<T>[];
  });
}

const attributeRealitySchema = z
  .object({
    isNullableInDatabase: optionalBoolean,
    hasNulls: optionalBoolean,
    hasDuplicates: optionalBoolean,
    hasOrphans: optionalBoolean
  })
  .strict()
  .transform(
    (value): AttributeReality => ({
      isNullableInDatabase: value.isNullableInDatabase,
      hasNulls: value.hasNulls,
      hasDuplicates: value.hasDuplicates,
      hasOrphans: value.hasOrphans,
      isPresentButInactive: false
    })
  );

const attributeSchema = z
  .object({
    name: requiredIdentifier("Attribute logical name"),
    physicalName: requiredIdentifier("Attribute physical name"),
    originalName: optionalTrimmedString("Attribute original name"),
    dataType: z
      .string({ required_error: "Attribute dataType is required." })
      .trim()
      .min(1, { message: "Attribute dataType is required." })
      .max(256, { message: "Attribute dataType must be 256 characters or fewer." }),
    length: optionalNonNegativeInt("Attribute length"),
    precision: optionalNonNegativeInt("Attribute precision"),
    scale: optionalNonNegativeInt("Attribute scale"),
    default: optionalTrimmedString("Attribute default value", { maxLength: 4000 }),
    isMandatory: boolish,
    isIdentifier: boolish,
    isAutoNumber: z.union([boolish, z.undefined()]).transform((value) =>
      value === undefined ? false : value
    ),
    isActive: boolish,
    isReference: boolish,
    refEntityId: z.union([z.number().int(), z.string(), z.null(), z.undefined()]).transform((value, ctx) => {
      if (value === null || value === undefined || value === "") {
        return null;
      }

      const numeric = typeof value === "string" ? Number(value) : value;
      if (!Number.isInteger(numeric)) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: "Referenced entity id must be an integer."
        });
        return z.NEVER;
      }

      return numeric;
    }),
    "refEntity.name": optionalTrimmedString("Referenced entity logical name"),
    "refEntity.physicalName": optionalTrimmedString("Referenced entity physical name"),
    "reference.deleteRuleCode": optionalTrimmedString("Reference delete rule code"),
    "reference.hasDbConstraint": z
      .union([boolish, z.null(), z.undefined()])
      .transform((value) => (value === null || value === undefined ? null : value)),
    "external.dbType": optionalTrimmedString("External database type"),
    "physical.isPresentButInactive": boolish,
    reality: z.union([attributeRealitySchema, z.null(), z.undefined()])
  })
  .strict()
  .superRefine((attribute, ctx) => {
    if (attribute.isReference) {
      if (!attribute["refEntity.name"]) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: "Referenced entity logical name is required when isReference is true.",
          path: ["refEntity.name"]
        });
      }

      if (!attribute["refEntity.physicalName"]) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: "Referenced entity physical name is required when isReference is true.",
          path: ["refEntity.physicalName"]
        });
      }
    }
  })
  .transform((attribute) => {
    const isReference = attribute.isReference;

    const reality: AttributeReality = {
      ...(attribute.reality ?? {
        isNullableInDatabase: null,
        hasNulls: null,
        hasDuplicates: null,
        hasOrphans: null
      }),
      isPresentButInactive: attribute["physical.isPresentButInactive"]
    };

    const reference: AttributeReference = isReference
      ? {
          isReference: true,
          targetEntityId: attribute.refEntityId,
          targetEntityName: attribute["refEntity.name"]!,
          targetEntityPhysicalName: attribute["refEntity.physicalName"]!,
          deleteRuleCode: attribute["reference.deleteRuleCode"] ?? null,
          hasDbConstraint: attribute["reference.hasDbConstraint"] === null ? false : attribute["reference.hasDbConstraint"]!
        }
      : {
          isReference: false,
          targetEntityId: null,
          targetEntityName: null,
          targetEntityPhysicalName: null,
          deleteRuleCode: null,
          hasDbConstraint: false
        };

    const normalized: NormalizedAttribute = {
      name: attribute.name,
      physicalName: attribute.physicalName,
      originalName: attribute.originalName,
      dataType: attribute.dataType,
      length: attribute.length,
      precision: attribute.precision,
      scale: attribute.scale,
      defaultValue: attribute.default,
      isMandatory: attribute.isMandatory,
      isIdentifier: attribute.isIdentifier,
      isAutoNumber: attribute.isAutoNumber,
      isActive: attribute.isActive,
      externalDbType: attribute["external.dbType"],
      reference,
      reality
    };

    return normalized;
  });

const indexColumnSchema = z
  .object({
    attribute: requiredIdentifier("Index column attribute name"),
    physicalColumn: requiredIdentifier("Index column physical name"),
    ordinal: positiveInt("Index column ordinal")
  })
  .strict()
  .transform((column) => ({
    attribute: column.attribute,
    physicalColumn: column.physicalColumn,
    ordinal: column.ordinal
  } satisfies NormalizedIndexColumn));

const indexSchema = z
  .object({
    name: requiredIdentifier("Index name"),
    isUnique: boolish,
    isPrimary: z.union([boolish, z.undefined()]).transform((value) =>
      value === undefined ? false : value
    ),
    isPlatformAuto: boolish,
    columns: z.array(indexColumnSchema)
  })
  .strict()
  .superRefine((index, ctx) => {
    const seenOrdinals = new Map<number, number>();
    index.columns.forEach((column, columnIndex) => {
      const existing = seenOrdinals.get(column.ordinal);
      if (existing !== undefined) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: `Duplicate index column ordinal ${column.ordinal}; first seen at index ${existing}.`,
          path: ["columns", columnIndex, "ordinal"]
        });
      } else {
        seenOrdinals.set(column.ordinal, columnIndex);
      }
    });

    if (index.columns.length === 0) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "Index must include at least one column.",
        path: ["columns"]
      });
    }
  })
  .transform((index) => ({
    name: index.name,
    isUnique: index.isUnique,
    isPrimary: index.isPrimary,
    isPlatformAuto: index.isPlatformAuto,
    columns: index.columns
  } satisfies NormalizedIndex));

const relationshipSchema = z
  .object({
    viaAttributeId: z.union([z.number(), z.string(), z.null(), z.undefined()]).optional(),
    viaAttributeName: requiredIdentifier("Relationship attribute name"),
    "toEntity.name": requiredIdentifier("Relationship target entity name"),
    "toEntity.physicalName": requiredIdentifier("Relationship target entity physical name"),
    deleteRuleCode: optionalTrimmedString("Relationship delete rule code"),
    hasDbConstraint: z.union([boolish, z.null(), z.undefined()]).transform((value) =>
      value === null || value === undefined ? false : value
    )
  })
  .strict()
  .transform((relationship) => ({
    viaAttributeName: relationship.viaAttributeName,
    toEntityName: relationship["toEntity.name"],
    toEntityPhysicalName: relationship["toEntity.physicalName"],
    deleteRuleCode:
      relationship.deleteRuleCode && relationship.deleteRuleCode.length > 0
        ? relationship.deleteRuleCode
        : "Ignore",
    hasDbConstraint: relationship.hasDbConstraint
  } satisfies NormalizedRelationship));

function findDuplicates(values: string[], options?: { caseInsensitive?: boolean }) {
  const seen = new Map<string, number>();
  const duplicates: Array<{ value: string; firstIndex: number; duplicateIndex: number }> = [];
  values.forEach((value, index) => {
    const key = options?.caseInsensitive ? value.toLowerCase() : value;
    const existing = seen.get(key);
    if (existing !== undefined) {
      duplicates.push({ value, firstIndex: existing, duplicateIndex: index });
    } else {
      seen.set(key, index);
    }
  });
  return duplicates;
}

const entitySchema = z
  .object({
    name: requiredIdentifier("Entity logical name"),
    physicalName: requiredIdentifier("Entity physical name"),
    isStatic: boolish,
    isExternal: boolish,
    isActive: boolish,
    "db.catalog": optionalTrimmedString("Entity catalog"),
    "db.schema": requiredIdentifier("Entity schema"),
    attributes: z.array(attributeSchema),
    indexes: optionalArray(indexSchema),
    relationships: optionalArray(relationshipSchema)
  })
  .strict()
  .superRefine((entity, ctx) => {
    if (entity.attributes.length === 0) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: `Entity "${entity.name}" must contain at least one attribute.`,
        path: ["attributes"]
      });
    }

    if (!entity.attributes.some((attribute) => attribute.isIdentifier)) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: `Entity "${entity.name}" must include at least one attribute marked as identifier.`,
        path: ["attributes"]
      });
    }

    const logicalDuplicates = findDuplicates(entity.attributes.map((attr) => attr.name));
    logicalDuplicates.forEach((duplicate) => {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: `Duplicate attribute logical name "${duplicate.value}" in entity "${entity.name}" (first at index ${duplicate.firstIndex}).`,
        path: ["attributes", duplicate.duplicateIndex, "name"]
      });
    });

    const physicalDuplicates = findDuplicates(entity.attributes.map((attr) => attr.physicalName), {
      caseInsensitive: true
    });
    physicalDuplicates.forEach((duplicate) => {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: `Duplicate attribute physical name "${duplicate.value}" in entity "${entity.name}" (first at index ${duplicate.firstIndex}).`,
        path: ["attributes", duplicate.duplicateIndex, "physicalName"]
      });
    });
  })
  .transform((entity) => ({
    name: entity.name,
    physicalName: entity.physicalName,
    schema: entity["db.schema"],
    catalog: entity["db.catalog"],
    isStatic: entity.isStatic,
    isExternal: entity.isExternal,
    isActive: entity.isActive,
    attributes: entity.attributes,
    indexes: entity.indexes,
    relationships: entity.relationships
  } satisfies NormalizedEntity));

const moduleSchema = z
  .object({
    name: requiredIdentifier("Module name"),
    isSystem: boolish,
    isActive: boolish,
    entities: z.array(entitySchema)
  })
  .strict()
  .superRefine((module, ctx) => {
    if (module.entities.length === 0) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: `Module "${module.name}" must include at least one entity.`,
        path: ["entities"]
      });
    }

    const logicalDuplicates = findDuplicates(module.entities.map((entity) => entity.name));
    logicalDuplicates.forEach((duplicate) => {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: `Duplicate entity logical name "${duplicate.value}" within module "${module.name}" (first at index ${duplicate.firstIndex}).`,
        path: ["entities", duplicate.duplicateIndex, "name"]
      });
    });

    const physicalDuplicates = findDuplicates(
      module.entities.map((entity) => entity.physicalName),
      { caseInsensitive: true }
    );
    physicalDuplicates.forEach((duplicate) => {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: `Duplicate entity physical name "${duplicate.value}" within module "${module.name}" (first at index ${duplicate.firstIndex}).`,
        path: ["entities", duplicate.duplicateIndex, "physicalName"]
      });
    });
  })
  .transform((module) => ({
    name: module.name,
    isSystem: module.isSystem,
    isActive: module.isActive,
    entities: module.entities
  } satisfies NormalizedModule));

const exportedAtUtcSchema = z
  .union([z.string(), z.date(), z.null(), z.undefined()])
  .transform((value, ctx) => {
    if (value === null || value === undefined) {
      return null;
    }

    if (value instanceof Date) {
      return value;
    }

    const trimmed = value.trim();
    if (trimmed.length === 0) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "exportedAtUtc must be a valid ISO-8601 timestamp when provided."
      });
      return z.NEVER;
    }

    const parsed = new Date(trimmed);
    if (Number.isNaN(parsed.getTime())) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "exportedAtUtc must be a valid ISO-8601 timestamp when provided."
      });
      return z.NEVER;
    }

    return parsed;
  });

export const modelSchema = z
  .object({
    exportedAtUtc: exportedAtUtcSchema.optional(),
    modules: z.array(moduleSchema)
  })
  .strict()
  .superRefine((model, ctx) => {
    if (model.modules.length === 0) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "Model must contain at least one module.",
        path: ["modules"]
      });
    }

    const duplicates = findDuplicates(model.modules.map((module) => module.name), { caseInsensitive: true });
    duplicates.forEach((duplicate) => {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: `Duplicate module name "${duplicate.value}" (first at index ${duplicate.firstIndex}).`,
        path: ["modules", duplicate.duplicateIndex, "name"]
      });
    });
  })
  .transform((model) => ({
    exportedAtUtc: model.exportedAtUtc ?? null,
    modules: model.modules
  } satisfies NormalizedModel));

export function parseModelJson(input: unknown): NormalizedModel {
  return modelSchema.parse(input);
}

export function safeParseModelJson(input: unknown) {
  return modelSchema.safeParse(input);
}

function formatIssuePath(path: (string | number)[]): string {
  if (path.length === 0) {
    return "";
  }

  return path
    .map((segment, index) => {
      if (typeof segment === "number") {
        return `[${segment}]`;
      }

      return index === 0 ? segment : `.${segment}`;
    })
    .join("");
}

export function formatZodErrors(error: ZodError): string[] {
  return error.issues.map((issue) => {
    const path = formatIssuePath(issue.path);
    return path ? `${path}: ${issue.message}` : issue.message;
  });
}

export function summarizeModel(model: NormalizedModel): string[] {
  const lines: string[] = [];
  lines.push(
    model.exportedAtUtc
      ? `Export timestamp: ${model.exportedAtUtc.toISOString()}`
      : "Export timestamp: <not provided>"
  );
  lines.push(`Modules: ${model.modules.length}`);
  model.modules.forEach((module, moduleIndex) => {
    lines.push(
      `  ${moduleIndex + 1}. ${module.name} (system: ${module.isSystem ? "yes" : "no"}, active: ${
        module.isActive ? "yes" : "no"
      }, entities: ${module.entities.length})`
    );
    module.entities.forEach((entity) => {
      lines.push(
        `     • ${entity.name} / ${entity.physicalName} — attributes: ${entity.attributes.length}, indexes: ${entity.indexes.length}, relationships: ${entity.relationships.length}`
      );
    });
  });

  return lines;
}

