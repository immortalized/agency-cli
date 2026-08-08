import type { ModuleManifest } from "./module-manifest.js";
import type { ProjectManifest } from "../project/project-manifest.js";

export function validateModuleRequirements(
  moduleManifest: ModuleManifest,
  projectManifest: ProjectManifest,
): void {
  const missingCapabilities =
    moduleManifest.requiredCapabilities.filter(
      (capability) =>
        !projectManifest.capabilities.includes(capability),
    );

  if (missingCapabilities.length > 0) {
    throw new Error(
      `Module '${moduleManifest.name}' requires missing project capabilities: ${missingCapabilities.join(", ")}.`,
    );
  }

  const missingDependencies =
    moduleManifest.dependencies.filter(
      (dependency) =>
        !projectManifest.modules.includes(dependency),
    );

  if (missingDependencies.length > 0) {
    throw new Error(
      `Module '${moduleManifest.name}' requires missing modules: ${missingDependencies.join(", ")}.`,
    );
  }
}
