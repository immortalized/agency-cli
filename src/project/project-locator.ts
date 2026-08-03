import path from "node:path";
import { access } from "node:fs/promises";

const manifestFileName = ".agency.json";

async function fileExists(filePath: string): Promise<boolean> {
  try {
    await access(filePath);
    return true;
  } catch {
    return false;
  }
}

export async function findProjectRoot(startDirectory: string): Promise<string> {
  let currentDirectory = path.resolve(startDirectory);

  while (true) {
    const manifestPath = path.join(currentDirectory, manifestFileName);

    if (await fileExists(manifestPath)) {
      return currentDirectory;
    }

    const parentDirectory = path.dirname(currentDirectory);

    if (parentDirectory === currentDirectory) {
      throw new Error(
        "No Agency CLI project found in the current directory or its parent directories.",
      );
    }

    currentDirectory = parentDirectory;
  }
}