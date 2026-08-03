import { readdir, rename, readFile, writeFile } from "node:fs/promises";
import path from "node:path";

export type TemplateTokens = Readonly<Record<string, string>>;

const binaryFileExtensions = new Set([
  ".png",
  ".jpg",
  ".jpeg",
  ".gif",
  ".webp",
  ".ico",
  ".pdf",
  ".zip",
  ".woff",
  ".woff2",
]);

export async function replaceTokensInDirectory(directory: string, tokens: TemplateTokens): Promise<void> {
  const entries = await readdir(directory, {
    withFileTypes: true,
});

  for (const entry of entries) {
    const originalPath  = path.join(directory, entry.name);

    if (entry.isDirectory()) {
        await replaceTokensInDirectory(originalPath, tokens);
    } else {
      await replaceTokensInFile(originalPath, tokens);
    }

    const updatedName = replaceTokens(entry.name, tokens);

    if (updatedName !== entry.name) {
      const updatedPath = path.join(directory, updatedName);

      await rename(originalPath, updatedPath);
    }
  }
}

async function replaceTokensInFile(filePath: string, tokens: TemplateTokens): Promise<void> {
  const extension = path.extname(filePath).toLowerCase();

  if (binaryFileExtensions.has(extension)) {
    return;
  }

  const originalContent = await readFile(filePath, "utf8");
  const updatedContent = replaceTokens(originalContent, tokens);

  if (updatedContent !== originalContent) {
    await writeFile(filePath, updatedContent, "utf8");
  }
}

function replaceTokens(content: string, tokens: TemplateTokens): string {
    let result = content;

    for (const [token, value] of Object.entries(tokens)) {
        result = result.replaceAll(token, value);
    }

    return result;
}