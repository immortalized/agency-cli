import process from "node:process";
import { updateDatabase } from "../database/database-updater.js";
import { findProjectRoot } from "../project/project-locator.js";
import { readProjectManifest } from "../project/project-manifest-reader.js";

export async function databaseCommand(args: string[]): Promise<void> {
  const action = args[0]?.trim().toLowerCase();

  if (!action) {
    throw new Error(
      "Database action is required. Example: agency database update",
    );
  }

  if (args.length > 1) {
    throw new Error("The database command accepts exactly one action.");
  }

  if (action !== "update") {
    throw new Error(
      `Unknown database action '${action}'. Supported actions: update`,
    );
  }

  const startDirectory = process.env.INIT_CWD ?? process.cwd();
  const projectRoot = await findProjectRoot(startDirectory);
  const projectManifest = await readProjectManifest(projectRoot);

  if (!projectManifest.capabilities.includes("database")) {
    throw new Error(
      "This project does not have the database capability.",
    );
  }

  console.log(`Project: ${projectManifest.project.displayName}`);

  await updateDatabase(projectRoot, projectManifest);

  console.log("Database updated successfully.");
}
