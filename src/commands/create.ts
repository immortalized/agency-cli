import { createProjectNames } from "../project/project-names.js";
import {
  normalizeProjectCapabilities,
  type ProjectCapability,
} from "../project/project-capability.js";
import { writeProject } from "../infrastructure/project-writer.js";

export async function runCreateCommand(
  args: readonly string[],
): Promise<void> {
  const requested = new Set<ProjectCapability>();
  const projectNameParts: string[] = [];

  for (const argument of args) {
    if (argument === "--api") {
      requested.add("api");
      continue;
    }

    if (argument === "--db") {
      requested.add("database");
      continue;
    }

    if (argument.startsWith("--")) {
      throw new Error(
        `Unknown create option '${argument}'. Supported options: --api, --db`,
      );
    }

    projectNameParts.push(argument);
  }

  const rawProjectName = projectNameParts
    .join(" ")
    .trim();

  if (!rawProjectName) {
    throw new Error(
      "Missing project name. Usage: agency create <project-name> [--api|--db]",
    );
  }

  const projectNames = createProjectNames(
    rawProjectName,
  );

  const capabilities = normalizeProjectCapabilities(
    requested,
  );

  await writeProject(
    projectNames.slug,
    projectNames,
    capabilities,
  );

  console.log(
    `Project "${projectNames.displayName}" created successfully in directory: ${projectNames.slug}`,
  );

  console.log(
    `Capabilities: ${capabilities.join(", ")}`,
  );
}
