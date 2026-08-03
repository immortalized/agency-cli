import { cp } from "node:fs/promises";

export async function copyTemplate(
  sourceDirectory: string,
  targetDirectory: string,
): Promise<void> {
  await cp(sourceDirectory, targetDirectory, {
    recursive: true,
    errorOnExist: true,
    force: false,
  });
}