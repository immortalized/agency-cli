import { readFile } from "node:fs/promises";
import {
  modulePackageProjects,
  type ModuleManifest,
  type ModuleMigration,
  type ModulePackage,
  type ModulePackageProject,
  type ModuleSecretOperations,
  type ModuleSecretRename,
} from "./module-manifest.js";
import path from "node:path";
import {
  isProjectCapability,
  type ProjectCapability,
} from "../project/project-capability.js";

function isNonEmptyString(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0;
}

function isStringArray(value: unknown): value is string[] {
  return (
    Array.isArray(value) &&
    value.every((item) => isNonEmptyString(item))
  );
}

function isCapabilityArray(
  value: unknown,
): value is ProjectCapability[] {
  return (
    Array.isArray(value) &&
    value.every((item) => isProjectCapability(item)) &&
    new Set(value).size === value.length
  );
}

function isSafeRelativePath(value: unknown): value is string {
  return (
    isNonEmptyString(value) &&
    !path.isAbsolute(value) &&
    !value.split(/[\\/]/).includes("..")
  );
}

function isPathArray(value: unknown): value is string[] {
  return (
    Array.isArray(value) &&
    value.every((item) => isSafeRelativePath(item)) &&
    new Set(value).size === value.length
  );
}

function isSecretRename(
  value: unknown,
): value is ModuleSecretRename {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const rename = value as Record<string, unknown>;

  return (
    isSafeRelativePath(rename.from) &&
    isSafeRelativePath(rename.to) &&
    rename.from !== rename.to
  );
}

function isSecretOperations(
  value: unknown,
): value is ModuleSecretOperations {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const operations = value as Record<string, unknown>;

  return (
    (operations.rename === undefined ||
      (Array.isArray(operations.rename) &&
        operations.rename.every((item) =>
          isSecretRename(item),
        ))) &&
    (operations.generate === undefined ||
      isPathArray(operations.generate))
  );
}

function isModuleMigration(value: unknown): value is ModuleMigration {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const migration = value as Record<string, unknown>;

  return isNonEmptyString(migration.name);
}

function isModulePackageProject(
  value: unknown,
): value is ModulePackageProject {
  return (
    typeof value === "string" &&
    modulePackageProjects.some(
      (project) => project === value,
    )
  );
}

function isModulePackage(value: unknown): value is ModulePackage {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const packageReference = value as Record<string, unknown>;

  return (
    isModulePackageProject(packageReference.project) &&
    isNonEmptyString(packageReference.name) &&
    isNonEmptyString(packageReference.version)
  );
}

function isModulePackageArray(
  value: unknown,
): value is ModulePackage[] {
  return (
    Array.isArray(value) &&
    value.every((item) => isModulePackage(item))
  );
}

function hasDuplicatePackages(
  packages: readonly ModulePackage[],
): boolean {
  const packageKeys = new Set<string>();

  for (const packageReference of packages) {
    const key = [
      packageReference.project.toLowerCase(),
      packageReference.name.toLowerCase(),
    ].join(":");

    if (packageKeys.has(key)) {
      return true;
    }

    packageKeys.add(key);
  }

  return false;
}

function isModuleManifest(value: unknown): value is ModuleManifest {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const manifest = value as Record<string, unknown>;

  if (
    !isNonEmptyString(manifest.name) ||
    !isNonEmptyString(manifest.version) ||
    !isStringArray(manifest.dependencies) ||
    !isCapabilityArray(manifest.requiredCapabilities)
  ) {
    return false;
  }

  if (
    manifest.replaces !== undefined &&
    !isPathArray(manifest.replaces)
  ) {
    return false;
  }

  if (
    manifest.removes !== undefined &&
    !isPathArray(manifest.removes)
  ) {
    return false;
  }

  if (
    manifest.secrets !== undefined &&
    !isSecretOperations(manifest.secrets)
  ) {
    return false;
  }

  if (
    manifest.packages !== undefined &&
    !isModulePackageArray(manifest.packages)
  ) {
    return false;
  }

  if (
    Array.isArray(manifest.packages) &&
    hasDuplicatePackages(manifest.packages)
  ) {
    return false;
  }

  if (
    manifest.migration !== undefined &&
    !isModuleMigration(manifest.migration)
  ) {
    return false;
  }

  return true;
}

export async function readModuleManifest(
  moduleDirectory: string,
): Promise<ModuleManifest> {
  const manifestPath = path.join(
    moduleDirectory,
    "module.json",
  );

  let content: string;

  try {
    content = await readFile(manifestPath, "utf8");
  } catch {
    throw new Error(
      `Unable to read module manifest: ${manifestPath}`,
    );
  }

  let parsedManifest: unknown;

  try {
    parsedManifest = JSON.parse(content);
  } catch {
    throw new Error(
      `Module manifest contains invalid JSON: ${manifestPath}`,
    );
  }

  if (!isModuleManifest(parsedManifest)) {
    throw new Error(
      `Module manifest has an invalid structure: ${manifestPath}`,
    );
  }

  return parsedManifest;
}
