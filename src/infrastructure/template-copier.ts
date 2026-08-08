import { cp } from "node:fs/promises";

export async function copyTemplate(
  sourceDirectory: string,
  targetDirectory: string,
  overwrite = false,
): Promise<void> {
  await cp(sourceDirectory, targetDirectory, {
    recursive: true,
    errorOnExist: !overwrite,
    force: overwrite,
  });
}
