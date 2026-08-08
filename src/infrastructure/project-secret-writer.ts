import path from "node:path";
import { randomBytes } from "node:crypto";
import {
  chmod,
  mkdir,
  writeFile,
} from "node:fs/promises";
import type { ProjectCapability } from "../project/project-capability.js";

const databasePasswordFileName =
  "database-password";

export async function writeProjectSecrets(
  projectRoot: string,
  capabilities: readonly ProjectCapability[],
): Promise<void> {
  if (!capabilities.includes("database")) {
    return;
  }

  const secretsDirectory = path.join(
    projectRoot,
    ".secrets",
  );

  await mkdir(secretsDirectory, {
    recursive: true,
  });

  await restrictPermissions(
    secretsDirectory,
    0o700,
  );

  const passwordFile = path.join(
    secretsDirectory,
    databasePasswordFileName,
  );

  await writeFile(
    passwordFile,
    `${randomBytes(48).toString("base64url")}\n`,
    {
      encoding: "utf8",
      flag: "wx",
    },
  );

  await restrictPermissions(passwordFile, 0o600);
}

async function restrictPermissions(
  targetPath: string,
  mode: number,
): Promise<void> {
  if (process.platform !== "win32") {
    await chmod(targetPath, mode);
  }
}
