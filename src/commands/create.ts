import { createProjectNames } from '../project/project-names.js';
import { writeProject } from '../infrastructure/project-writer.js';

export async function runCreateCommand(rawProjectName: string): Promise<void> {
    const projectNames = createProjectNames(rawProjectName);

    await writeProject(projectNames.slug, projectNames);

    console.log(`Project "${projectNames.displayName}" created successfully in directory: ${projectNames.slug}`);
}