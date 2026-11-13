import { chromium } from 'playwright';
import { checkEvaluations } from './evaluations.mjs';
import { checkSorting } from './sorting.mjs';
import { checkCandidates } from './candidates.mjs';
import { checkOffers } from './offers.mjs';
import { checkDocuments } from './documents.mjs';
import { checkCosts } from './costs.mjs';
import { checkScenarios } from './scenarios.mjs';
import assert from 'node:assert/strict';
import { spawn, execFileSync } from 'node:child_process';
import { once } from 'node:events';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { request as proxyRequest } from 'node:http';
import { createServer as createHttpsServer } from 'node:https';
import { tmpdir } from 'node:os';
import { resolve, join } from 'node:path';

const database = process.env.STEERVO_E2E_DATABASE;
if (!database) throw new Error('STEERVO_E2E_DATABASE must point to a dedicated, empty PostgreSQL test database.');
const httpsMode = process.env.STEERVO_E2E_HTTPS === '1';
const containerMode = process.env.STEERVO_E2E_CONTAINER === '1';
const origin = process.env.STEERVO_E2E_ORIGIN || (httpsMode ? 'https://steervo-browser.test:5190' : 'http://localhost:5189');
const listenOrigin = httpsMode ? 'http://127.0.0.1:5189' : origin;
const mailOrigin = process.env.STEERVO_E2E_MAIL || 'http://localhost:52080';
const output = resolve('.artifacts/browser');
await mkdir(output, { recursive: true });
const keys = await mkdtemp(join(tmpdir(), 'steervo-browser-keys-'));
const token = 'browser-test-only-single-use-installation-token';
const environment = {
  ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: listenOrigin,
  ConnectionStrings__Database: database, PublicUrl: origin, AllowedHosts: '*',
  Setup__Token: token, DataProtection__Path: keys, DocumentStorage__Path: join(keys, 'documents'), Smtp__Host: 'localhost', Smtp__Port: process.env.STEERVO_E2E_SMTP_PORT || '52025',
  Smtp__StartTls: 'false', Smtp__From: 'steervo@example.test', Smtp__HourlyLimit: '100', Smtp__DailyLimit: '200'
};
const application = resolve('src/Steervo.Web/bin/Debug/net10.0/Steervo.Web.dll');
const containerName = `steervo-browser-${process.pid}-${Date.now()}`;
const keyVolume = `${containerName}-keys`;
const documentVolume = `${containerName}-documents`;
let server;
let browser;
let httpsProxy;
let log = '';
const checks = [];
const errors = [];
const record = label => { checks.push(label); console.log(`PASS ${label}`); };
function runApplication(migrate = false) {
  const processOptions = { env: environment, stdio: ['ignore', 'pipe', 'pipe'] };
  if (!containerMode) return spawn('dotnet', [application, ...(migrate ? ['--migrate'] : ['--contentRoot', resolve('src/Steervo.Web')])], processOptions);
  const network = JSON.parse(execFileSync('docker', ['network', 'inspect', 'steervo-tests_default'], { encoding: 'utf8' }))[0];
  const proxyAddress = network.IPAM.Config.find(config => config.Gateway)?.Gateway;
  if (!proxyAddress) throw new Error('Test network has no gateway for the local HTTPS proxy.');
  const settings = {
    ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: 'http://+:8080', PublicUrl: origin, AllowedHosts: '*',
    ConnectionStrings__Database: process.env.STEERVO_E2E_CONTAINER_DATABASE || database.replace(/Host=[^;]+/i, 'Host=postgres18').replace(/Port=[^;]+/i, 'Port=5432'),
    Setup__Token: token, DataProtection__Path: '/var/steervo/keys', DocumentStorage__Path: '/var/steervo/documents', TrustedProxies__0: proxyAddress,
    Smtp__Host: 'mail', Smtp__Port: '1025', Smtp__StartTls: 'false', Smtp__From: 'steervo@example.test', Smtp__HourlyLimit: '100', Smtp__DailyLimit: '200'
  };
  return spawn('docker', ['run', '--rm', '--pull=never', '--name', migrate ? `${containerName}-migrate` : containerName,
    '--network', 'steervo-tests_default', '--mount', `type=volume,source=${keyVolume},target=/var/steervo/keys`,
    '--mount', `type=volume,source=${documentVolume},target=/var/steervo/documents`,
    ...(!migrate ? ['-p', `127.0.0.1:${new URL(listenOrigin).port}:8080`] : []),
    ...Object.entries(settings).flatMap(([key, value]) => ['--env', `${key}=${value}`]),
    process.env.STEERVO_IMAGE || 'steervo:local', ...(migrate ? ['--migrate'] : [])], processOptions);
}
async function startServer() {
  server = runApplication();
  server.stdout.on('data', chunk => { log += chunk; }); server.stderr.on('data', chunk => { log += chunk; });
  for (let i = 0; i < 100; i++) {
    if (server.exitCode !== null) throw new Error(`Application exited: ${log}`);
    try { if ((await fetch(`${listenOrigin}/health/ready`)).ok) return; } catch {}
    await new Promise(resolve => setTimeout(resolve, 150));
  }
  throw new Error('Application did not become ready.');
}
async function stopServer() {
  if (server && server.exitCode === null) { const exit = once(server, 'exit'); server.kill('SIGTERM'); await exit; }
}
async function newUser() {
  const context = await browser.newContext({ viewport: { width: 1440, height: 1050 }, reducedMotion: 'reduce', ignoreHTTPSErrors: httpsMode });
  await context.route('**/*', route => new URL(route.request().url()).origin === origin ? route.continue() : route.abort());
  const page = await context.newPage(); page.setDefaultTimeout(10_000);
  page.on('pageerror', error => errors.push(error.message));
  page.on('dialog', dialog => dialog.accept());
  return { context, page };
}
async function latestCode(email) {
  for (let i = 0; i < 50; i++) {
    const messages = await (await fetch(`${mailOrigin}/email`)).json();
    const matching = messages.filter(m => m.to?.some(to => to.address.toLowerCase() === email.toLowerCase()) && m.subject?.includes('Anmeldecode'));
    if (matching.length) {
      const latest = matching.sort((a, b) => new Date(b.time) - new Date(a.time))[0];
      const message = await (await fetch(`${mailOrigin}/email/${latest.id}`)).json();
      const code = message.text?.match(/\b\d{6}\b/); if (code) return code[0];
    }
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  throw new Error('Email code was not delivered.');
}
async function verify(page, email) {
  await page.getByLabel('Einmalcode').fill(await latestCode(email));
  await page.getByRole('button', { name: 'Anmelden', exact: true }).click();
  await page.waitForURL(origin + '/');
}
async function signIn(page, email) {
  await page.goto(`${origin}/Auth/Login`);
  await page.getByLabel('E-Mail-Adresse', { exact: true }).fill(email);
  await page.getByRole('button', { name: 'Anmeldecode senden' }).click();
  await verify(page, email);
}
async function mutate(page, operation) {
  const oldVersion = await page.locator('#evaluation-workspace').getAttribute('data-version');
  await operation();
  await page.waitForFunction(old => document.querySelector('#evaluation-workspace')?.dataset.version !== old, oldVersion);
  await page.waitForFunction(() => !document.querySelector('.htmx-settling, .htmx-request'));
}
async function post(page, path, values) {
  const csrf = await page.locator('input[name="__RequestVerificationToken"]').first().inputValue();
  return browserRequest(page, path, { __RequestVerificationToken: csrf, ...values });
}
async function browserRequest(page, path, form) {
  const result = await page.evaluate(async ({ url, form }) => {
    const response = await fetch(url, { method: form ? 'POST' : 'GET', body: form ? new URLSearchParams(form) : undefined, credentials: 'same-origin', redirect: 'manual' });
    return { status: response.status, text: await response.text() };
  }, { url: `${origin}${path}`, form });
  return { status: () => result.status, text: async () => result.text };
}

try {
  const migration = runApplication(true);
  let migrationLog = ''; migration.stdout.on('data', c => { migrationLog += c; }); migration.stderr.on('data', c => { migrationLog += c; });
  const [migrationCode] = await once(migration, 'exit'); assert.equal(migrationCode, 0, migrationLog);
  await startServer();
  if (httpsMode) {
    const key = join(keys, 'test-tls.key'); const cert = join(keys, 'test-tls.crt');
    execFileSync('openssl', ['req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1', '-keyout', key, '-out', cert,
      '-subj', '/CN=steervo-browser.test', '-addext', 'subjectAltName=DNS:steervo-browser.test,DNS:other.steervo-browser.test'], { stdio: 'pipe' });
    httpsProxy = createHttpsServer({ key: await readFile(key), cert: await readFile(cert) }, (request, response) => {
      const upstream = proxyRequest(`${listenOrigin}${request.url}`, { method: request.method, headers: { ...request.headers, 'x-forwarded-proto': 'https' } }, result => {
        response.writeHead(result.statusCode, result.headers); result.pipe(response);
      });
      upstream.on('error', () => { response.writeHead(502); response.end(); });
      request.pipe(upstream);
    });
    await new Promise(resolve => httpsProxy.listen(Number(new URL(origin).port), '127.0.0.1', resolve));
  }
  browser = await chromium.launch({ headless: true, executablePath: process.env.PLAYWRIGHT_EXECUTABLE_PATH || undefined,
    args: httpsMode ? ['--host-resolver-rules=MAP steervo-browser.test 127.0.0.1, MAP *.steervo-browser.test 127.0.0.1', '--no-proxy-server'] : [] });
  const owner = await newUser(); const page = owner.page;
  const stamp = Date.now(); const ownerEmail = `owner-${stamp}@example.test`; const readerEmail = `reader-${stamp}@example.test`;
  await page.goto(origin);
  await page.getByLabel('Einrichtungsschlüssel').fill(token);
  await page.getByLabel('Name', { exact: true }).fill('Test Owner');
  await page.getByLabel('E-Mail-Adresse', { exact: true }).fill(ownerEmail);
  const setupAt = Date.now();
  await page.getByRole('button', { name: 'E-Mail-Adresse bestätigen' }).click();
  await verify(page, ownerEmail);
  assert.equal(await page.locator('.case-card').count(), 0);
  assert.equal((await browserRequest(page, '/Auth/Setup')).status(), 404);
  assert.equal((await browserRequest(page, '/Identity/Account/Register')).status(), 404);
  record('Single-use setup, verified email and empty workspace');
  await page.screenshot({ path: join(output, '01-empty-desktop.png'), fullPage: true });

  const creation = page.getByRole('dialog', { name: 'Neue Evaluation', exact: true });
  const firstEvaluation = page.getByRole('button', { name: 'Erste Evaluation anlegen' });
  assert.equal(await creation.isVisible(), false);
  assert.equal(await page.getByRole('button', { name: 'Neue Evaluation', exact: true }).count(), 0);
  await firstEvaluation.click();
  assert.equal(await creation.isVisible(), true);
  assert.equal(await creation.getByLabel('Name der Evaluation').evaluate(input => input === document.activeElement), true);
  await page.screenshot({ path: join(output, '05-create-desktop.png'), fullPage: true });
  await page.keyboard.press('Escape');
  assert.equal(await creation.isVisible(), false);
  assert.equal(await firstEvaluation.evaluate(button => button === document.activeElement), true);
  await firstEvaluation.click();
  await creation.getByLabel('Name der Evaluation').fill('   ');
  await creation.getByLabel('Worum geht es?').fill('Ein unabhängiger Browser-Test.');
  await creation.getByRole('button', { name: 'Evaluation anlegen', exact: false }).click();
  await creation.locator('.validation-summary-errors').waitFor();
  assert.equal(await creation.getByLabel('Worum geht es?').inputValue(), 'Ein unabhängiger Browser-Test.');
  await page.setViewportSize({ width: 390, height: 844 });
  const bounds = await creation.boundingBox();
  assert.ok(bounds.x >= 0 && bounds.y >= 0 && bounds.x + bounds.width <= 390 && bounds.y + bounds.height <= 844);
  for (let index = 0; index < 7; index++) {
    await page.keyboard.press('Tab');
    // Native dialogs allow focus into browser chrome, but keep background controls inert.
    assert.equal(await page.evaluate(() => document.activeElement === document.body || !!document.activeElement?.closest('dialog')), true);
  }
  await page.screenshot({ path: join(output, '06-create-mobile.png'), fullPage: true });
  await creation.getByRole('button', { name: 'Abbrechen', exact: true }).click();
  await page.getByRole('button', { name: 'Weiterbearbeiten', exact: true }).click();
  assert.equal(await creation.getByLabel('Worum geht es?').inputValue(), 'Ein unabhängiger Browser-Test.');
  await page.setViewportSize({ width: 1440, height: 1050 });
  await page.getByLabel('Name der Evaluation').fill('Meine Testentscheidung');
  await page.getByLabel('Worum geht es?').fill('Ein unabhängiger Browser-Test.');
  await creation.getByRole('button', { name: 'Evaluation anlegen' }).click();
  await page.waitForURL(/\/evaluations\//);
  const casePath = new URL(page.url()).pathname;
  await page.goto(origin);
  await page.getByRole('button', { name: /Neue Evaluation/ }).click();
  assert.equal(await creation.isVisible(), true);
  await creation.getByRole('button', { name: 'Schliessen', exact: true }).click();
  assert.equal(await page.locator('.case-card').count(), 1);
  await page.goto(`${origin}${casePath}`);
  record('Evaluation creation opens a focused modal, preserves invalid drafts and works on desktop/mobile');
  const groupEditor = page.locator('#new-group-editor');
  for (const name of ['Alltag', 'Komfort']) {
    await page.locator('#add-group').click();
    await groupEditor.getByLabel('Gruppenname').fill(name);
    await mutate(page, () => groupEditor.getByRole('button', { name: 'Gruppe hinzufügen' }).click());
  }
  await page.locator('#add-criterion').click();
  const add = page.locator('form').filter({ has: page.locator('input[name="action"][value="add-criterion"]') });
  await add.getByLabel('Bezeichnung').fill('   ');
  const invalidSubmission = page.waitForResponse(response => new URL(response.url()).pathname === casePath && response.request().method() === 'POST');
  await add.getByRole('button', { name: 'Kriterium hinzufügen' }).click();
  const invalidResponse = await invalidSubmission;
  assert.equal(invalidResponse.status(), 422);
  assert.equal(invalidResponse.request().headers()['hx-request'], 'true');
  await page.locator('#new-criterion-editor .editor-error').waitFor();
  await page.waitForFunction(() => document.querySelector('#name-new')?.value === '   ');
  await add.getByLabel('Bezeichnung').fill('Gute Sicht');
  await add.getByLabel('Gruppe', { exact: true }).selectOption({ label: 'Alltag' });
  await mutate(page, () => add.getByRole('button', { name: 'Kriterium hinzufügen' }).click());
  await page.locator('#add-criterion').click();
  await add.getByLabel('Bezeichnung').fill('Kapazität');
  await add.getByLabel('Art des Kriteriums').selectOption('Measurement');
  await add.getByLabel('Schwellenwert').fill('12.5'); await add.getByLabel('Einheit', { exact: true }).fill('units');
  await add.getByLabel('Gruppe', { exact: true }).selectOption({ label: 'Alltag' });
  await mutate(page, () => add.getByRole('button', { name: 'Kriterium hinzufügen' }).click());
  await page.locator('#add-criterion').click();
  await add.getByLabel('Bezeichnung').fill('Sitzgefühl');
  await add.getByLabel('Art des Kriteriums').selectOption('Rating');
  await add.getByLabel('Priorität').selectOption('Preferred');
  await add.getByLabel('Gruppe', { exact: true }).selectOption({ label: 'Komfort' });
  await mutate(page, () => add.getByRole('button', { name: 'Kriterium hinzufügen' }).click());
  assert.match(await page.locator('.criterion').filter({ has: page.getByRole('heading', { name: 'Kapazität', exact: true }) }).innerText(), /12,5/);
  const original = page.locator('.criterion').filter({ has: page.getByRole('heading', { name: 'Gute Sicht', exact: true }) });
  const criterionId = await original.getAttribute('data-criterion-id');
  await mutate(page, () => page.getByRole('button', { name: 'Kriterium Kapazität nach oben', exact: true }).click());
  await mutate(page, () => page.getByRole('button', { name: 'Gruppe Komfort nach oben', exact: true }).click());
  await original.locator('[data-editor-open]').click();
  const edit = original.locator('form.edit-fields');
  await edit.getByLabel('Gruppe', { exact: true }).selectOption({ label: 'Komfort' });
  await mutate(page, () => edit.getByRole('button', { name: 'Speichern', exact: true }).click());
  assert.equal(await original.getAttribute('data-criterion-id'), criterionId);
  const group = page.locator('.criterion-group').filter({ has: page.getByRole('heading', { name: 'Komfort', exact: true }) });
  await group.locator('.group-heading [data-editor-open]').click();
  await group.getByLabel('Gruppenname').fill('Wohlfühlen');
  await mutate(page, () => group.getByRole('button', { name: 'Speichern', exact: true }).click());
  record('Three criterion types, decimal threshold, group/criterion ordering and identity-preserving reassignment');

  await checkSorting({ page, owner, origin, output, post, mutate, record });

  const staleVersion = await page.locator('#evaluation-workspace').getAttribute('data-version');
  await mutate(page, () => page.getByRole('button', { name: 'Gruppe Alltag nach oben', exact: true }).click());
  const conflict = await post(page, casePath, { action: 'add-group', name: 'Stale write', version: staleVersion });
  assert.equal(conflict.status(), 409); assert.ok((await conflict.text()).includes("inzwischen ge"), "Conflict response must explain the stale version.");
  const csrfRejected = await browserRequest(page, casePath, { action: 'add-group', name: 'No token', version: '1' });
  assert.equal(csrfRejected.status(), 400);
  record('Stale writes rejected and antiforgery required');
  await page.screenshot({ path: join(output, '02-criteria-desktop.png'), fullPage: true });

  await page.getByRole('link', { name: 'Einladungen', exact: true }).click();
  await page.getByLabel('Name', { exact: true }).fill('Test Reader'); await page.getByLabel('E-Mail-Adresse', { exact: true }).fill(readerEmail);
  await page.getByRole('button', { name: 'Einladung senden' }).click();
  const reader = await newUser(); await signIn(reader.page, readerEmail);
  assert.equal(await reader.page.locator('.case-card').count(), 0);
  assert.equal((await browserRequest(reader.page, casePath)).status(), 403);
  assert.equal((await browserRequest(reader.page, '/Account/Invitations')).status(), 403);
  assert.equal((await post(reader.page, '/Account/Invitations', { Name: 'Blocked', Email: 'blocked@example.test' })).status(), 403);
  record('Invitations do not share data; invited accounts cannot invite');

  await page.goto(`${origin}${casePath}`);
  await page.getByRole('link', { name: 'Freigaben', exact: true }).click();
  await page.locator('#add-share').click();
  const share = page.locator('.share-form'); await share.getByLabel('E-Mail-Adresse').fill(readerEmail);
  await mutate(page, () => share.getByRole('button', { name: 'Zugriff freigeben' }).click());
  await reader.page.goto(`${origin}${casePath}`);
  assert.equal(await reader.page.locator('input[value="add-criterion"]').count(), 0);
  assert.equal(await reader.page.locator('[data-drag-handle]').count(), 0);
  let version = await reader.page.locator('#evaluation-workspace').getAttribute('data-version');
  for (const action of ['add-group', 'delete', 'share', 'place-group', 'place-criterion']) assert.equal((await post(reader.page, casePath, { action, name: 'Blocked', email: ownerEmail, access: 'Editor', version })).status(), 403);
  await page.locator('#add-share').click();
  await share.getByLabel('E-Mail-Adresse').fill(readerEmail); await share.getByLabel('Berechtigung').selectOption('Editor');
  await mutate(page, () => share.getByRole('button', { name: 'Zugriff freigeben' }).click());
  await reader.page.reload(); version = await reader.page.locator('#evaluation-workspace').getAttribute('data-version');
  assert.equal((await post(reader.page, casePath, { action: 'add-group', name: 'Gemeinsam', version })).status(), 200);
  await reader.page.reload(); version = await reader.page.locator('#evaluation-workspace').getAttribute('data-version');
  for (const action of ['delete', 'share']) assert.equal((await post(reader.page, casePath, { action, email: ownerEmail, access: 'Reader', version })).status(), 403);
  record('Reader/editor restrictions cover direct requests; editor can change content');

  await page.reload();
  await page.setViewportSize({ width: 390, height: 844 });
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: join(output, '03-criteria-mobile.png'), fullPage: true });
  await page.setViewportSize({ width: 1440, height: 1050 });
  record('Responsive layout at desktop and 390px mobile width');

  await stopServer(); await startServer();
  await checkEvaluations({ page, reader, readerEmail, origin, output, post, browserRequest, mutate, record });

  // Run the candidate journey in a fresh request-rate window while retaining data and sessions.
  await stopServer(); await startServer();
  await page.goto(`${origin}${casePath}`);
  const candidateChecks = await checkCandidates({ page, reader, origin, output, post, browserRequest, record });
  await stopServer(); await startServer();
  const offerChecks = await checkOffers({ page, reader, readerEmail, origin, output, post, browserRequest, record });
  await stopServer(); await startServer();
  await checkDocuments({ page, reader, readerEmail, origin, output, post, browserRequest, record, restart: async () => { await stopServer(); await startServer(); } });
  await stopServer(); await startServer();
  await checkCosts({ page, reader, readerEmail, origin, output, post, browserRequest, record, restart: async () => { await stopServer(); await startServer(); } });
  await stopServer(); await startServer();
  await checkScenarios({ page, reader, readerEmail, origin, output, post, browserRequest, record, restart: async () => { await stopServer(); await startServer(); } });
  await stopServer(); await startServer();

  if (httpsMode) {
    for (const [address, expected] of [
      ['https://other.steervo-browser.test:5190', 'Passkeys sind für'],
      ['http://steervo-browser.test:5189', 'Passkeys benötigen eine sichere HTTPS-Verbindung']
    ]) {
      const context = await browser.newContext({ ignoreHTTPSErrors: true });
      await context.addCookies((await owner.context.cookies()).map(cookie => ({ ...cookie, domain: new URL(address).hostname, secure: address.startsWith('https:') })));
      const alternate = await context.newPage();
      let optionsRequests = 0;
      alternate.on('request', request => { if (request.url().includes('handler=Options')) optionsRequests++; });
      await alternate.goto(`${address}/Account`);
      await alternate.getByLabel('Name des Passkeys').fill('Wrong origin');
      await alternate.getByRole('button', { name: 'Passkey hinzufügen', exact: true }).click();
      const notice = alternate.locator('[data-passkey-error]');
      await notice.filter({ hasText: expected }).waitFor();
      assert.ok((await notice.innerText()).includes(origin));
      assert.equal(optionsRequests, 0);
      await context.close();
    }
    record('Passkey enrollment explains wrong HTTPS origins and insecure HTTP before contacting an authenticator');
  }

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
  await cdp.send('WebAuthn.enable');
  const authenticator = await cdp.send('WebAuthn.addVirtualAuthenticator', { options: { protocol: 'ctap2', transport: 'internal', hasResidentKey: true, hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true } });
  await page.goto(`${origin}/Account`);
  await page.getByLabel('Name des Passkeys').fill('Testgerät');
  await page.getByRole('button', { name: 'Passkey hinzufügen', exact: true }).click();
  await page.getByRole('heading', { name: 'Testgerät', exact: true }).waitFor();
  await page.getByRole('button', { name: 'Abmelden', exact: true }).click();
  await page.getByRole('button', { name: 'Mit Passkey anmelden', exact: true }).click();
  await page.waitForURL(origin + '/');
  record(`WebAuthn enrollment and sign-in tolerate omitted extension results${httpsMode ? ' on a custom HTTPS domain' : ''}`);

  await stopServer();
  if (process.env.STEERVO_E2E_DATABASE_CONTAINER) execFileSync('docker', ['restart', process.env.STEERVO_E2E_DATABASE_CONTAINER], { stdio: 'pipe' });
  await startServer();
  await page.goto(`${origin}${casePath}`);
  assert.equal(await page.locator('.criterion').count(), 3);
  assert.equal(await original.getAttribute('data-criterion-id'), criterionId);
  const persistedCandidates = JSON.parse(await (await browserRequest(page, `${casePath}?handler=Export`)).text());
  const persistedOffers = JSON.parse(await (await browserRequest(page, `${offerChecks.casePath}?handler=Export`)).text());
  assert.equal(persistedOffers.currency, 'EUR');
  assert.equal(persistedOffers.offers.find(offer => offer.id === offerChecks.firstId).revisions.length, 4);
  assert.equal(persistedCandidates.candidates.length, 8);
  assert.equal(persistedCandidates.candidates.find(item => item.id === candidateChecks.firstId).specifications.kerbWeightKg, 2100);
  assert.equal(persistedCandidates.assessments.find(item => item.candidateId === candidateChecks.firstId && item.criterionId === candidateChecks.rating).ratingValue, 2);
  record(`${process.env.STEERVO_E2E_DATABASE_CONTAINER ? 'Application and database' : 'Application'} restart preserves session keys, cases, criteria, ordering and shares`);

  await page.goto(`${origin}/Account`); await page.locator('.passkey-row summary').click();
  await page.locator('.passkey-row').getByLabel('Name', { exact: true }).fill('Umbenanntes Testgerät');
  await page.getByRole('button', { name: 'Umbenennen', exact: true }).click();
  await page.getByRole('heading', { name: 'Umbenanntes Testgerät', exact: true }).waitFor();
  await page.locator('.passkey-row summary').click();
  await page.getByRole('button', { name: 'Passkey entfernen', exact: true }).click();
  await page.getByRole('heading', { name: 'Noch kein Passkey hinterlegt.', exact: true }).waitFor();
  await cdp.send('WebAuthn.removeVirtualAuthenticator', authenticator);
  await page.getByRole('button', { name: 'Abmelden', exact: true }).click();
  const cooldownRemaining = 61_000 - (Date.now() - setupAt);
  if (cooldownRemaining > 0) await new Promise(resolve => setTimeout(resolve, cooldownRemaining));
  await signIn(page, ownerEmail);
  record('Passkey rename/removal and email recovery');

  await page.goto(`${origin}${casePath}`);
  await page.getByRole('link', { name: 'Freigaben', exact: true }).click();
  await mutate(page, () => page.locator('.share-row').getByRole('button', { name: 'Entziehen', exact: true }).click());
  assert.equal((await browserRequest(reader.page, casePath)).status(), 403);
  assert.equal((await browserRequest(reader.page, `${casePath}?handler=Export`)).status(), 403);
  record('Revocation takes effect for an existing authenticated session');

  const downloadReady = page.waitForEvent('download');
  await page.getByRole('link', { name: /Exportieren/ }).click();
  const download = await downloadReady;
  const exportFile = join(output, 'evaluation.json'); await download.saveAs(exportFile);
  await page.goto(origin);
  await page.getByRole('button', { name: 'Evaluation importieren', exact: true }).click();
  await page.getByLabel('Exportdatei (JSON oder ZIP)').setInputFiles({ name: 'invalid.json', mimeType: 'application/json', buffer: Buffer.from('{invalid') });
  await page.getByRole('button', { name: 'Als neue Evaluation importieren', exact: true }).click();
  await page.locator('#import-evaluation-editor .editor-error:not([hidden])').waitFor();
  assert.equal(await creation.isVisible(), false);
  assert.equal(await page.locator('#upload').evaluate(input => input.files[0].name), 'invalid.json');
  await page.keyboard.press('Escape');
  await page.getByRole('button', { name: 'Weiterbearbeiten', exact: true }).click();
  assert.equal(await page.locator('#upload').evaluate(input => input.files[0].name), 'invalid.json');
  await page.getByLabel('Exportdatei (JSON oder ZIP)').setInputFiles(exportFile);
  await page.getByRole('button', { name: 'Als neue Evaluation importieren', exact: true }).click();
  await page.waitForURL(/\/evaluations\//);
  assert.notEqual(new URL(page.url()).pathname, casePath);
  assert.equal(await page.locator('.criterion').count(), 3);
  assert.equal(await page.locator('.share-row').count(), 0);
  const importedCandidates = JSON.parse(await (await browserRequest(page, `${new URL(page.url()).pathname}?handler=Export`)).text());
  assert.equal(importedCandidates.candidates.length, 8);
  assert.equal(importedCandidates.candidates.find(item => item.model === "Nord").specifications.accelerationSeconds, 4.5);
  assert.ok(importedCandidates.assessments.length >= 6);
  assert.ok(importedCandidates.candidates.every(item => item.id !== candidateChecks.firstId));
  record('Authorized JSON export and import as an independent private evaluation');
  assert.deepEqual(errors, []);
  await writeFile(join(output, 'results.json'), JSON.stringify({ checks, browserErrors: errors }, null, 2));
  console.log(`Completed ${checks.length} browser checks.`);
} catch (error) {
  await writeFile(join(output, 'server.log'), log);
  if (errors.length) console.error('Browser errors:', errors);
  throw error;
} finally {
  if (browser) await browser.close();
  if (httpsProxy) { httpsProxy.closeAllConnections(); await new Promise(resolve => httpsProxy.close(resolve)); }
  await stopServer();
  if (containerMode) {
    for (const name of [containerName, `${containerName}-migrate`]) {
      try { execFileSync('docker', ['rm', '-f', name], { stdio: 'ignore' }); } catch {}
    }
    try { execFileSync('docker', ['volume', 'rm', keyVolume, documentVolume], { stdio: 'ignore' }); } catch {}
  }
  await rm(keys, { recursive: true, force: true });
}
