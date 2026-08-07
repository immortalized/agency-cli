import { spawnSync } from "node:child_process";

const apiAddress = process.env.AUTH_TEST_API_ADDRESS ??
  "http://localhost:8080";

const identifier = process.env.AUTH_TEST_IDENTIFIER;
const password = process.env.AUTH_TEST_PASSWORD;

if (!identifier || !password) {
  throw new Error(
    "AUTH_TEST_IDENTIFIER and AUTH_TEST_PASSWORD must be configured.",
  );
}

async function login() {
  const response = await fetch(
    `${apiAddress}/api/auth/login`,
    {
      method: "POST",
      headers: {
        "content-type": "application/json",
      },
      body: JSON.stringify({ identifier, password }),
    },
  );

  if (!response.ok) {
    throw new Error(
      `Login failed with HTTP status ${response.status}.`,
    );
  }

  return (await response.json()).accessToken;
}

function header(token) {
  return JSON.parse(
    Buffer.from(token.split(".")[0], "base64url")
      .toString("utf8"),
  );
}

async function validate(token) {
  return fetch(
    `${apiAddress}/api/auth/dev/validate-token`,
    {
      headers: {
        authorization: `Bearer ${token}`,
      },
    },
  );
}

function run(command, args) {
  const result = spawnSync(command, args, {
    stdio: "inherit",
  });

  if (result.status !== 0) {
    throw new Error(
      `'${command} ${args.join(" ")}' failed with exit code ${result.status}.`,
    );
  }
}

const oldToken = await login();
const oldKeyId = header(oldToken).kid;

run("npm", ["run", "auth:rotate"]);
run("docker", [
  "compose",
  "up",
  "-d",
  "--force-recreate",
  "api",
]);

let oldTokenResponse;

for (let attempt = 0; attempt < 30; attempt += 1) {
  try {
    oldTokenResponse = await validate(oldToken);
    break;
  } catch {
    await new Promise((resolve) =>
      setTimeout(resolve, 500));
  }
}

if (oldTokenResponse?.status !== 204) {
  throw new Error(
    "A token issued before rotation no longer validates.",
  );
}

const newToken = await login();
const newKeyId = header(newToken).kid;

if (newKeyId === oldKeyId) {
  throw new Error(
    "Rotation did not activate a new JWT kid.",
  );
}

if ((await validate(newToken)).status !== 204) {
  throw new Error(
    "A token issued after rotation does not validate.",
  );
}

console.log(
  `JWT rotation verified (${oldKeyId} retained, ${newKeyId} active).`,
);
