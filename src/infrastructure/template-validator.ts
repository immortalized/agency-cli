import { readdir, readFile } from "node:fs/promises";
import path from "node:path";

const unresolvedTokenPattern = /__[A-Z0-9_]+__/g;

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

export async function validateGeneratedTemplate(directory: string): Promise<void> {
  const unresolvedTokens = new Set<string>();

  await findUnresolvedTokens(directory, unresolvedTokens);

  if (unresolvedTokens.size === 0) {
    return;
  }

  const tokens = [...unresolvedTokens].join(", ");

  throw new Error(`Unresolved template tokens found: ${tokens}`);
}

async function findUnresolvedTokens(directory: string, unresolvedTokens: Set<string>): Promise<void> {
  const entries = await readdir(directory, {
    withFileTypes: true,
  });

  for (const entry of entries) {
    const entryPath = path.join(directory, entry.name);

    collectTokens(entry.name, unresolvedTokens);

    if (entry.isDirectory()) {
      await findUnresolvedTokens(entryPath, unresolvedTokens);
      continue;
    }

    const extension = path.extname(entry.name).toLowerCase();

    if (binaryFileExtensions.has(extension)) {
      continue;
    }

    const content = await readFile(entryPath, "utf8");

    collectTokens(content, unresolvedTokens);
  }
}

function collectTokens(content: string, target: Set<string>): void {
  const matches = content.match(unresolvedTokenPattern);

  if (!matches) {
    return;
  }

  for (const match of matches) {
    target.add(match);
  }
}