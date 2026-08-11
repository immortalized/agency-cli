import path from "node:path";
import { readFile } from "node:fs/promises";
import type { ProjectManifest } from "./project-manifest.js";
import {
  isProjectCapability,
  normalizeProjectCapabilities,
  type ProjectCapability,
} from "./project-capability.js";

function isNonEmptyString(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0;
}

function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every((item) => typeof item === "string");
}

function isCapabilityArray(
  value: unknown,
): value is ProjectCapability[] {
  if (
    !Array.isArray(value) ||
    !value.every((item) => isProjectCapability(item))
  ) {
    return false;
  }

  const capabilities = new Set(value);
  const normalized = normalizeProjectCapabilities(
    capabilities,
  );

  return (
    capabilities.size === value.length &&
    normalized.length === value.length &&
    normalized.every(
      (capability, index) => capability === value[index],
    )
  );
}

function isProjectNames(value: unknown): boolean {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const project = value as Record<string, unknown>;

  return (
    isNonEmptyString(project.displayName) &&
    isNonEmptyString(project.slug) &&
    isNonEmptyString(project.namespace) &&
    isNonEmptyString(project.databaseName)
  );
}

function isModuleOptions(value: unknown): boolean {
  if (value === undefined) {
    return true;
  }

  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return false;
  }

  return Object.values(value).every((moduleValue) =>
    typeof moduleValue === "object" &&
    moduleValue !== null &&
    !Array.isArray(moduleValue) &&
    Object.values(moduleValue).every((optionValue) =>
      typeof optionValue === "string" ||
      typeof optionValue === "number" ||
      typeof optionValue === "boolean",
    ),
  );
}

function isProjectManifest(value: unknown): value is ProjectManifest {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const manifest = value as Record<string, unknown>;

  return (
    isNonEmptyString(manifest.generatorVersion) &&
    isProjectNames(manifest.project) &&
    isCapabilityArray(manifest.capabilities) &&
    isStringArray(manifest.modules) &&
    isModuleOptions(manifest.moduleOptions)
  );
}

export async function readProjectManifest(projectRoot: string): Promise<ProjectManifest> {
  const manifestPath = path.join(projectRoot, ".agency.json");

  let content: string;

  try {
    content = await readFile(manifestPath, "utf8");
  } catch {
    throw new Error(`Unable to read project manifest: ${manifestPath}`);
  }

  let parsedManifest: unknown;

  try {
    parsedManifest = JSON.parse(content);
  } catch {
    throw new Error(`Project manifest contains invalid JSON: ${manifestPath}`);
  }

  if (!isProjectManifest(parsedManifest)) {
    throw new Error(`Project manifest has an invalid structure: ${manifestPath}`);
  }

  return parsedManifest;
}
