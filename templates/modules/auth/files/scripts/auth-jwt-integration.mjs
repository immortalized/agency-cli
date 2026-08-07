const apiAddress = process.env.AUTH_TEST_API_ADDRESS ??
  "http://localhost:8080";

const identifier = process.env.AUTH_TEST_IDENTIFIER;
const password = process.env.AUTH_TEST_PASSWORD;

if (!identifier || !password) {
  throw new Error(
    "AUTH_TEST_IDENTIFIER and AUTH_TEST_PASSWORD must be configured.",
  );
}

const loginResponse = await fetch(
  `${apiAddress}/api/auth/login`,
  {
    method: "POST",
    headers: {
      "content-type": "application/json",
    },
    body: JSON.stringify({ identifier, password }),
  },
);

if (!loginResponse.ok) {
  throw new Error(
    `Login failed with HTTP status ${loginResponse.status}.`,
  );
}

const auth = await loginResponse.json();
const token = auth.accessToken;

if (typeof token !== "string") {
  throw new Error("Login response contains no access token.");
}

const parts = token.split(".");

if (parts.length !== 3) {
  throw new Error("Access token is not a compact JWT.");
}

function decodeJson(value) {
  return JSON.parse(
    Buffer.from(value, "base64url").toString("utf8"),
  );
}

const header = decodeJson(parts[0]);
const payload = decodeJson(parts[1]);

if (header.alg !== "RS256" || header.typ !== "JWT") {
  throw new Error("JWT JOSE header does not declare RS256/JWT.");
}

if (!/^__PROJECT_SLUG__-jwt-v[1-9][0-9]*$/.test(header.kid)) {
  throw new Error(`JWT contains an unexpected kid '${header.kid}'.`);
}

if (payload.iss !== "__PROJECT_SLUG__"
  || payload.aud !== "__PROJECT_SLUG__-api") {
  throw new Error("JWT issuer or audience changed unexpectedly.");
}

for (const claim of ["sub", "unique_name", "jti", "iat", "nbf", "exp"]) {
  if (!(claim in payload)) {
    throw new Error(`JWT is missing the '${claim}' claim.`);
  }
}

if (payload.exp - payload.iat !== 600
  || Math.abs(payload.nbf - payload.iat) > 1) {
  throw new Error("JWT lifetime or not-before semantics changed unexpectedly.");
}

const validationResponse = await fetch(
  `${apiAddress}/api/auth/dev/validate-token`,
  {
    headers: {
      authorization: `Bearer ${token}`,
    },
  },
);

if (validationResponse.status !== 204) {
  throw new Error(
    `ASP.NET JWT Bearer validation failed with HTTP status ${validationResponse.status}.`,
  );
}

console.log(
  `JWT integration verified (alg=${header.alg}, kid=${header.kid}, issuer=${payload.iss}, audience=${payload.aud}).`,
);
