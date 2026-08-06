import path from "node:path";
import { generateKeyPair, randomBytes } from "node:crypto";
import {
  chmod,
  mkdir,
  rm,
  writeFile,
} from "node:fs/promises";

const secretsDirectoryName = ".secrets";

const privateKeyFileName =
  "auth-jwt-private-key.pem";

const publicKeyFileName =
  "auth-jwt-public-key.pem";

const composeOverrideFileName =
  "compose.override.yaml";

export interface AuthSecretInitializationOptions {
  projectRoot: string;
  projectSlug: string;
}

export interface AuthSecretInitializationResult {
  privateKeyFile: string;
  publicKeyFile: string;
  composeOverrideFile: string;
  keyId: string;
}

interface GeneratedRsaKeyPair {
  privateKey: string;
  publicKey: string;
}

function generateRsaKeyPair():
  Promise<GeneratedRsaKeyPair> {
  return new Promise((resolve, reject) => {
    generateKeyPair(
      "rsa",
      {
        modulusLength: 3072,

        publicKeyEncoding: {
          type: "spki",
          format: "pem",
        },

        privateKeyEncoding: {
          type: "pkcs8",
          format: "pem",
        },
      },
      (
        error,
        publicKey,
        privateKey,
      ) => {
        if (error) {
          reject(error);
          return;
        }

        resolve({
          privateKey,
          publicKey,
        });
      },
    );
  });
}

function generateKeyId(): string {
  const datePart = new Date()
    .toISOString()
    .slice(0, 10);

  const randomPart = randomBytes(6)
    .toString("hex");

  return `primary-${datePart}-${randomPart}`;
}

function createComposeOverride(
  projectSlug: string,
  keyId: string,
): string {
  return `services:
  api:
    environment:
      Auth__Jwt__Issuer: "${projectSlug}"
      Auth__Jwt__Audience: "${projectSlug}-api"
      Auth__Jwt__AccessTokenLifetimeMinutes: "10"
      Auth__Jwt__PrivateKeyFile: "/run/secrets/auth_jwt_private_key"
      Auth__Jwt__PublicKeyFile: "/run/secrets/auth_jwt_public_key"
      Auth__Jwt__KeyId: "${keyId}"

    secrets:
      - auth_jwt_private_key
      - auth_jwt_public_key

secrets:
  auth_jwt_private_key:
    file: ./.secrets/auth-jwt-private-key.pem

  auth_jwt_public_key:
    file: ./.secrets/auth-jwt-public-key.pem
`;
}

async function restrictDirectoryPermissions(
  directoryPath: string,
): Promise<void> {
  if (process.platform === "win32") {
    return;
  }

  await chmod(directoryPath, 0o700);
}

async function restrictPrivateKeyPermissions(
  filePath: string,
): Promise<void> {
  if (process.platform === "win32") {
    return;
  }

  await chmod(filePath, 0o600);
}

async function setPublicKeyPermissions(
  filePath: string,
): Promise<void> {
  if (process.platform === "win32") {
    return;
  }

  await chmod(filePath, 0o644);
}

function isFileAlreadyExistsError(
  error: unknown,
): boolean {
  if (
    typeof error !== "object" ||
    error === null ||
    !("code" in error)
  ) {
    return false;
  }

  return error.code === "EEXIST";
}

export async function initializeAuthSecrets(
  options: AuthSecretInitializationOptions,
): Promise<AuthSecretInitializationResult> {
  const projectRoot =
    path.resolve(options.projectRoot);

  const projectSlug =
    options.projectSlug.trim();

  if (!projectSlug) {
    throw new Error(
      "Project slug cannot be empty.",
    );
  }

  const secretsDirectory = path.join(
    projectRoot,
    secretsDirectoryName,
  );

  const privateKeyFile = path.join(
    secretsDirectory,
    privateKeyFileName,
  );

  const publicKeyFile = path.join(
    secretsDirectory,
    publicKeyFileName,
  );

  const composeOverrideFile = path.join(
    projectRoot,
    composeOverrideFileName,
  );

  const createdFiles: string[] = [];

  await mkdir(secretsDirectory, {
    recursive: true,
  });

  await restrictDirectoryPermissions(
    secretsDirectory,
  );

  try {
    const keyPair =
      await generateRsaKeyPair();

    const keyId = generateKeyId();

    const composeOverride =
      createComposeOverride(
        projectSlug,
        keyId,
      );

    /*
     * "wx" means:
     *
     * - create a new file;
     * - fail if it already exists;
     * - never overwrite existing secrets.
     */
    await writeFile(
      privateKeyFile,
      keyPair.privateKey,
      {
        encoding: "utf8",
        flag: "wx",
      },
    );

    createdFiles.push(privateKeyFile);

    await restrictPrivateKeyPermissions(
      privateKeyFile,
    );

    await writeFile(
      publicKeyFile,
      keyPair.publicKey,
      {
        encoding: "utf8",
        flag: "wx",
      },
    );

    createdFiles.push(publicKeyFile);

    await setPublicKeyPermissions(
      publicKeyFile,
    );

    /*
     * The compose override also uses "wx".
     *
     * We do not overwrite an existing override because
     * it could already contain manually configured
     * services or another module's configuration.
     */
    await writeFile(
      composeOverrideFile,
      composeOverride,
      {
        encoding: "utf8",
        flag: "wx",
      },
    );

    createdFiles.push(composeOverrideFile);

    return {
      privateKeyFile,
      publicKeyFile,
      composeOverrideFile,
      keyId,
    };
  } catch (error) {
    for (
      const createdFile of
        [...createdFiles].reverse()
    ) {
      try {
        await rm(createdFile, {
          force: true,
        });
      } catch {
        // Preserve the original error.
      }
    }

    if (isFileAlreadyExistsError(error)) {
      throw new Error(
        "Authentication initialization cannot continue because an authentication key or compose.override.yaml already exists. No existing file was overwritten.",
        {
          cause: error,
        },
      );
    }

    throw error;
  }
}