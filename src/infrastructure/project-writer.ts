import {
  access,
  rm,
} from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

import type { ProjectNames } from "../project/project-names.js";
import type { ProjectCapability } from "../project/project-capability.js";
import type { ProjectManifest } from "../project/project-manifest.js";
import { writeProjectManifest } from "../project/project-manifest-writer.js";
import { copyTemplate } from "./template-copier.js";
import {
  replaceTokensInDirectory,
  type TemplateTokens,
} from "./token-replacer.js";
import { validateGeneratedTemplate } from "./template-validator.js";
import { writeProjectSecrets } from "./project-secret-writer.js";

const currentDirectory = path.dirname(
  fileURLToPath(import.meta.url),
);

async function pathExists(
  targetPath: string,
): Promise<boolean> {
  try {
    await access(targetPath);
    return true;
  } catch {
    return false;
  }
}

export async function writeProject(
  outputDirectory: string,
  projectNames: ProjectNames,
  capabilities: readonly ProjectCapability[],
): Promise<void> {
  const absoluteDirectory = path.resolve(
    outputDirectory,
  );

  if (await pathExists(absoluteDirectory)) {
    throw new Error(
      `Target directory already exists: ${absoluteDirectory}`,
    );
  }

  const templatesDirectory = path.resolve(
    currentDirectory,
    "../../templates",
  );

  const tokens: TemplateTokens = {
    __PROJECT_DISPLAY_NAME__: projectNames.displayName,
    __PROJECT_SLUG__: projectNames.slug,
    __PROJECT_NAMESPACE__: projectNames.namespace,
    __DATABASE_NAME__: projectNames.databaseName,
    __PROJECT_CAPABILITIES__: capabilities.join(", "),
  };

  try {
    await copyTemplate(
      path.join(templatesDirectory, "base"),
      absoluteDirectory,
    );

    if (capabilities.includes("api")) {
      await copyTemplate(
        path.join(
          templatesDirectory,
          "capabilities",
          "api",
        ),
        absoluteDirectory,
        true,
      );
    }

    if (capabilities.includes("database")) {
      await copyTemplate(
        path.join(
          templatesDirectory,
          "capabilities",
          "database",
        ),
        absoluteDirectory,
        true,
      );
    }

    await replaceTokensInDirectory(
      absoluteDirectory,
      tokens,
    );

    await validateGeneratedTemplate(
      absoluteDirectory,
    );

    await writeProjectSecrets(
      absoluteDirectory,
      capabilities,
    );

    const manifest: ProjectManifest = {
      generatorVersion: "0.2.0",
      project: projectNames,
      capabilities: [...capabilities],
      modules: [],
    };

    await writeProjectManifest(
      absoluteDirectory,
      manifest,
    );
  } catch (error) {
    await rm(absoluteDirectory, {
      recursive: true,
      force: true,
    });

    throw error;
  }
}
