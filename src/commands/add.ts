import process from "node:process";
import { findModuleTemplate } from "../modules/module-locator.js";
import { readModuleManifest } from "../modules/module-manifest-reader.js";
import { installModule } from "../modules/module-installer.js";
import { validateModuleRequirements } from "../modules/module-requirements.js";
import { findProjectRoot } from "../project/project-locator.js";
import { readProjectManifest } from "../project/project-manifest-reader.js";

export async function addCommand(args: string[]): Promise<void> {
  const requestedModuleName = args[0]?.trim().toLowerCase();

  if (!requestedModuleName) {
    throw new Error("Module name is required. Example: agency add menu");
  }

  if (args.length > 1) {
    throw new Error("The add command accepts exactly one module name.");
  }

  const startDirectory = process.env.INIT_CWD ?? process.cwd();
  const projectRoot = await findProjectRoot(startDirectory);
  const projectManifest = await readProjectManifest(projectRoot);

  if (projectManifest.modules.includes(requestedModuleName)) {
    throw new Error(`Module '${requestedModuleName}' is already installed.`);
  }

  const moduleDirectory = await findModuleTemplate(requestedModuleName);
  const moduleManifest = await readModuleManifest(moduleDirectory);

  if (moduleManifest.name !== requestedModuleName) {
    throw new Error(
      `Module directory '${requestedModuleName}' contains manifest for '${moduleManifest.name}'.`,
    );
  }

  validateModuleRequirements(
    moduleManifest,
    projectManifest,
  );

  console.log(`Installing module '${requestedModuleName}'...`);

  await installModule(
    projectRoot,
    projectManifest,
    moduleDirectory,
    moduleManifest,
  );

  console.log(`Module '${requestedModuleName}' installed successfully.`);
  console.log(`Project: ${projectManifest.project.displayName}`);
}
