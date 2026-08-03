import path from "node:path";
import {
  access,
  copyFile,
  cp,
  mkdir,
  mkdtemp,
  readdir,
  rm,
} from "node:fs/promises";
import type { ModuleManifest } from "./module-manifest.js";
import type { ProjectManifest } from "../project/project-manifest.js";
import { replaceTokensInDirectory } from "../infrastructure/token-replacer.js";
import { validateGeneratedTemplate } from "../infrastructure/template-validator.js";
import { writeProjectManifest } from "../project/project-manifest-writer.js";
import {
  generateModuleMigration,
  getMigrationsDirectory,
} from "./module-migration-generator.js";

interface DirectoryBackup {
  originalDirectory: string;
  backupDirectory: string;
  existed: boolean;
}

async function pathExists(targetPath: string): Promise<boolean> {
  try {
    await access(targetPath);
    return true;
  } catch {
    return false;
  }
}

async function collectRelativeFiles(
  directory: string,
  relativeDirectory = "",
): Promise<string[]> {
  const currentDirectory = path.join(directory, relativeDirectory);
  const entries = await readdir(currentDirectory, { withFileTypes: true });
  const files: string[] = [];

  for (const entry of entries) {
    const relativePath = path.join(relativeDirectory, entry.name);

    if (entry.isDirectory()) {
      files.push(...(await collectRelativeFiles(directory, relativePath)));
      continue;
    }

    if (entry.isFile()) {
      files.push(relativePath);
    }
  }

  return files;
}

async function ensureDirectory(
  directory: string,
  projectRoot: string,
  createdDirectories: string[],
): Promise<void> {
  if (await pathExists(directory)) {
    return;
  }

  const parentDirectory = path.dirname(directory);

  if (directory !== projectRoot && parentDirectory !== directory) {
    await ensureDirectory(parentDirectory, projectRoot, createdDirectories);
  }

  await mkdir(directory);
  createdDirectories.push(directory);
}

async function createDirectoryBackup(
  originalDirectory: string,
  backupDirectory: string,
): Promise<DirectoryBackup> {
  const existed = await pathExists(originalDirectory);

  if (existed) {
    await cp(originalDirectory, backupDirectory, { recursive: true });
  }

  return {
    originalDirectory,
    backupDirectory,
    existed,
  };
}

async function restoreDirectoryBackup(backup: DirectoryBackup): Promise<void> {
  await rm(backup.originalDirectory, {
    recursive: true,
    force: true,
  });

  if (backup.existed) {
    await cp(
      backup.backupDirectory,
      backup.originalDirectory,
      { recursive: true },
    );
  }
}

async function rollbackInstalledFiles(
  createdFiles: string[],
  createdDirectories: string[],
): Promise<void> {
  for (const filePath of [...createdFiles].reverse()) {
    await rm(filePath, { force: true });
  }

  for (const directory of [...createdDirectories].reverse()) {
    try {
      await rm(directory);
    } catch {
      // The directory is not empty or existed before installation.
    }
  }
}

function createTemplateTokens(
  manifest: ProjectManifest,
): Readonly<Record<string, string>> {
  return {
    __PROJECT_DISPLAY_NAME__: manifest.project.displayName,
    __PROJECT_SLUG__: manifest.project.slug,
    __PROJECT_NAMESPACE__: manifest.project.namespace,
    __DATABASE_NAME__: manifest.project.databaseName,
  };
}

function validateDependencies(
  moduleManifest: ModuleManifest,
  projectManifest: ProjectManifest,
): void {
  const missingDependencies = moduleManifest.dependencies.filter(
    (dependency) => !projectManifest.modules.includes(dependency),
  );

  if (missingDependencies.length > 0) {
    throw new Error(
      `Module '${moduleManifest.name}' requires missing modules: ${missingDependencies.join(", ")}.`,
    );
  }
}

export async function installModule(
  projectRoot: string,
  projectManifest: ProjectManifest,
  moduleDirectory: string,
  moduleManifest: ModuleManifest,
): Promise<void> {
  validateDependencies(moduleManifest, projectManifest);

  const moduleFilesDirectory = path.join(moduleDirectory, "files");

  if (!(await pathExists(moduleFilesDirectory))) {
    throw new Error(
      `Module '${moduleManifest.name}' does not contain a files directory.`,
    );
  }

  const transactionDirectory = await mkdtemp(
    path.join(projectRoot, `.agency-${moduleManifest.name}-`),
  );

  const stagedFilesDirectory = path.join(
    transactionDirectory,
    "files",
  );

  const migrationBackupDirectory = path.join(
    transactionDirectory,
    "migrations-backup",
  );

  const createdFiles: string[] = [];
  const createdDirectories: string[] = [];
  let migrationBackup: DirectoryBackup | undefined;

  try {
    await cp(moduleFilesDirectory, stagedFilesDirectory, {
      recursive: true,
    });

    await replaceTokensInDirectory(
      stagedFilesDirectory,
      createTemplateTokens(projectManifest),
    );

    await validateGeneratedTemplate(stagedFilesDirectory);

    const relativeFiles = await collectRelativeFiles(
      stagedFilesDirectory,
    );

    for (const relativeFile of relativeFiles) {
      const destinationPath = path.join(
        projectRoot,
        relativeFile,
      );

      if (await pathExists(destinationPath)) {
        throw new Error(
          `Module '${moduleManifest.name}' cannot be installed because the following path already exists: ${relativeFile}`,
        );
      }
    }

    for (const relativeFile of relativeFiles) {
      const sourcePath = path.join(
        stagedFilesDirectory,
        relativeFile,
      );

      const destinationPath = path.join(
        projectRoot,
        relativeFile,
      );

      const destinationDirectory = path.dirname(destinationPath);

      await ensureDirectory(
        destinationDirectory,
        projectRoot,
        createdDirectories,
      );

      await copyFile(sourcePath, destinationPath);
      createdFiles.push(destinationPath);
    }

    if (moduleManifest.migration) {
      const migrationsDirectory = getMigrationsDirectory(
        projectRoot,
        projectManifest,
      );

      migrationBackup = await createDirectoryBackup(
        migrationsDirectory,
        migrationBackupDirectory,
      );

      await generateModuleMigration(
        projectRoot,
        projectManifest,
        moduleManifest.migration.name,
      );
    }

    const updatedManifest: ProjectManifest = {
      ...projectManifest,
      modules: [
        ...projectManifest.modules,
        moduleManifest.name,
      ],
    };

    await writeProjectManifest(projectRoot, updatedManifest);
  } catch (error) {
    if (migrationBackup) {
      await restoreDirectoryBackup(migrationBackup);
    }

    await rollbackInstalledFiles(
      createdFiles,
      createdDirectories,
    );

    throw error;
  } finally {
    await rm(transactionDirectory, {
      recursive: true,
      force: true,
    });
  }
}