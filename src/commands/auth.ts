import path from "node:path";
import process from "node:process";
import {
  initializeAuthSecrets,
} from "../auth/auth-secret-initializer.js";
import {
  findProjectRoot,
} from "../project/project-locator.js";
import {
  readProjectManifest,
} from "../project/project-manifest-reader.js";

export async function authCommand(
  args: string[],
): Promise<void> {
  if (args.length > 0) {
    throw new Error(
      "The auth:init command does not accept arguments.",
    );
  }

  const startDirectory =
    process.env.INIT_CWD ??
    process.cwd();

  const projectRoot =
    await findProjectRoot(startDirectory);

  const projectManifest =
    await readProjectManifest(projectRoot);

  if (
    !projectManifest.modules.includes("auth")
  ) {
    throw new Error(
      "The auth module is not installed. Run 'agency add auth' first.",
    );
  }

  console.log(
    `Project: ${projectManifest.project.displayName}`,
  );

  console.log(
    "Generating project-specific RSA authentication keys...",
  );

  const result =
    await initializeAuthSecrets({
      projectRoot,
      projectSlug:
        projectManifest.project.slug,
    });

  console.log(
    "Authentication secrets initialized successfully.",
  );

  console.log(
    `Private key: ${path.relative(
      projectRoot,
      result.privateKeyFile,
    )}`,
  );

  console.log(
    `Public key: ${path.relative(
      projectRoot,
      result.publicKeyFile,
    )}`,
  );

  console.log(
    `Docker Compose override: ${path.relative(
      projectRoot,
      result.composeOverrideFile,
    )}`,
  );

  console.log(
    `JWT key id: ${result.keyId}`,
  );

  console.log(
    "The private key is mounted only into the API container through Docker Compose secrets.",
  );

  console.log(
    "Existing authentication files are never overwritten by auth:init.",
  );
}