import path from "node:path";
import { runCommand } from "../infrastructure/command-runner.js";
import type { ProjectManifest } from "../project/project-manifest.js";

export function getMigrationsDirectory(
  projectRoot: string,
  projectManifest: ProjectManifest,
): string {
  const namespace = projectManifest.project.namespace;

  return path.join(
    projectRoot,
    "backend",
    "src",
    `${namespace}.Infrastructure`,
    "Persistence",
    "Migrations",
  );
}

export async function generateModuleMigration(
  projectRoot: string,
  projectManifest: ProjectManifest,
  migrationName: string,
): Promise<void> {
  const namespace = projectManifest.project.namespace;

  const infrastructureProject = path.join(
    projectRoot,
    "backend",
    "src",
    `${namespace}.Infrastructure`,
    `${namespace}.Infrastructure.csproj`,
  );

  const startupProject = path.join(
    projectRoot,
    "backend",
    "src",
    `${namespace}.Api`,
    `${namespace}.Api.csproj`,
  );

  console.log("Restoring local .NET tools...");

  await runCommand(
    "dotnet",
    ["tool", "restore"],
    { cwd: projectRoot },
  );

  console.log(`Generating EF Core migration '${migrationName}'...`);

  await runCommand(
    "dotnet",
    [
      "ef",
      "migrations",
      "add",
      migrationName,
      "--project",
      infrastructureProject,
      "--startup-project",
      startupProject,
      "--output-dir",
      "Persistence/Migrations",
    ],
    { cwd: projectRoot },
  );
}