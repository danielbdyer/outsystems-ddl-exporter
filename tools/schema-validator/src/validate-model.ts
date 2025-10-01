#!/usr/bin/env node
import fs from "node:fs/promises";
import path from "node:path";
import process from "node:process";

import { ZodError } from "zod";

import {
  formatZodErrors,
  parseModelJson,
  summarizeModel,
  type NormalizedModel
} from "./modelSchema.js";

interface CliOptions {
  readonly filePath: string;
  readonly printNormalized: boolean;
}

function parseArguments(argv: string[]): CliOptions | null {
  let filePath: string | undefined;
  let printNormalized = false;

  for (const argument of argv) {
    if (argument === "--help" || argument === "-h") {
      return null;
    }

    if (argument === "--print-normalized" || argument === "-p") {
      printNormalized = true;
      continue;
    }

    if (argument.startsWith("-")) {
      throw new Error(`Unknown option: ${argument}`);
    }

    if (filePath) {
      throw new Error("Only one JSON file path can be supplied.");
    }

    filePath = argument;
  }

  if (!filePath) {
    throw new Error("Path to the emitted OutSystems model JSON is required.");
  }

  return { filePath, printNormalized };
}

function printUsage(): void {
  console.log(`Usage: npm run validate -- <model.json> [options]\n\n` +
    `Options:\n` +
    `  -h, --help              Show this message.\n` +
    `  -p, --print-normalized  Print the normalized JSON after validation.\n` +
    `\nExamples:\n` +
    `  npm run validate -- tests/Fixtures/model.edge-case.json\n` +
    `  npm run validate -- my-export.json --print-normalized\n`);
}

async function loadJson(filePath: string): Promise<unknown> {
  const absolutePath = path.resolve(process.cwd(), filePath);
  const payload = await fs.readFile(absolutePath, "utf8");
  try {
    return JSON.parse(payload);
  } catch (error) {
    if (error instanceof SyntaxError) {
      throw new Error(`Failed to parse JSON from ${absolutePath}: ${error.message}`);
    }

    throw error;
  }
}

function printSummary(model: NormalizedModel): void {
  const lines = summarizeModel(model);
  for (const line of lines) {
    console.log(line);
  }
}

async function main(): Promise<void> {
  let options: CliOptions | null;
  try {
    options = parseArguments(process.argv.slice(2));
  } catch (error) {
    if (error instanceof Error) {
      console.error(`✖ ${error.message}`);
    } else {
      console.error("✖ Unable to read CLI arguments.");
    }
    printUsage();
    process.exitCode = 1;
    return;
  }

  if (options === null) {
    printUsage();
    return;
  }

  let rawModel: unknown;
  try {
    rawModel = await loadJson(options.filePath);
  } catch (error) {
    if (error instanceof Error) {
      console.error(`✖ ${error.message}`);
    } else {
      console.error("✖ Unable to load the JSON file.");
    }
    process.exitCode = 1;
    return;
  }

  let model: NormalizedModel;
  try {
    model = parseModelJson(rawModel);
  } catch (error) {
    if (error instanceof ZodError) {
      const issues = formatZodErrors(error);
      console.error(`✖ Schema validation failed with ${issues.length} issue${issues.length === 1 ? "" : "s"}:`);
      issues.forEach((issue, index) => {
        console.error(`  ${index + 1}. ${issue}`);
      });
    } else if (error instanceof Error) {
      console.error(`✖ ${error.message}`);
    } else {
      console.error("✖ Unexpected error during validation.", error);
    }
    process.exitCode = 1;
    return;
  }

  console.log("✅ Model JSON matches the OutSystems 11 contract.");
  printSummary(model);

  if (options.printNormalized) {
    console.log("\nNormalized projection:\n");
    const replacer = (_key: string, value: unknown) =>
      value instanceof Date ? value.toISOString() : value;
    console.log(JSON.stringify(model, replacer, 2));
  }
}

main().catch((error) => {
  console.error("✖ Unhandled error:", error);
  process.exit(1);
});

