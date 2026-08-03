import path from "node:path";
import { readFile } from "node:fs/promises";
import type { ProjectManifest } from "./project-manifest.js";

function isNonEmptyString(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0;
}

function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every((item) => typeof item === "string");
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

function isProjectManifest(value: unknown): value is ProjectManifest {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const manifest = value as Record<string, unknown>;

  return (
    isNonEmptyString(manifest.generatorVersion) &&
    isProjectNames(manifest.project) &&
    isStringArray(manifest.modules)
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