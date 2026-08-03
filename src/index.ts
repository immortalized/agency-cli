import { addCommand } from './commands/add.js';
import { runCreateCommand } from './commands/create.js';
import { databaseCommand } from './commands/database.js';

async function main(){
    const [, , command, ...args] = process.argv;

    switch (command) {
        case "create":
            const projectName = args.join(" ").trim();

            if(!projectName) {
                throw new Error("Missing project name. Usage: create <project-name>");
            }

            await runCreateCommand(projectName);
            break;
        case "add":
            await addCommand(args);
            break;
        case "database":
            await databaseCommand(args);
            break;
        case "help":
        case undefined:
            printHelp();
            break;
        default:
            throw new Error(`Unknown command '${command}'. Run 'agency help' for usage.`);
    }
}

function printHelp(): void{
    console.log(`
        Agency CLI
        Usage:
            agency create <project-name>  Create a new project with the specified name.
            agency add <module-name>      Add a module to the current project.
            agency help                   Show this help message.
        Example:
            agency create "Example Project"`);
}

main().catch((error: unknown) => {
    const message = error instanceof Error ? error.message : "Unknown error";

    console.error(`Error: ${message}`);
    process.exitCode = 1;
});