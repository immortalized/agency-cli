#!/usr/bin/env node

import { addCommand } from "./commands/add.js";
import { runCreateCommand } from "./commands/create.js";
import { databaseCommand } from "./commands/database.js";

async function main(): Promise<void> {
  const [, , command, ...args] = process.argv;

  switch (command) {
    case "create": {
      await runCreateCommand(args);
      break;
    }

    case "add":
      await addCommand(args);
      break;

    case "database":
      await databaseCommand(args);
      break;

    case "help":
    case undefined:
      printHelp();
      break;

    default:
      throw new Error(
        `Unknown command '${command}'. Run 'agency help' for usage.`,
      );
  }
}

function printHelp(): void {
  console.log(`
Agency CLI

Usage:
  agency create <project-name>        Create a frontend-only project.
  agency create <project-name> --api  Add an ASP.NET Core API.
  agency create <project-name> --db   Add an API and PostgreSQL.
  agency add <module-name>      Add a module to the current project.
  agency database update       Apply pending database migrations.
  agency help                  Show this help message.

Examples:
  agency create "Example Project"
  agency create "Example API" --api
  agency create "Example App" --db
  agency add auth
  agency database update
`);
}

main().catch((error: unknown) => {
  const message =
    error instanceof Error
      ? error.message
      : "Unknown error";

  console.error(`Error: ${message}`);
  process.exitCode = 1;
});
