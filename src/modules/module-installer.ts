import path from "node:path";
import {
  access,
  chmod,
  copyFile,
  cp,
  mkdir,
  mkdtemp,
  readdir,
  rename,
  rm,
  writeFile,
} from "node:fs/promises";
import { randomBytes } from "node:crypto";
import { setTimeout as delay } from "node:timers/promises";
import type { ModuleManifest } from "./module-manifest.js";
import type { ProjectManifest } from "../project/project-manifest.js";
import { replaceTokensInDirectory } from "../infrastructure/token-replacer.js";
import { validateGeneratedTemplate } from "../infrastructure/template-validator.js";
import { writeProjectManifest } from "../project/project-manifest-writer.js";
import {
  generateModuleMigration,
  getMigrationsDirectory,
} from "./module-migration-generator.js";
import {
  installModulePackages,
  restoreProjectFileBackups,
  type ProjectFileBackup,
} from "./module-package-installer.js";
import { validateModuleRequirements } from "./module-requirements.js";

interface DirectoryBackup {
  originalDirectory: string;
  backupDirectory: string;
  existed: boolean;
}

interface FileBackup {
  originalFile: string;
  backupFile: string;
}

interface AppliedSecretOperations {
  renamed: Array<{
    from: string;
    to: string;
  }>;
  generated: string[];
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

async function removePathWithRetries(
  targetPath: string,
  options: {
    recursive: boolean;
    force: boolean;
  },
): Promise<void> {
  const maximumAttempts = 10;

  for (
    let attempt = 1;
    attempt <= maximumAttempts;
    attempt += 1
  ) {
    try {
      await rm(targetPath, {
        recursive: options.recursive,
        force: options.force,
        maxRetries: 3,
        retryDelay: 200,
      });

      return;
    } catch (error) {
      const isLastAttempt =
        attempt === maximumAttempts;

      if (isLastAttempt) {
        throw error;
      }

      await delay(attempt * 250);
    }
  }
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

async function restoreDirectoryBackup(
  backup: DirectoryBackup,
): Promise<void> {
  await removePathWithRetries(
    backup.originalDirectory,
    {
      recursive: true,
      force: true,
    },
  );

  if (backup.existed) {
    await cp(
      backup.backupDirectory,
      backup.originalDirectory,
      {
        recursive: true,
      },
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

async function restoreFileBackups(
  backups: readonly FileBackup[],
): Promise<void> {
  for (const backup of [...backups].reverse()) {
    await copyFile(
      backup.backupFile,
      backup.originalFile,
    );
  }
}

function createTemplateTokens(
  manifest: ProjectManifest,
  moduleOptions: Readonly<Record<string, string | number | boolean>> = {},
): Readonly<Record<string, string>> {
  return {
    __PROJECT_DISPLAY_NAME__: manifest.project.displayName,
    __PROJECT_SLUG__: manifest.project.slug,
    __PROJECT_NAMESPACE__: manifest.project.namespace,
    __DATABASE_NAME__: manifest.project.databaseName,
    __UNSEAL_STRATEGY__: String(moduleOptions.unsealStrategy ?? ""),
    __UNSEAL_KEY_SHARES__: String(moduleOptions.unsealShares ?? "0"),
    __UNSEAL_KEY_THRESHOLD__: String(moduleOptions.unsealThreshold ?? "0"),
  };
}

function resolveManifestPath(
  configuredPath: string,
  projectManifest: ProjectManifest,
): string {
  let result = configuredPath;

  for (const [token, value] of Object.entries(
    createTemplateTokens(projectManifest),
  )) {
    result = result.replaceAll(token, value);
  }

  return path.normalize(result);
}

function resolveManifestPaths(
  configuredPaths: readonly string[] | undefined,
  projectManifest: ProjectManifest,
): Set<string> {
  return new Set(
    (configuredPaths ?? []).map((configuredPath) =>
      resolveManifestPath(
        configuredPath,
        projectManifest,
      ),
    ),
  );
}

async function applySecretOperations(
  projectRoot: string,
  projectManifest: ProjectManifest,
  moduleManifest: ModuleManifest,
  applied: AppliedSecretOperations,
): Promise<void> {

  for (const operation of
    moduleManifest.secrets?.rename ?? []) {
    const from = path.join(
      projectRoot,
      resolveManifestPath(
        operation.from,
        projectManifest,
      ),
    );

    const to = path.join(
      projectRoot,
      resolveManifestPath(
        operation.to,
        projectManifest,
      ),
    );

    await rename(from, to);
    applied.renamed.push({ from, to });
  }

  for (const configuredPath of
    moduleManifest.secrets?.generate ?? []) {
    const filePath = path.join(
      projectRoot,
      resolveManifestPath(
        configuredPath,
        projectManifest,
      ),
    );

    await writeFile(
      filePath,
      `${randomBytes(48).toString("base64url")}\n`,
      {
        encoding: "utf8",
        flag: "wx",
      },
    );

    if (process.platform !== "win32") {
      await chmod(filePath, 0o600);
    }

    applied.generated.push(filePath);
  }

}

async function rollbackSecretOperations(
  applied: AppliedSecretOperations,
): Promise<void> {
  for (const generated of [...applied.generated].reverse()) {
    await rm(generated, { force: true });
  }

  for (const operation of [...applied.renamed].reverse()) {
    await rename(operation.to, operation.from);
  }
}

export async function installModule(
  projectRoot: string,
  projectManifest: ProjectManifest,
  moduleDirectory: string,
  moduleManifest: ModuleManifest,
  moduleOptions: Readonly<Record<string, string | number | boolean>> = {},
): Promise<void> {
  validateModuleRequirements(
    moduleManifest,
    projectManifest,
  );

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

  const projectFileBackupDirectory = path.join(
    transactionDirectory,
    "project-files-backup",
  );

  const changedFileBackupDirectory = path.join(
    transactionDirectory,
    "changed-files-backup",
  );

  const createdFiles: string[] = [];
  const createdDirectories: string[] = [];
  let migrationBackup: DirectoryBackup | undefined;
  let projectFileBackups: ProjectFileBackup[] = [];
  const changedFileBackups: FileBackup[] = [];
  let appliedSecrets: AppliedSecretOperations = {
    renamed: [],
    generated: [],
  };

  try {
    await cp(moduleFilesDirectory, stagedFilesDirectory, {
      recursive: true,
    });

    const strategiesDirectory = path.join(
      stagedFilesDirectory,
      ".strategies",
    );

    if (await pathExists(strategiesDirectory)) {
      const selectedStrategy = moduleOptions.unsealStrategy;

      if (typeof selectedStrategy !== "string") {
        throw new Error(
          `Module '${moduleManifest.name}' requires an unseal strategy.`,
        );
      }

      const selectedDirectory = path.join(
        strategiesDirectory,
        selectedStrategy,
      );

      if (!(await pathExists(selectedDirectory))) {
        throw new Error(
          `Module '${moduleManifest.name}' does not provide strategy '${selectedStrategy}'.`,
        );
      }

      await cp(selectedDirectory, stagedFilesDirectory, {
        recursive: true,
        force: true,
      });

      await rm(strategiesDirectory, {
        recursive: true,
        force: true,
      });
    }

    await replaceTokensInDirectory(
      stagedFilesDirectory,
      createTemplateTokens(projectManifest, moduleOptions),
    );

    await validateGeneratedTemplate(stagedFilesDirectory);

    const relativeFiles = await collectRelativeFiles(
      stagedFilesDirectory,
    );

    const replacements = resolveManifestPaths(
      moduleManifest.replaces,
      projectManifest,
    );

    const removals = resolveManifestPaths(
      moduleManifest.removes,
      projectManifest,
    );

    for (const replacement of replacements) {
      if (!relativeFiles.includes(replacement)) {
        throw new Error(
          `Module '${moduleManifest.name}' declares replacement '${replacement}' but does not provide that file.`,
        );
      }

      if (!(await pathExists(
          path.join(projectRoot, replacement)))) {
        throw new Error(
          `Module '${moduleManifest.name}' cannot replace missing path: ${replacement}`,
        );
      }
    }

    for (const removal of removals) {
      if (!(await pathExists(
          path.join(projectRoot, removal)))) {
        throw new Error(
          `Module '${moduleManifest.name}' cannot remove missing path: ${removal}`,
        );
      }
    }

    for (const operation of
      moduleManifest.secrets?.rename ?? []) {
      const from = path.join(
        projectRoot,
        resolveManifestPath(
          operation.from,
          projectManifest,
        ),
      );

      const to = path.join(
        projectRoot,
        resolveManifestPath(
          operation.to,
          projectManifest,
        ),
      );

      if (!(await pathExists(from))) {
        throw new Error(
          `Module '${moduleManifest.name}' requires missing secret: ${operation.from}`,
        );
      }

      if (await pathExists(to)) {
        throw new Error(
          `Module '${moduleManifest.name}' cannot create secret because it already exists: ${operation.to}`,
        );
      }
    }

    for (const configuredPath of
      moduleManifest.secrets?.generate ?? []) {
      const generatedPath = path.join(
        projectRoot,
        resolveManifestPath(
          configuredPath,
          projectManifest,
        ),
      );

      if (await pathExists(generatedPath)) {
        throw new Error(
          `Module '${moduleManifest.name}' cannot create secret because it already exists: ${configuredPath}`,
        );
      }
    }

    for (const relativeFile of relativeFiles) {
      const destinationPath = path.join(
        projectRoot,
        relativeFile,
      );

      if (
        await pathExists(destinationPath) &&
        !replacements.has(relativeFile)
      ) {
        throw new Error(
          `Module '${moduleManifest.name}' cannot be installed because the following path already exists: ${relativeFile}`,
        );
      }
    }

    await mkdir(changedFileBackupDirectory, {
      recursive: true,
    });

    for (const [index, relativeFile] of
      relativeFiles.entries()) {
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

      if (replacements.has(relativeFile)) {
        const backupFile = path.join(
          changedFileBackupDirectory,
          `replacement-${index}`,
        );

        await copyFile(destinationPath, backupFile);
        changedFileBackups.push({
          originalFile: destinationPath,
          backupFile,
        });
      }

      await copyFile(sourcePath, destinationPath);

      if (!replacements.has(relativeFile)) {
        createdFiles.push(destinationPath);
      }
    }

    for (const [index, relativePath] of
      [...removals].entries()) {
      const originalFile = path.join(
        projectRoot,
        relativePath,
      );

      const backupFile = path.join(
        changedFileBackupDirectory,
        `removal-${index}`,
      );

      await copyFile(originalFile, backupFile);
      changedFileBackups.push({
        originalFile,
        backupFile,
      });

      await rm(originalFile);
    }

    await applySecretOperations(
      projectRoot,
      projectManifest,
      moduleManifest,
      appliedSecrets,
    );

    if (
      moduleManifest.packages &&
      moduleManifest.packages.length > 0
    ) {
      await mkdir(projectFileBackupDirectory, {
        recursive: true,
      });

      projectFileBackups = await installModulePackages(
        projectRoot,
        projectManifest,
        moduleManifest.packages,
        projectFileBackupDirectory,
      );
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
      ...(Object.keys(moduleOptions).length > 0
        ? {
            moduleOptions: {
              ...projectManifest.moduleOptions,
              [moduleManifest.name]: { ...moduleOptions },
            },
          }
        : projectManifest.moduleOptions
          ? { moduleOptions: projectManifest.moduleOptions }
          : {}),
    };

    await writeProjectManifest(projectRoot, updatedManifest);
  } catch (error) {
    await rollbackSecretOperations(appliedSecrets);

    if (migrationBackup) {
      await restoreDirectoryBackup(migrationBackup);
    }

    if (projectFileBackups.length > 0) {
      await restoreProjectFileBackups(
        projectFileBackups,
      );
    }

    await restoreFileBackups(changedFileBackups);

    await rollbackInstalledFiles(
      createdFiles,
      createdDirectories,
    );

    throw error;
  } finally {
    await rm(transactionDirectory, {
      recursive: true,
      force: true,
      maxRetries: 5,
      retryDelay: 250,
    });
  }
}
