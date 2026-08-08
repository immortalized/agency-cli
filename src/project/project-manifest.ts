import type { ProjectNames } from "./project-names.js";
import type { ProjectCapability } from "./project-capability.js";

export interface ProjectManifest {
  generatorVersion: string;
  project: ProjectNames;
  capabilities: ProjectCapability[];
  modules: string[];
}
