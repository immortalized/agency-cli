import type { ProjectCapability } from "../project/project-capability.js";

export const modulePackageProjects = [
  "Api",
  "Application",
  "Domain",
  "Infrastructure",
] as const;

export type ModulePackageProject =
  (typeof modulePackageProjects)[number];

export interface ModuleMigration {
  name: string;
}

export interface ModulePackage {
  project: ModulePackageProject;
  name: string;
  version: string;
}

export interface ModuleSecretRename {
  from: string;
  to: string;
}

export interface ModuleSecretOperations {
  rename?: ModuleSecretRename[];
  generate?: string[];
}

export interface ModuleManifest {
  name: string;
  version: string;
  dependencies: string[];
  requiredCapabilities: ProjectCapability[];
  replaces?: string[];
  removes?: string[];
  secrets?: ModuleSecretOperations;
  packages?: ModulePackage[];
  migration?: ModuleMigration;
}
