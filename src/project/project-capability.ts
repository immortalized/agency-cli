export const projectCapabilities = [
  "frontend",
  "api",
  "database",
] as const;

export type ProjectCapability =
  (typeof projectCapabilities)[number];

export function normalizeProjectCapabilities(
  requested: ReadonlySet<ProjectCapability>,
): ProjectCapability[] {
  const normalized = new Set<ProjectCapability>([
    "frontend",
  ]);

  if (requested.has("api") || requested.has("database")) {
    normalized.add("api");
  }

  if (requested.has("database")) {
    normalized.add("database");
  }

  return projectCapabilities.filter((capability) =>
    normalized.has(capability),
  );
}

export function isProjectCapability(
  value: unknown,
): value is ProjectCapability {
  return projectCapabilities.some(
    (capability) => capability === value,
  );
}
