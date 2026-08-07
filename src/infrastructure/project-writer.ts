import {
  access,
  rm,
  writeFile,
} from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

import type { ProjectNames } from "../project/project-names.js";
import { copyTemplate } from "./template-copier.js";
import {
  replaceTokensInDirectory,
  type TemplateTokens,
} from "./token-replacer.js";
import { validateGeneratedTemplate } from "./template-validator.js";
import { writeProjectSecrets } from "./project-secret-writer.js";

interface ProjectManifest {
  generatorVersion: string;
  project: ProjectNames;
  modules: string[];
}

const currentFilePath =
  fileURLToPath(import.meta.url);

const currentDirectory =
  path.dirname(currentFilePath);

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
): Promise<void> {
  const absoluteDirectory =
    path.resolve(outputDirectory);

  if (await pathExists(absoluteDirectory)) {
    throw new Error(
      `Target directory already exists: ${absoluteDirectory}`,
    );
  }

  const templateDirectory = path.resolve(
    currentDirectory,
    "../../templates/base",
  );

  const tokens: TemplateTokens = {
    "__PROJECT_DISPLAY_NAME__":
      projectNames.displayName,

    "__PROJECT_SLUG__":
      projectNames.slug,

    "__PROJECT_NAMESPACE__":
      projectNames.namespace,

    "__DATABASE_NAME__":
      projectNames.databaseName,
  };

  try {
    await copyTemplate(
      templateDirectory,
      absoluteDirectory,
    );

    await replaceTokensInDirectory(
      absoluteDirectory,
      tokens,
    );

    await validateGeneratedTemplate(
      absoluteDirectory,
    );

    await writeProjectSecrets(
      absoluteDirectory,
    );

    const manifest: ProjectManifest = {
      generatorVersion: "0.1.0",
      project: projectNames,
      modules: [],
    };

    const manifestPath = path.join(
      absoluteDirectory,
      ".agency.json",
    );

    await writeFile(
      manifestPath,
      `${JSON.stringify(
        manifest,
        null,
        2,
      )}\n`,
      "utf8",
    );
  } catch (error) {
    await rm(absoluteDirectory, {
      recursive: true,
      force: true,
    });

    throw error;
  }
}