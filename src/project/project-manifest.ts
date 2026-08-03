import type { ProjectNames } from "./project-names.js";

export interface ProjectManifest {
  generatorVersion: string;
  project: ProjectNames;
  modules: string[];
}