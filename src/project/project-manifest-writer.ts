import path from "node:path";
import { rename, rm, writeFile } from "node:fs/promises";
import type { ProjectManifest } from "./project-manifest.js";

export async function writeProjectManifest(
  projectRoot: string,
  manifest: ProjectManifest,
): Promise<void> {
  const manifestPath = path.join(projectRoot, ".agency.json");
  const temporaryPath = path.join(projectRoot, ".agency.json.tmp");
  const content = `${JSON.stringify(manifest, null, 2)}\n`;

  try {
    await writeFile(temporaryPath, content, "utf8");
    await rename(temporaryPath, manifestPath);
  } catch (error) {
    await rm(temporaryPath, { force: true });
    throw error;
  }
}