import path from "node:path";
import {
  copyFile,
  readFile,
  writeFile,
} from "node:fs/promises";
import type {
  ModulePackage,
  ModulePackageProject,
} from "./module-manifest.js";
import type { ProjectManifest } from "../project/project-manifest.js";

export interface ProjectFileBackup {
  projectFile: string;
  backupFile: string;
}

function escapeXmlAttribute(value: string): string {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll('"', "&quot;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;");
}

function getProjectFile(
  projectRoot: string,
  projectManifest: ProjectManifest,
  project: ModulePackageProject,
): string {
  const namespace = projectManifest.project.namespace;

  return path.join(
    projectRoot,
    "backend",
    "src",
    `${namespace}.${project}`,
    `${namespace}.${project}.csproj`,
  );
}

function createPackageReference(
  packageReference: ModulePackage,
  lineEnding: string,
): string {
  const packageName = escapeXmlAttribute(
    packageReference.name,
  );

  const packageVersion = escapeXmlAttribute(
    packageReference.version,
  );

  return [
    "  <ItemGroup>",
    `    <PackageReference Include="${packageName}" Version="${packageVersion}" />`,
    "  </ItemGroup>",
    "",
  ].join(lineEnding);
}

function packageAlreadyExists(
  projectFileContent: string,
  packageName: string,
): boolean {
  const escapedPackageName = packageName.replace(
    /[.*+?^${}()|[\]\\]/g,
    "\\$&",
  );

  const pattern = new RegExp(
    `<PackageReference\\s+[^>]*Include\\s*=\\s*["']${escapedPackageName}["']`,
    "i",
  );

  return pattern.test(projectFileContent);
}

function addPackageReference(
  projectFileContent: string,
  packageReference: ModulePackage,
): string {
  if (
    packageAlreadyExists(
      projectFileContent,
      packageReference.name,
    )
  ) {
    throw new Error(
      `Package '${packageReference.name}' is already referenced by the ${packageReference.project} project.`,
    );
  }

  const closingProjectTag = "</Project>";
  const closingTagIndex =
    projectFileContent.lastIndexOf(closingProjectTag);

  if (closingTagIndex < 0) {
    throw new Error(
      "Project file does not contain a closing </Project> tag.",
    );
  }

  const lineEnding = projectFileContent.includes("\r\n")
    ? "\r\n"
    : "\n";

  const contentBeforeClosingTag =
    projectFileContent
      .slice(0, closingTagIndex)
      .trimEnd();

  const contentAfterClosingTag =
    projectFileContent.slice(closingTagIndex);

  return [
    contentBeforeClosingTag,
    "",
    createPackageReference(
      packageReference,
      lineEnding,
    ).trimEnd(),
    "",
    contentAfterClosingTag,
  ].join(lineEnding);
}

export async function installModulePackages(
  projectRoot: string,
  projectManifest: ProjectManifest,
  packages: readonly ModulePackage[],
  backupDirectory: string,
): Promise<ProjectFileBackup[]> {
  const backups = new Map<string, ProjectFileBackup>();

  for (const packageReference of packages) {
    const projectFile = getProjectFile(
      projectRoot,
      projectManifest,
      packageReference.project,
    );

    let projectFileContent: string;

    try {
      projectFileContent = await readFile(
        projectFile,
        "utf8",
      );
    } catch {
      throw new Error(
        `Unable to read project file for package installation: ${projectFile}`,
      );
    }

    if (!backups.has(projectFile)) {
      const backupFile = path.join(
        backupDirectory,
        `${packageReference.project}.csproj`,
      );

      await copyFile(projectFile, backupFile);

      backups.set(projectFile, {
        projectFile,
        backupFile,
      });
    }

    const updatedContent = addPackageReference(
      projectFileContent,
      packageReference,
    );

    await writeFile(
      projectFile,
      updatedContent,
      "utf8",
    );
  }

  return [...backups.values()];
}

export async function restoreProjectFileBackups(
  backups: readonly ProjectFileBackup[],
): Promise<void> {
  for (const backup of backups) {
    await copyFile(
      backup.backupFile,
      backup.projectFile,
    );
  }
}