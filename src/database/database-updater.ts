import path from "node:path";
import { runCommand } from "../infrastructure/command-runner.js";
import type { ProjectManifest } from "../project/project-manifest.js";

export async function updateDatabase(
  projectRoot: string,
  projectManifest: ProjectManifest,
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

  await runCommand("dotnet", ["tool", "restore"], {
    cwd: projectRoot,
  });

  console.log("Applying EF Core migrations...");

  await runCommand(
    "dotnet",
    [
      "ef",
      "database",
      "update",
      "--project",
      infrastructureProject,
      "--startup-project",
      startupProject,
    ],
    {
      cwd: projectRoot,
    },
  );
}