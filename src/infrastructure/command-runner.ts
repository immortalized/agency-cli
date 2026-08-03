import { spawn } from "node:child_process";

export interface RunCommandOptions {
  cwd: string;
}

export async function runCommand(
  command: string,
  args: string[],
  options: RunCommandOptions,
): Promise<void> {
  await new Promise<void>((resolve, reject) => {
    const childProcess = spawn(command, args, {
      cwd: options.cwd,
      stdio: "inherit",
      shell: false,
    });

    childProcess.on("error", (error) => {
      reject(
        new Error(
          `Unable to start command '${command}': ${error.message}`,
        ),
      );
    });

    childProcess.on("close", (exitCode, signal) => {
      if (exitCode === 0) {
        resolve();
        return;
      }

      if (signal) {
        reject(
          new Error(
            `Command '${command}' was terminated by signal ${signal}.`,
          ),
        );
        return;
      }

      reject(
        new Error(
          `Command '${command}' failed with exit code ${exitCode ?? "unknown"}.`,
        ),
      );
    });
  });
}