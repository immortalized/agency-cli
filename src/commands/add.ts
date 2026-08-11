import process from "node:process";
import { findModuleTemplate } from "../modules/module-locator.js";
import { readModuleManifest } from "../modules/module-manifest-reader.js";
import { installModule } from "../modules/module-installer.js";
import { validateModuleRequirements } from "../modules/module-requirements.js";
import { findProjectRoot } from "../project/project-locator.js";
import { readProjectManifest } from "../project/project-manifest-reader.js";

interface ParsedAddRequest {
  moduleName: string;
  options: Record<string, string | number | boolean>;
}

const unsealStrategies = [
  "passphrase",
  "multi-passphrase",
  "kms",
] as const;

function parsePositiveInteger(
  option: string,
  value: string | undefined,
): number {
  if (!value || !/^\d+$/.test(value)) {
    throw new Error(`${option} requires a positive integer.`);
  }

  const parsed = Number.parseInt(value, 10);

  if (!Number.isSafeInteger(parsed) || parsed < 1 || parsed > 10) {
    throw new Error(`${option} must be between 1 and 10.`);
  }

  return parsed;
}

function readOptionValue(
  args: readonly string[],
  index: number,
  option: string,
): { value: string; consumed: number } {
  const argument = args[index]!;
  const prefix = `${option}=`;

  if (argument.startsWith(prefix)) {
    const value = argument.slice(prefix.length).trim();
    if (!value) {
      throw new Error(`${option} requires a value.`);
    }
    return { value, consumed: 1 };
  }

  const value = args[index + 1]?.trim();
  if (!value || value.startsWith("--")) {
    throw new Error(`${option} requires a value.`);
  }

  return { value, consumed: 2 };
}

function parseAddRequest(args: readonly string[]): ParsedAddRequest {
  const moduleName = args[0]?.trim().toLowerCase();

  if (!moduleName) {
    throw new Error("Module name is required. Example: agency add menu");
  }

  const options: Record<string, string | number | boolean> = {};

  for (let index = 1; index < args.length;) {
    const argument = args[index]!;

    if (moduleName !== "auth") {
      throw new Error(`Module '${moduleName}' does not accept options.`);
    }

    if (argument === "--unseal-strategy" || argument.startsWith("--unseal-strategy=")) {
      const parsed = readOptionValue(args, index, "--unseal-strategy");
      if (!unsealStrategies.includes(parsed.value as (typeof unsealStrategies)[number])) {
        throw new Error(
          `Unsupported unseal strategy '${parsed.value}'. Expected passphrase, multi-passphrase, or kms.`,
        );
      }
      options.unsealStrategy = parsed.value;
      index += parsed.consumed;
      continue;
    }

    if (argument === "--unseal-shares" || argument.startsWith("--unseal-shares=")) {
      const parsed = readOptionValue(args, index, "--unseal-shares");
      options.unsealShares = parsePositiveInteger("--unseal-shares", parsed.value);
      index += parsed.consumed;
      continue;
    }

    if (argument === "--unseal-threshold" || argument.startsWith("--unseal-threshold=")) {
      const parsed = readOptionValue(args, index, "--unseal-threshold");
      options.unsealThreshold = parsePositiveInteger("--unseal-threshold", parsed.value);
      index += parsed.consumed;
      continue;
    }

    throw new Error(`Unknown auth option '${argument}'.`);
  }

  if (moduleName === "auth") {
    const strategy = (options.unsealStrategy as string | undefined) ?? "passphrase";
    const shares = (options.unsealShares as number | undefined) ??
      (strategy === "multi-passphrase" ? 5 : 1);
    const threshold = (options.unsealThreshold as number | undefined) ??
      (strategy === "multi-passphrase" ? 3 : 1);

    if (strategy === "passphrase" && (shares !== 1 || threshold !== 1)) {
      throw new Error("The passphrase strategy uses exactly one share with threshold one.");
    }

    if (strategy === "kms" && (options.unsealShares !== undefined || options.unsealThreshold !== undefined)) {
      throw new Error("The kms strategy does not accept Shamir share options.");
    }

    if (strategy === "multi-passphrase" && (shares < 2 || threshold < 2 || threshold > shares)) {
      throw new Error("The multi-passphrase strategy requires 2-10 shares and a threshold between 2 and the share count.");
    }

    options.unsealStrategy = strategy;
    if (strategy !== "kms") {
      options.unsealShares = shares;
      options.unsealThreshold = threshold;
    }
  }

  return { moduleName, options };
}

export async function addCommand(args: string[]): Promise<void> {
  const request = parseAddRequest(args);
  const requestedModuleName = request.moduleName;

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
    request.options,
  );

  console.log(`Module '${requestedModuleName}' installed successfully.`);
  console.log(`Project: ${projectManifest.project.displayName}`);
}
