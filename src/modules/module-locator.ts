import path from "node:path";
import { access } from "node:fs/promises";
import { fileURLToPath } from "node:url";

async function directoryExists(directory: string): Promise<boolean> {
  try {
    await access(directory);
    return true;
  } catch {
    return false;
  }
}

export async function findModuleTemplate(moduleName: string): Promise<string> {
  const currentFilePath = fileURLToPath(import.meta.url);
  const currentDirectory = path.dirname(currentFilePath);

  const moduleDirectory = path.resolve(
    currentDirectory,
    "../../templates/modules",
    moduleName,
  );

  if (!(await directoryExists(moduleDirectory))) {
    throw new Error(`Unknown module '${moduleName}'.`);
  }

  return moduleDirectory;
}