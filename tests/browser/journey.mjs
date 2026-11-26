import { chromium } from "playwright";
import { checkEvaluations } from "./evaluations.mjs";
import { checkSorting } from "./sorting.mjs";
import { checkCandidates } from "./candidates.mjs";
import { checkOptionalEquipment } from "./optional-equipment.mjs";
import { checkOffers } from "./offers.mjs";
import { checkDocuments } from "./documents.mjs";
import { checkCosts } from "./costs.mjs";
import {
  checkSetup,
  checkVerify,
  checkAccess,
  checkEnrollment,
  checkRename,
  checkRateLimit,
} from "./accounts.mjs";
import { checkScenarios } from "./scenarios.mjs";
import assert from "node:assert/strict";
import { spawn, execFileSync } from "node:child_process";
import { once } from "node:events";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { request as proxyRequest } from "node:http";
import { createServer as createHttpsServer } from "node:https";
import { tmpdir } from "node:os";
import { resolve, join } from "node:path";

// ── Configuration ───────────────────────────────────────────────────────────

const APPLICATION_PORT = 5189;
const HTTPS_PROXY_PORT = 5190;
const LOOPBACK_ADDRESS = "127.0.0.1";
const HTTPS_HOST = "steervo-browser.test";
const ALTERNATE_HTTPS_HOST = `other.${HTTPS_HOST}`;

const DOCKER_NETWORK = "steervo-tests_default";
const CONTAINER_KEYS_PATH = "/var/steervo/keys";
const CONTAINER_DOCUMENTS_PATH = "/var/steervo/documents";

const SMTP_SENDER = "steervo@example.test";
const SMTP_HOURLY_LIMIT = "100";
const SMTP_DAILY_LIMIT = "200";

const DESKTOP_VIEWPORT = { width: 1440, height: 1050 };
const MOBILE_VIEWPORT = { width: 390, height: 844 };

const PAGE_TIMEOUT_MS = 10_000;
const READINESS_ATTEMPTS = 100;
const READINESS_INTERVAL_MS = 150;
const MAIL_ATTEMPTS = 50;
const MAIL_INTERVAL_MS = 100;
// A new sign-in code for the same address is only sent after this time has passed.
const CODE_COOLDOWN_MS = 61_000;

const PURPOSE_DRAFT = "Ein unabhängiger Browser-Test.";

const database = process.env.STEERVO_E2E_DATABASE;
if (!database) {
  throw new Error(
    "STEERVO_E2E_DATABASE must point to a dedicated, empty PostgreSQL test database.",
  );
}

const databaseContainer = process.env.STEERVO_E2E_DATABASE_CONTAINER;
const httpsMode = process.env.STEERVO_E2E_HTTPS === "1";
const containerMode = process.env.STEERVO_E2E_CONTAINER === "1";
const origin =
  process.env.STEERVO_E2E_ORIGIN ||
  (httpsMode
    ? `https://${HTTPS_HOST}:${HTTPS_PROXY_PORT}`
    : `http://localhost:${APPLICATION_PORT}`);
// In HTTPS mode the application stays on plain HTTP behind the local TLS proxy.
const listenOrigin = httpsMode ? `http://${LOOPBACK_ADDRESS}:${APPLICATION_PORT}` : origin;
const mailOrigin = process.env.STEERVO_E2E_MAIL || "http://localhost:52080";

const output = resolve(".artifacts/browser");
await mkdir(output, { recursive: true });
const keys = await mkdtemp(join(tmpdir(), "steervo-browser-keys-"));

const setupToken = "browser-test-only-single-use-installation-token";
const environment = {
  ...process.env,
  ASPNETCORE_ENVIRONMENT: "Development",
  ASPNETCORE_URLS: listenOrigin,
  ConnectionStrings__Database: database,
  PublicUrl: origin,
  AllowedHosts: "*",
  Setup__Token: setupToken,
  DataProtection__Path: keys,
  DocumentStorage__Path: join(keys, "documents"),
  Smtp__Host: "localhost",
  Smtp__Port: process.env.STEERVO_E2E_SMTP_PORT || "52025",
  Smtp__StartTls: "false",
  Smtp__From: SMTP_SENDER,
  Smtp__HourlyLimit: SMTP_HOURLY_LIMIT,
  Smtp__DailyLimit: SMTP_DAILY_LIMIT,
};
const application = resolve("src/Steervo.Web/bin/Debug/net11.0/Steervo.Web.dll");
const containerName = `steervo-browser-${process.pid}-${Date.now()}`;
const migrationContainerName = `${containerName}-migrate`;
const keyVolume = `${containerName}-keys`;
const documentVolume = `${containerName}-documents`;

// ── Run state ───────────────────────────────────────────────────────────────

let server;
let browser;
let httpsProxy;
let log = "";
const checks = [];
const errors = [];

const record = (label) => {
  checks.push(label);
  console.log(`PASS ${label}`);
};

function pause(milliseconds) {
  return new Promise((done) => setTimeout(done, milliseconds));
}

// ── Application host ────────────────────────────────────────────────────────

function runApplication(migrate = false) {
  const processOptions = { env: environment, stdio: ["ignore", "pipe", "pipe"] };

  if (!containerMode) {
    return spawn(
      "dotnet",
      [application, ...(migrate ? ["--migrate"] : ["--contentRoot", resolve("src/Steervo.Web")])],
      processOptions,
    );
  }

  return runContainer(migrate, processOptions);
}

function runContainer(migrate, processOptions) {
  const network = JSON.parse(
    execFileSync("docker", ["network", "inspect", DOCKER_NETWORK], { encoding: "utf8" }),
  )[0];
  const proxyAddress = network.IPAM.Config.find((config) => config.Gateway)?.Gateway;
  if (!proxyAddress) {
    throw new Error("Test network has no gateway for the local HTTPS proxy.");
  }

  const settings = {
    ASPNETCORE_ENVIRONMENT: "Development",
    ASPNETCORE_URLS: "http://+:8080",
    PublicUrl: origin,
    AllowedHosts: "*",
    ConnectionStrings__Database:
      process.env.STEERVO_E2E_CONTAINER_DATABASE ||
      database.replace(/Host=[^;]+/i, "Host=postgres18").replace(/Port=[^;]+/i, "Port=5432"),
    Setup__Token: setupToken,
    DataProtection__Path: CONTAINER_KEYS_PATH,
    DocumentStorage__Path: CONTAINER_DOCUMENTS_PATH,
    TrustedProxies__0: proxyAddress,
    Smtp__Host: "mail",
    Smtp__Port: "1025",
    Smtp__StartTls: "false",
    Smtp__From: SMTP_SENDER,
    Smtp__HourlyLimit: SMTP_HOURLY_LIMIT,
    Smtp__DailyLimit: SMTP_DAILY_LIMIT,
  };
  // Only the serving container is reachable from the host; the migration run needs no port.
  const publishedPort = migrate
    ? []
    : ["-p", `${LOOPBACK_ADDRESS}:${new URL(listenOrigin).port}:8080`];
  const settingArguments = Object.entries(settings).flatMap(([key, value]) => [
    "--env",
    `${key}=${value}`,
  ]);

  return spawn(
    "docker",
    [
      "run",
      "--rm",
      "--pull=never",
      "--name",
      migrate ? migrationContainerName : containerName,
      "--network",
      DOCKER_NETWORK,
      "--mount",
      `type=volume,source=${keyVolume},target=${CONTAINER_KEYS_PATH}`,
      "--mount",
      `type=volume,source=${documentVolume},target=${CONTAINER_DOCUMENTS_PATH}`,
      ...publishedPort,
      ...settingArguments,
      process.env.STEERVO_IMAGE || "steervo:local",
      ...(migrate ? ["--migrate"] : []),
    ],
    processOptions,
  );
}

async function migrateDatabase() {
  const migration = runApplication(true);
  let migrationLog = "";
  migration.stdout.on("data", (chunk) => {
    migrationLog += chunk;
  });
  migration.stderr.on("data", (chunk) => {
    migrationLog += chunk;
  });

  const [migrationCode] = await once(migration, "exit");
  assert.equal(migrationCode, 0, migrationLog);
}

async function startServer() {
  server = runApplication();
  server.stdout.on("data", (chunk) => {
    log += chunk;
  });
  server.stderr.on("data", (chunk) => {
    log += chunk;
  });

  for (let attempt = 0; attempt < READINESS_ATTEMPTS; attempt++) {
    if (server.exitCode !== null) {
      throw new Error(`Application exited: ${log}`);
    }

    try {
      if ((await fetch(`${listenOrigin}/health/ready`)).ok) {
        return;
      }
    } catch {}

    await pause(READINESS_INTERVAL_MS);
  }

  throw new Error("Application did not become ready.");
}

async function stopServer() {
  if (server && server.exitCode === null) {
    const exit = once(server, "exit");
    server.kill("SIGTERM");
    await exit;
  }
}

/** Gives the next checks a fresh request-rate window while retaining data and sessions. */
async function restartServer() {
  await stopServer();
  await startServer();
}

// ── HTTPS proxy and browser ─────────────────────────────────────────────────

async function startHttpsProxy() {
  const key = join(keys, "test-tls.key");
  const cert = join(keys, "test-tls.crt");
  execFileSync(
    "openssl",
    [
      "req",
      "-x509",
      "-newkey",
      "rsa:2048",
      "-nodes",
      "-days",
      "1",
      "-keyout",
      key,
      "-out",
      cert,
      "-subj",
      `/CN=${HTTPS_HOST}`,
      "-addext",
      `subjectAltName=DNS:${HTTPS_HOST},DNS:${ALTERNATE_HTTPS_HOST}`,
    ],
    { stdio: "pipe" },
  );

  httpsProxy = createHttpsServer(
    { key: await readFile(key), cert: await readFile(cert) },
    forwardToApplication,
  );
  await new Promise((listening) =>
    httpsProxy.listen(Number(new URL(origin).port), LOOPBACK_ADDRESS, listening),
  );
}

function forwardToApplication(request, response) {
  const upstream = proxyRequest(
    `${listenOrigin}${request.url}`,
    { method: request.method, headers: { ...request.headers, "x-forwarded-proto": "https" } },
    (result) => {
      response.writeHead(result.statusCode, result.headers);
      result.pipe(response);
    },
  );
  upstream.on("error", () => {
    response.writeHead(502);
    response.end();
  });
  request.pipe(upstream);
}

function launchBrowser() {
  return chromium.launch({
    headless: true,
    executablePath: process.env.PLAYWRIGHT_EXECUTABLE_PATH || undefined,
    args: httpsMode
      ? [
          `--host-resolver-rules=MAP ${HTTPS_HOST} ${LOOPBACK_ADDRESS}, MAP *.${HTTPS_HOST} ${LOOPBACK_ADDRESS}`,
          "--no-proxy-server",
        ]
      : [],
  });
}

async function newUser() {
  const context = await browser.newContext({
    viewport: DESKTOP_VIEWPORT,
    reducedMotion: "reduce",
    ignoreHTTPSErrors: httpsMode,
  });
  // The journey must not depend on anything outside the application under test.
  await context.route("**/*", (route) =>
    new URL(route.request().url()).origin === origin ? route.continue() : route.abort(),
  );

  const page = await context.newPage();
  page.setDefaultTimeout(PAGE_TIMEOUT_MS);
  page.on("pageerror", (error) => errors.push(error.message));
  page.on("dialog", (dialog) => dialog.accept());
  return { context, page };
}

// ── Sign-in helpers ─────────────────────────────────────────────────────────

async function latestCode(email) {
  for (let attempt = 0; attempt < MAIL_ATTEMPTS; attempt++) {
    const messages = await (await fetch(`${mailOrigin}/api/email`)).json();
    const matching = messages.filter(
      (summary) =>
        summary.to?.some((recipient) => recipient.address.toLowerCase() === email.toLowerCase()) &&
        summary.subject?.includes("Anmeldecode"),
    );

    if (matching.length) {
      const latest = matching.sort(
        (first, second) => new Date(second.time) - new Date(first.time),
      )[0];
      const message = await (await fetch(`${mailOrigin}/api/email/${latest.id}`)).json();
      const code = message.text?.match(/\b\d{6}\b/);
      if (code) {
        return code[0];
      }
    }

    await pause(MAIL_INTERVAL_MS);
  }

  throw new Error("Email code was not delivered.");
}

async function verify(page, email) {
  await page.waitForURL(/\/Auth\/Verify/);
  await page.getByLabel("Einmalcode").fill(await latestCode(email));
  await page.getByRole("button", { name: "Anmelden", exact: true }).click();
  await page.waitForURL(origin + "/");
}

async function signIn(page, email) {
  await page.goto(`${origin}/Auth/Login`);
  await page.getByLabel("E-Mail-Adresse", { exact: true }).fill(email);
  await page.getByRole("button", { name: "Anmeldecode senden" }).click();
  await verify(page, email);
}

// ── Workspace helpers ───────────────────────────────────────────────────────

function workspaceVersion(page) {
  return page.locator("#evaluation-workspace").getAttribute("data-version");
}

/** Runs a workspace change and waits until the new version has been swapped in and settled. */
async function mutate(page, operation) {
  const oldVersion = await workspaceVersion(page);
  await operation();
  await page.waitForFunction(
    (old) => document.querySelector("#evaluation-workspace")?.dataset.version !== old,
    oldVersion,
  );
  await page.waitForFunction(() => !document.querySelector(".htmx-settling, .htmx-request"));
}

async function post(page, path, values) {
  const csrf = await page.locator('input[name="__RequestVerificationToken"]').first().inputValue();
  return browserRequest(page, path, { __RequestVerificationToken: csrf, ...values });
}

/** Sends a request from inside the page, so it carries the session of that browser context. */
async function browserRequest(page, path, form) {
  const result = await page.evaluate(
    async ({ url, form }) => {
      const response = await fetch(url, {
        method: form ? "POST" : "GET",
        body: form ? new URLSearchParams(form) : undefined,
        credentials: "same-origin",
        redirect: "manual",
      });
      return { status: response.status, text: await response.text() };
    },
    { url: `${origin}${path}`, form },
  );
  return { status: () => result.status, text: async () => result.text };
}

async function exportEvaluation(page, path) {
  return JSON.parse(await (await browserRequest(page, `${path}?handler=Export`)).text());
}

function newEvaluationDialog(page) {
  return page.getByRole("dialog", { name: "Neue Evaluation", exact: true });
}

function criterionCard(page, name) {
  return page
    .locator(".criterion")
    .filter({ has: page.getByRole("heading", { name, exact: true }) });
}

async function selectRegion(page) {
  await page.locator("#new-region-country").selectOption("CH");
  await page.locator("#new-region-currency").fill("EUR");
}

async function openGeneralSettings(page, casePath) {
  await page.goto(`${origin}${casePath}`);
  await page
    .locator(".workflow-nav")
    .getByRole("link", { name: "Grundlagen", exact: true })
    .click();
  await page
    .locator(".workspace-subnav")
    .getByRole("link", { name: "Allgemein", exact: true })
    .click();
}

// ── Scenario: installation setup ────────────────────────────────────────────

/** Sets up the installation with the owner account; returns when its first code was requested. */
async function setUpInstallation(page, ownerEmail) {
  await page.goto(origin);
  await page.getByLabel("Einrichtungsschlüssel").fill(setupToken);
  await page.getByLabel("Name", { exact: true }).fill("Test Owner");
  await page.getByLabel("E-Mail-Adresse", { exact: true }).fill(ownerEmail);
  await checkSetup({ page, token: setupToken, output, record });

  const setupAt = Date.now();
  await page.getByRole("button", { name: "E-Mail-Adresse bestätigen" }).click();
  await checkVerify({ page, code: await latestCode(ownerEmail), output, record });
  await verify(page, ownerEmail);

  assert.equal(await page.locator(".case-card").count(), 0);
  assert.equal((await browserRequest(page, "/Auth/Setup")).status(), 404);
  assert.equal((await browserRequest(page, "/Identity/Account/Register")).status(), 404);
  record("Single-use setup, verified email and empty workspace");
  await page.screenshot({ path: join(output, "01-empty-desktop.png"), fullPage: true });

  return setupAt;
}

// ── Scenario: evaluation creation ───────────────────────────────────────────

/** Creates the main evaluation through the dialog and returns its path. */
async function createFirstEvaluation(page) {
  const creation = newEvaluationDialog(page);
  const firstEvaluation = page.getByRole("button", { name: "Erste Evaluation anlegen" });

  // The dialog opens focused and an untouched one closes back onto its opener.
  assert.equal(await creation.isVisible(), false);
  assert.equal(await page.getByRole("button", { name: "Neue Evaluation", exact: true }).count(), 0);
  await firstEvaluation.click();
  assert.equal(await creation.isVisible(), true);
  assert.equal(
    await creation
      .getByLabel("Name der Evaluation")
      .evaluate((input) => input === document.activeElement),
    true,
  );
  await page.screenshot({ path: join(output, "05-create-desktop.png"), fullPage: true });
  await page.keyboard.press("Escape");
  assert.equal(await creation.isVisible(), false);
  assert.equal(await firstEvaluation.evaluate((button) => button === document.activeElement), true);

  // An invalid submission keeps the draft.
  await firstEvaluation.click();
  await creation.getByLabel("Name der Evaluation").fill("   ");
  await selectRegion(page);
  await creation.getByLabel("Worum geht es?").fill(PURPOSE_DRAFT);
  await creation.getByRole("button", { name: "Evaluation anlegen", exact: false }).click();
  await creation.locator(".validation-summary-errors").waitFor();
  assert.equal(await creation.getByLabel("Worum geht es?").inputValue(), PURPOSE_DRAFT);

  // On a phone the dialog fits the viewport and keeps keyboard focus inside.
  await page.setViewportSize(MOBILE_VIEWPORT);
  const bounds = await creation.boundingBox();
  assert.ok(
    bounds.x >= 0 &&
      bounds.y >= 0 &&
      bounds.x + bounds.width <= MOBILE_VIEWPORT.width &&
      bounds.y + bounds.height <= MOBILE_VIEWPORT.height,
  );
  for (let index = 0; index < 7; index++) {
    await page.keyboard.press("Tab");
    // Native dialogs allow focus into browser chrome, but keep background controls inert.
    assert.equal(
      await page.evaluate(
        () =>
          document.activeElement === document.body || !!document.activeElement?.closest("dialog"),
      ),
      true,
    );
  }
  await page.screenshot({ path: join(output, "06-create-mobile.png"), fullPage: true });

  // Cancelling a changed draft asks first; continuing keeps it.
  await creation.getByRole("button", { name: "Abbrechen", exact: true }).click();
  await page.getByRole("button", { name: "Weiterbearbeiten", exact: true }).click();
  assert.equal(await creation.getByLabel("Worum geht es?").inputValue(), PURPOSE_DRAFT);

  await page.setViewportSize(DESKTOP_VIEWPORT);
  await page.getByLabel("Name der Evaluation").fill("Meine Testentscheidung");
  await selectRegion(page);
  await page.getByLabel("Worum geht es?").fill(PURPOSE_DRAFT);
  await creation.getByRole("button", { name: "Evaluation anlegen" }).click();
  await page.waitForURL(/\/evaluations\//);
  const casePath = new URL(page.url()).pathname;

  // With an existing evaluation the overview offers the regular creation button.
  await page.goto(origin);
  await page.getByRole("button", { name: /Neue Evaluation/ }).click();
  assert.equal(await creation.isVisible(), true);
  await creation.getByRole("button", { name: "Schliessen", exact: true }).click();
  assert.equal(await page.locator(".case-card").count(), 1);

  await page.goto(`${origin}${casePath}?section=criteria`);
  record(
    "Evaluation creation opens a focused modal, preserves invalid drafts and works on desktop/mobile",
  );

  return casePath;
}

// ── Scenario: groups and criteria ───────────────────────────────────────────

/**
 * Builds the groups and the three criteria the later checks rely on.
 * Returns the card and id of the criterion whose identity has to survive the run.
 */
async function defineCriteria(page, casePath) {
  await addGroups(page, ["Alltag", "Komfort"]);

  const criterionForm = page
    .locator("form")
    .filter({ has: page.locator('input[name="action"][value="add-criterion"]') });
  const addCriterionButton = criterionForm.getByRole("button", { name: "Kriterium hinzufügen" });

  await page.locator("#add-criterion").click();
  await checkInvalidCriterionIsRejected(page, casePath, criterionForm, addCriterionButton);
  await criterionForm.getByLabel("Bezeichnung").fill("Gute Sicht");
  await criterionForm.getByLabel("Gruppe", { exact: true }).selectOption({ label: "Alltag" });
  await mutate(page, () => addCriterionButton.click());

  await page.locator("#add-criterion").click();
  await criterionForm.getByLabel("Bezeichnung").fill("Kapazität");
  await criterionForm.getByLabel("Art des Kriteriums").selectOption("Measurement");
  await criterionForm.getByLabel("Schwellenwert").fill("12.5");
  await criterionForm.getByLabel("Einheit", { exact: true }).fill("units");
  await criterionForm.getByLabel("Gruppe", { exact: true }).selectOption({ label: "Alltag" });
  await mutate(page, () => addCriterionButton.click());

  await page.locator("#add-criterion").click();
  await criterionForm.getByLabel("Bezeichnung").fill("Sitzgefühl");
  await criterionForm.getByLabel("Art des Kriteriums").selectOption("Rating");
  await criterionForm.getByLabel("Priorität").selectOption("Preferred");
  await criterionForm.getByLabel("Gruppe", { exact: true }).selectOption({ label: "Komfort" });
  await mutate(page, () => addCriterionButton.click());

  // The threshold is entered with a decimal point and shown in the local number format.
  assert.match(await criterionCard(page, "Kapazität").innerText(), /12,5/);

  const sightCriterion = criterionCard(page, "Gute Sicht");
  const sightCriterionId = await sightCriterion.getAttribute("data-criterion-id");

  await mutate(page, () =>
    page.getByRole("button", { name: "Kriterium Kapazität nach oben", exact: true }).click(),
  );
  await mutate(page, () =>
    page.getByRole("button", { name: "Gruppe Komfort nach oben", exact: true }).click(),
  );

  // Moving a criterion into another group must not replace it with a new one.
  await sightCriterion.locator("[data-editor-open]").click();
  const criterionEditor = sightCriterion.locator("form.edit-fields");
  await criterionEditor.getByLabel("Gruppe", { exact: true }).selectOption({ label: "Komfort" });
  await mutate(page, () =>
    criterionEditor.getByRole("button", { name: "Speichern", exact: true }).click(),
  );
  assert.equal(await sightCriterion.getAttribute("data-criterion-id"), sightCriterionId);

  const comfortGroup = page
    .locator(".criterion-group")
    .filter({ has: page.getByRole("heading", { name: "Komfort", exact: true }) });
  await comfortGroup.locator(".group-heading [data-editor-open]").click();
  await comfortGroup.getByLabel("Gruppenname").fill("Wohlfühlen");
  await mutate(page, () =>
    comfortGroup.getByRole("button", { name: "Speichern", exact: true }).click(),
  );

  record(
    "Three criterion types, decimal threshold, group/criterion ordering and identity-preserving reassignment",
  );

  return { sightCriterion, sightCriterionId };
}

async function addGroups(page, names) {
  const groupEditor = page.locator("#new-group-editor");

  for (const name of names) {
    await page.locator("#add-group").click();
    await groupEditor.getByLabel("Gruppenname").fill(name);
    await mutate(page, () =>
      groupEditor.getByRole("button", { name: "Gruppe hinzufügen" }).click(),
    );
  }
}

/** Submits a blank name and expects an HTMX validation response that keeps the editor open. */
async function checkInvalidCriterionIsRejected(page, casePath, criterionForm, addCriterionButton) {
  await criterionForm.getByLabel("Bezeichnung").fill("   ");
  const invalidSubmission = page.waitForResponse(
    (response) =>
      new URL(response.url()).pathname === casePath && response.request().method() === "POST",
  );
  await addCriterionButton.click();

  const invalidResponse = await invalidSubmission;
  assert.equal(invalidResponse.status(), 422);
  assert.equal(invalidResponse.request().headers()["hx-request"], "true");
  await page.locator("#new-criterion-editor .editor-error").waitFor();
  await page.waitForFunction(() => document.querySelector("#name-new")?.value === "   ");
}

// ── Scenario: stale writes and antiforgery ──────────────────────────────────

async function checkStaleWritesAndAntiforgery(page, casePath) {
  const staleVersion = await workspaceVersion(page);
  await mutate(page, () =>
    page.getByRole("button", { name: "Gruppe Alltag nach oben", exact: true }).click(),
  );

  const conflict = await post(page, casePath, {
    action: "add-group",
    name: "Stale write",
    version: staleVersion,
  });
  assert.equal(conflict.status(), 409);
  assert.ok(
    (await conflict.text()).includes("inzwischen ge"),
    "Conflict response must explain the stale version.",
  );

  const csrfRejected = await browserRequest(page, casePath, {
    action: "add-group",
    name: "No token",
    version: "1",
  });
  assert.equal(csrfRejected.status(), 400);

  record("Stale writes rejected and antiforgery required");
  await page.screenshot({ path: join(output, "02-criteria-desktop.png"), fullPage: true });
}

// ── Scenario: invitation and sharing ────────────────────────────────────────

/** Invites the second account, signs it in and returns its browser context and page. */
async function inviteReader(page, readerEmail, casePath) {
  await page.getByRole("link", { name: "Einladungen", exact: true }).click();
  await page.locator("#invite-person").click();
  await page.getByLabel("Name", { exact: true }).fill("Test Reader");
  await page.getByLabel("E-Mail-Adresse", { exact: true }).fill(readerEmail);
  await page.getByRole("button", { name: "Einladung senden" }).click();

  const reader = await newUser();
  await signIn(reader.page, readerEmail);

  assert.equal(await reader.page.locator(".case-card").count(), 0);
  assert.equal((await browserRequest(reader.page, casePath)).status(), 403);
  assert.equal((await browserRequest(reader.page, "/Account/Invitations")).status(), 403);
  const invitationAttempt = await post(reader.page, "/Account/Invitations", {
    Name: "Blocked",
    Email: "blocked@example.test",
  });
  assert.equal(invitationAttempt.status(), 403);

  record("Invitations do not share data; invited accounts cannot invite");
  return reader;
}

async function checkReaderAndEditorRestrictions(page, reader, casePath, ownerEmail, readerEmail) {
  const share = page.locator(".share-form");
  const grantButton = share.getByRole("button", { name: "Zugriff freigeben" });

  // Reader: no editing controls and every write is rejected.
  await openGeneralSettings(page, casePath);
  await page.locator("#add-share").click();
  await share.getByLabel("E-Mail-Adresse").fill(readerEmail);
  await mutate(page, () => grantButton.click());

  await reader.page.goto(`${origin}${casePath}?section=criteria`);
  assert.equal(await reader.page.locator('input[value="add-criterion"]').count(), 0);
  assert.equal(await reader.page.locator("[data-drag-handle]").count(), 0);

  const readerVersion = await workspaceVersion(reader.page);
  for (const action of ["add-group", "delete", "share", "place-group", "place-criterion"]) {
    const response = await post(reader.page, casePath, {
      action,
      name: "Blocked",
      email: ownerEmail,
      access: "Editor",
      version: readerVersion,
    });
    assert.equal(response.status(), 403);
  }

  // Editor: content can be changed, deleting and sharing stay reserved for the owner.
  await page.locator("#add-share").click();
  await share.getByLabel("E-Mail-Adresse").fill(readerEmail);
  await share.getByLabel("Berechtigung").selectOption("Editor");
  await mutate(page, () => grantButton.click());

  await reader.page.reload();
  const editorVersion = await workspaceVersion(reader.page);
  const addedGroup = await post(reader.page, casePath, {
    action: "add-group",
    name: "Gemeinsam",
    version: editorVersion,
  });
  assert.equal(addedGroup.status(), 200);

  await reader.page.reload();
  const versionAfterEdit = await workspaceVersion(reader.page);
  for (const action of ["delete", "share"]) {
    const response = await post(reader.page, casePath, {
      action,
      email: ownerEmail,
      access: "Reader",
      version: versionAfterEdit,
    });
    assert.equal(response.status(), 403);
  }

  record("Reader/editor restrictions cover direct requests; editor can change content");
}

// ── Scenario: responsive layout ─────────────────────────────────────────────

async function checkResponsiveLayout(page) {
  await page.reload();
  await page.setViewportSize(MOBILE_VIEWPORT);
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: join(output, "03-criteria-mobile.png"), fullPage: true });
  await page.setViewportSize(DESKTOP_VIEWPORT);

  record("Responsive layout at desktop and 390px mobile width");
}

// ── Scenario: passkeys ──────────────────────────────────────────────────────

/** Opens the account page under addresses that differ from the configured public origin. */
async function checkPasskeyOriginGuidance(owner) {
  const wrongOrigins = [
    [`https://${ALTERNATE_HTTPS_HOST}:${HTTPS_PROXY_PORT}`, "Passkeys sind für"],
    [
      `http://${HTTPS_HOST}:${APPLICATION_PORT}`,
      "Passkeys benötigen eine sichere HTTPS-Verbindung",
    ],
  ];

  for (const [address, expected] of wrongOrigins) {
    await checkPasskeyNoticeAt(owner, address, expected);
  }

  record(
    "Passkey enrollment explains wrong HTTPS origins and insecure HTTP before contacting an authenticator",
  );
}

async function checkPasskeyNoticeAt(owner, address, expected) {
  const context = await browser.newContext({ ignoreHTTPSErrors: true });
  // Carry the owner session over to the alternate address.
  await context.addCookies(
    (await owner.context.cookies()).map((cookie) => ({
      ...cookie,
      domain: new URL(address).hostname,
      secure: address.startsWith("https:"),
    })),
  );

  const alternate = await context.newPage();
  let optionsRequests = 0;
  alternate.on("request", (request) => {
    if (request.url().includes("handler=Options")) {
      optionsRequests++;
    }
  });

  await alternate.goto(`${address}/Account`);
  await alternate.locator("#add-passkey").click();
  await alternate.getByLabel("Name des Passkeys").fill("Wrong origin");
  await alternate.locator("#save-passkey").click();

  const notice = alternate.locator("[data-passkey-error]");
  await notice.filter({ hasText: expected }).waitFor();
  assert.ok((await notice.innerText()).includes(origin));
  assert.equal(optionsRequests, 0);
  await context.close();
}

/** Adds a virtual authenticator to the owner's browser; returns the handles to remove it again. */
async function addVirtualAuthenticator(owner, page) {
  await owner.context.addInitScript(() => {
    const originalToJSON = PublicKeyCredential.prototype.toJSON;
    PublicKeyCredential.prototype.toJSON = function () {
      const result = originalToJSON.call(this);
      // Some passkey providers omit empty extension results from their JSON response.
      delete result.clientExtensionResults;
      return result;
    };
  });

  const cdp = await owner.context.newCDPSession(page);
  await cdp.send("WebAuthn.enable");
  const authenticator = await cdp.send("WebAuthn.addVirtualAuthenticator", {
    options: {
      protocol: "ctap2",
      transport: "internal",
      hasResidentKey: true,
      hasUserVerification: true,
      isUserVerified: true,
      automaticPresenceSimulation: true,
    },
  });

  return { cdp, authenticator };
}

async function checkPasskeyEnrollmentAndSignIn(page) {
  await checkEnrollment({ page, origin, output, record });

  await page.goto(`${origin}/Account`);
  await page.locator("#add-passkey").click();
  await page.getByLabel("Name des Passkeys").fill("Testgerät");
  await page.locator("#save-passkey").click();
  await page.getByRole("heading", { name: "Testgerät", exact: true }).waitFor();

  await page.getByRole("button", { name: "Abmelden", exact: true }).click();
  await page.getByRole("button", { name: "Mit Passkey anmelden", exact: true }).click();
  await page.waitForURL(origin + "/");

  record(
    `WebAuthn enrollment and sign-in tolerate omitted extension results${httpsMode ? " on a custom HTTPS domain" : ""}`,
  );
}

async function checkPasskeyRemovalAndEmailRecovery({
  page,
  owner,
  ownerEmail,
  setupAt,
  cdp,
  authenticator,
}) {
  await page.goto(`${origin}/Account`);
  await checkRename({ page, owner, browser, origin, output, record });

  await page.locator(".passkey-row [data-editor-open]").click();
  await page
    .locator(".passkey-row")
    .getByLabel("Name", { exact: true })
    .fill("Umbenanntes Testgerät");
  await page.locator(".passkey-row [id^=save-passkey-]").click();
  await page.getByRole("heading", { name: "Umbenanntes Testgerät", exact: true }).waitFor();

  await page.getByRole("button", { name: "Passkey entfernen", exact: true }).click();
  await page.locator("[data-confirm-accept]").click();
  await page.getByRole("heading", { name: "Noch kein Passkey hinterlegt.", exact: true }).waitFor();
  await cdp.send("WebAuthn.removeVirtualAuthenticator", authenticator);
  await page.getByRole("button", { name: "Abmelden", exact: true }).click();

  // Without a passkey the owner signs in by email again, once the code cooldown has passed.
  const cooldownRemaining = CODE_COOLDOWN_MS - (Date.now() - setupAt);
  if (cooldownRemaining > 0) {
    await pause(cooldownRemaining);
  }
  await signIn(page, ownerEmail);

  record("Passkey rename/removal and email recovery");
}

// ── Scenario: restart persistence ───────────────────────────────────────────

async function checkRestartPreservesData({
  page,
  casePath,
  sightCriterion,
  sightCriterionId,
  candidateChecks,
  offerChecks,
}) {
  await stopServer();
  if (databaseContainer) {
    execFileSync("docker", ["restart", databaseContainer], {
      stdio: "pipe",
    });
  }
  await startServer();

  await page.goto(`${origin}${casePath}?section=criteria`);
  assert.equal(await page.locator(".criterion").count(), 3);
  assert.equal(await sightCriterion.getAttribute("data-criterion-id"), sightCriterionId);

  const persistedCandidates = await exportEvaluation(page, casePath);
  const persistedOffers = await exportEvaluation(page, offerChecks.casePath);

  assert.equal(persistedOffers.currency, "EUR");
  assert.equal(
    persistedOffers.offers.find((offer) => offer.id === offerChecks.firstId).revisions.length,
    4,
  );
  assert.equal(persistedCandidates.candidates.length, 8);
  assert.equal(
    persistedCandidates.candidates.find((item) => item.id === candidateChecks.firstId)
      .specifications.kerbWeightKg,
    2100,
  );
  assert.equal(
    persistedCandidates.assessments.find(
      (item) =>
        item.candidateId === candidateChecks.firstId && item.criterionId === candidateChecks.rating,
    ).ratingValue,
    2,
  );

  record(
    `${databaseContainer ? "Application and database" : "Application"} restart preserves session keys, cases, criteria, ordering and shares`,
  );
}

// ── Scenario: revocation, export and import ─────────────────────────────────

async function checkRevocation(page, reader, casePath) {
  await openGeneralSettings(page, casePath);
  await mutate(page, () =>
    page.locator(".share-row").getByRole("button", { name: "Entziehen", exact: true }).click(),
  );

  assert.equal((await browserRequest(reader.page, casePath)).status(), 403);
  assert.equal((await browserRequest(reader.page, `${casePath}?handler=Export`)).status(), 403);
  record("Revocation takes effect for an existing authenticated session");
}

async function checkExportAndImport(page, casePath, candidateChecks) {
  const exportFile = await downloadExport(page);

  const fileInput = page.getByLabel("Exportdatei (JSON oder ZIP)");
  const importButton = page.getByRole("button", {
    name: "Als neue Evaluation importieren",
    exact: true,
  });
  const selectedFileName = () => page.locator("#upload").evaluate((input) => input.files[0].name);

  // An unreadable file is reported in the import editor and stays selected.
  await page.goto(origin);
  await page.getByRole("button", { name: "Evaluation importieren", exact: true }).click();
  await fileInput.setInputFiles({
    name: "invalid.json",
    mimeType: "application/json",
    buffer: Buffer.from("{invalid"),
  });
  await importButton.click();
  await page.locator("#import-evaluation-editor .editor-error:not([hidden])").waitFor();
  assert.equal(await newEvaluationDialog(page).isVisible(), false);
  assert.equal(await selectedFileName(), "invalid.json");
  await page.keyboard.press("Escape");
  await page.getByRole("button", { name: "Weiterbearbeiten", exact: true }).click();
  assert.equal(await selectedFileName(), "invalid.json");

  // The real export becomes a separate evaluation without the original's shares.
  await fileInput.setInputFiles(exportFile);
  await importButton.click();
  await page.waitForURL(/\/evaluations\//);
  assert.notEqual(new URL(page.url()).pathname, casePath);
  assert.equal(await page.locator(".criterion").count(), 3);
  assert.equal(await page.locator(".share-row").count(), 0);

  const importedCandidates = await exportEvaluation(page, new URL(page.url()).pathname);
  assert.equal(importedCandidates.candidates.length, 8);
  assert.equal(
    importedCandidates.candidates.find((item) => item.model === "Nord").specifications
      .accelerationSeconds,
    4.5,
  );
  assert.ok(importedCandidates.assessments.length >= 6);
  assert.ok(importedCandidates.candidates.every((item) => item.id !== candidateChecks.firstId));

  record("Authorized JSON export and import as an independent private evaluation");
}

async function downloadExport(page) {
  const downloadReady = page.waitForEvent("download");
  await page
    .locator("#evaluation-data")
    .getByRole("link", { name: "Exportieren", exact: true })
    .click();
  const download = await downloadReady;

  const exportFile = join(output, "evaluation.json");
  await download.saveAs(exportFile);
  return exportFile;
}

// ── Journey ─────────────────────────────────────────────────────────────────

/**
 * Runs all checks in order. The run is stateful: later checks rely on the accounts,
 * evaluations and sessions earlier ones leave behind, so the sequence must not change.
 */
async function runJourney() {
  const owner = await newUser();
  const page = owner.page;
  const stamp = Date.now();
  const ownerEmail = `owner-${stamp}@example.test`;
  const readerEmail = `reader-${stamp}@example.test`;

  const setupAt = await setUpInstallation(page, ownerEmail);
  const casePath = await createFirstEvaluation(page);
  const { sightCriterion, sightCriterionId } = await defineCriteria(page, casePath);

  await checkSorting({ page, owner, origin, output, post, mutate, record });

  await checkStaleWritesAndAntiforgery(page, casePath);
  const reader = await inviteReader(page, readerEmail, casePath);
  await checkReaderAndEditorRestrictions(page, reader, casePath, ownerEmail, readerEmail);
  await checkResponsiveLayout(page);

  // Each larger check module starts in a fresh request-rate window.
  await restartServer();
  await checkEvaluations({
    page,
    reader,
    readerEmail,
    origin,
    output,
    post,
    browserRequest,
    mutate,
    record,
  });

  await restartServer();
  await page.goto(`${origin}${casePath}`);
  const candidateChecks = await checkCandidates({
    page,
    reader,
    origin,
    output,
    post,
    browserRequest,
    record,
  });

  await restartServer();
  const offerChecks = await checkOffers({
    page,
    reader,
    readerEmail,
    origin,
    output,
    post,
    browserRequest,
    record,
  });

  await restartServer();
  await checkOptionalEquipment({
    page,
    reader,
    readerEmail,
    origin,
    output,
    post,
    browserRequest,
    record,
    casePath,
    ...candidateChecks,
    offerChecks,
  });

  await restartServer();
  await checkDocuments({
    page,
    reader,
    readerEmail,
    origin,
    output,
    post,
    browserRequest,
    record,
    restart: restartServer,
  });

  await restartServer();
  await checkCosts({
    page,
    reader,
    readerEmail,
    origin,
    output,
    post,
    browserRequest,
    record,
    restart: restartServer,
  });

  await restartServer();
  await checkScenarios({
    page,
    reader,
    readerEmail,
    origin,
    output,
    post,
    browserRequest,
    record,
    restart: restartServer,
  });

  await restartServer();
  await checkAccess({ page, reader, newUser, origin, output, record });
  await checkRateLimit({ page, origin, record });

  await restartServer();
  if (httpsMode) {
    await checkPasskeyOriginGuidance(owner);
  }
  const { cdp, authenticator } = await addVirtualAuthenticator(owner, page);
  await checkPasskeyEnrollmentAndSignIn(page);

  await checkRestartPreservesData({
    page,
    casePath,
    sightCriterion,
    sightCriterionId,
    candidateChecks,
    offerChecks,
  });

  await checkPasskeyRemovalAndEmailRecovery({
    page,
    owner,
    ownerEmail,
    setupAt,
    cdp,
    authenticator,
  });
  await checkRevocation(page, reader, casePath);
  await checkExportAndImport(page, casePath, candidateChecks);
}

async function writeResults() {
  assert.deepEqual(errors, []);
  await writeFile(
    join(output, "results.json"),
    JSON.stringify({ checks, browserErrors: errors }, null, 2),
  );
  console.log(`Completed ${checks.length} browser checks.`);
}

// ── Cleanup ─────────────────────────────────────────────────────────────────

async function cleanUp() {
  if (browser) {
    await browser.close();
  }

  if (httpsProxy) {
    httpsProxy.closeAllConnections();
    await new Promise((closed) => httpsProxy.close(closed));
  }

  await stopServer();

  if (containerMode) {
    removeContainerResources();
  }

  await rm(keys, { recursive: true, force: true });
}

/** Best effort: containers started with --rm are usually gone already. */
function removeContainerResources() {
  for (const name of [containerName, migrationContainerName]) {
    try {
      execFileSync("docker", ["rm", "-f", name], { stdio: "ignore" });
    } catch {}
  }

  try {
    execFileSync("docker", ["volume", "rm", keyVolume, documentVolume], { stdio: "ignore" });
  } catch {}
}

// ── Entry point ─────────────────────────────────────────────────────────────

try {
  await migrateDatabase();
  await startServer();
  if (httpsMode) {
    await startHttpsProxy();
  }
  browser = await launchBrowser();

  await runJourney();
  await writeResults();
} catch (error) {
  await writeFile(join(output, "server.log"), log);
  if (errors.length) {
    console.error("Browser errors:", errors);
  }
  throw error;
} finally {
  await cleanUp();
}
