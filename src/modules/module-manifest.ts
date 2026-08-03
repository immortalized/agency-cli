export interface ModuleMigration {
  name: string;
}

export interface ModuleManifest {
  name: string;
  version: string;
  dependencies: string[];
  migration?: ModuleMigration;
}