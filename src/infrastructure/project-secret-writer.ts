import path from "node:path";
import { randomBytes } from "node:crypto";
import {
  chmod,
  mkdir,
  writeFile,
} from "node:fs/promises";

const secretsDirectoryName = ".secrets";
const databasePasswordFileName =
  "database-password";

export interface ProjectSecretWriteResult {
  databasePasswordFile: string;
}

export async function writeProjectSecrets(
  projectRoot: string,
): Promise<ProjectSecretWriteResult> {
  const secretsDirectory = path.join(
    projectRoot,
    secretsDirectoryName,
  );

  const databasePasswordFile = path.join(
    secretsDirectory,
    databasePasswordFileName,
  );

  await mkdir(secretsDirectory, {
    recursive: true,
  });

  await restrictDirectoryPermissions(
    secretsDirectory,
  );

  const password = randomBytes(48)
    .toString("base64url");

  await writeFile(
    databasePasswordFile,
    `${password}\n`,
    {
      encoding: "utf8",
      flag: "wx",
    },
  );

  await restrictSecretPermissions(
    databasePasswordFile,
  );

  return {
    databasePasswordFile,
  };
}

async function restrictDirectoryPermissions(
  directoryPath: string,
): Promise<void> {
  if (process.platform === "win32") {
    return;
  }

  await chmod(directoryPath, 0o700);
}

async function restrictSecretPermissions(
  filePath: string,
): Promise<void> {
  if (process.platform === "win32") {
    return;
  }

  await chmod(filePath, 0o600);
}