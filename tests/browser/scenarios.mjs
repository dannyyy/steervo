import { checkScenarioWorkflows } from "./financial-workflows.mjs";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { join } from "node:path";

const MOBILE_VIEWPORT = { width: 390, height: 844 };
const DESKTOP_VIEWPORT = { width: 1440, height: 1050 };

const EVALUATION_URL = /\/evaluations\//;

/**
 * Walks through the cost comparison: four procurement scenarios with hand-checked results,
 * comparison, offer revisions, access rights, copies and both import formats.
 * The steps build on each other and must run in this order.
 */
export async function checkScenarios({
  page,
  reader,
  readerEmail,
  origin,
  output,
  post,
  browserRequest,
  record,
  restart,
}) {
  const originalPath = new URL(page.url()).pathname;

  const casePath = await createEvaluation(page, origin);
  const context = {
    page,
    reader,
    readerEmail,
    origin,
    output,
    post,
    browserRequest,
    record,
    restart,
    casePath,
    scenariosPath: `${casePath}/scenarios`,
  };

  const candidateId = await addCandidate(context);
  const offerId = await createOfferWithFreeServiceBenefit(context, candidateId);
  const costId = await planCostsWithParkingSpace(context);
  await setAnnualReturnToZero(context);

  const { cashId, leaseId } = await checkProcurementCalculations(context, costId);
  await checkComparisonAndOfferRevisions(context, { cashId, leaseId, offerId });
  await checkAccessAndConcurrency(context, cashId);
  await checkCopyJsonImportAndDeletion(context, { leaseId, costId });
  await checkZipRoundTrip(context, offerId);

  await restart();
  await checkScenarioWorkflows({
    page,
    reader,
    readerEmail,
    origin,
    casePath,
    cash: cashId,
    lease: leaseId,
    cost: costId,
    post,
    record,
  });
  await page.goto(`${origin}${originalPath}`);
}

// ---------------------------------------------------------------------------
// Shared interaction helpers
// ---------------------------------------------------------------------------

/** Runs an operation that submits a form and waits for the full page load it triggers. */
async function saveAndWaitForLoad(target, operation) {
  await Promise.all([target.waitForNavigation({ waitUntil: "load" }), operation()]);
}

/** Opens the evaluation overview and reads the version needed for the next write. */
async function readCaseVersion({ page, origin, casePath }) {
  await page.goto(`${origin}${casePath}`);
  return page.locator("#evaluation-workspace").getAttribute("data-version");
}

async function exportCase({ page, browserRequest, casePath }) {
  const exportResponse = await browserRequest(page, `${casePath}?handler=Export`);
  return JSON.parse(await exportResponse.text());
}

/** The effective cost shown for the selected month; empty while the result is unknown. */
function effectiveCost(target) {
  return target.locator("[data-effective-cost]").first().getAttribute("data-effective-cost");
}

/** Starts listening for the form post of the page that is currently open. */
function waitForPostToCurrentUrl(page) {
  return page.waitForResponse(
    (response) => response.request().method() === "POST" && response.url() === page.url(),
  );
}

function lastPathSegment(page) {
  return new URL(page.url()).pathname.split("/").at(-1);
}

/** Creates a scenario for the first offer and returns its id. */
async function createScenario({ page, origin, scenariosPath }, name, mode, values) {
  await page.goto(`${origin}${scenariosPath}`);
  await page.getByRole("link", { name: "Szenario anlegen", exact: true }).click();
  await page.locator(".scenario-offer-option").first().click();
  await page.getByLabel("Name des Szenarios").fill(name);
  await page.getByLabel("Beschaffungsweg").selectOption(mode);

  for (const [field, value] of Object.entries(values)) {
    await page.locator(`#scenario-${field}`).fill(value);
  }

  await page
    .getByLabel("Ich habe Zusatzkosten und Vergünstigungen für dieses Szenario geprüft.")
    .check();
  await saveAndWaitForLoad(page, () => page.locator("#save-scenario").click());
  await page.locator("#scenario-detail").waitFor();

  return lastPathSegment(page);
}

/** Records the three residual values for an exact month of a scenario. */
async function saveResidualCheckpoint(
  { page, origin, scenariosPath },
  scenarioId,
  month,
  low,
  expected,
  high,
) {
  await page.goto(`${origin}${scenariosPath}/${scenarioId}?month=${month}`);
  await page.locator("#open-residual").click();
  await page.getByLabel("Pessimistisch (EUR)", { exact: true }).fill(low);
  await page.getByLabel("Erwartet (EUR)", { exact: true }).fill(expected);
  await page.getByLabel("Optimistisch (EUR)", { exact: true }).fill(high);
  await saveAndWaitForLoad(page, () =>
    page.getByRole("button", { name: "Restwert-Stichtag speichern" }).click(),
  );
  await page.locator("#scenario-detail").waitFor();
}

/** Imports an export file from the start page and returns the path of the new evaluation. */
async function importEvaluationFile(page, origin, file) {
  await page.goto(origin);
  await page.getByRole("button", { name: "Evaluation importieren", exact: true }).click();
  await page.getByLabel("Exportdatei (JSON oder ZIP)").setInputFiles(file);
  await page.getByRole("button", { name: "Als neue Evaluation importieren", exact: true }).click();
  await page.waitForURL(EVALUATION_URL);

  return new URL(page.url()).pathname;
}

// ---------------------------------------------------------------------------
// Preparation: evaluation, car, offer, cost plan
// ---------------------------------------------------------------------------

async function createEvaluation(page, origin) {
  await page.goto(origin);
  await page.locator('[data-dialog-open="new-evaluation"]').first().click();

  const creationDialog = page.getByRole("dialog", { name: "Neue Evaluation", exact: true });
  await creationDialog.getByLabel("Name der Evaluation").fill("Szenarienprüfung");
  await page.locator("#new-region-country").selectOption("CH");
  await page.locator("#new-region-currency").fill("EUR");
  await creationDialog.getByRole("button", { name: "Evaluation anlegen", exact: false }).click();
  await page.waitForURL(EVALUATION_URL);

  return new URL(page.url()).pathname;
}

async function addCandidate(context) {
  const { page, post, casePath } = context;

  const caseVersion = await readCaseVersion(context);
  const addResponse = await post(page, `${casePath}/candidates`, {
    action: "add-candidate",
    "Input.Model": "Rechenauto",
    version: caseVersion,
  });
  assert.equal(addResponse.status(), 0);

  return (await exportCase(context)).candidates[0].id;
}

async function createOfferWithFreeServiceBenefit(context, candidateId) {
  const { page, origin, casePath } = context;

  await page.goto(`${origin}${casePath}/offers/edit?candidateId=${candidateId}`);
  assert.equal(await page.locator("#offer-currency").inputValue(), "EUR");
  await page.locator("#offer-Name").fill("Rechenofferte");

  // A free benefit with a duration: the cost comparison names it as covered (no service cost exists here, so sums stay as they are).
  await page.getByRole("button", { name: /Position hinzufügen/ }).click();
  const servicePackLine = page.locator("[data-offer-line]").last();
  await servicePackLine
    .getByLabel("Bezeichnung der Position", { exact: true })
    .fill("Servicepaket");
  await servicePackLine.getByLabel("Art", { exact: true }).selectOption("Benefit");
  await servicePackLine.getByLabel("Preisangabe", { exact: true }).selectOption("Included");
  await servicePackLine.getByLabel("Wirkung", { exact: true }).selectOption("CostSaving");
  await servicePackLine.getByLabel("Kostenbereich").selectOption("Service");
  await servicePackLine.getByLabel("Dauer in Monaten").fill("6");

  await page.getByRole("button", { name: /Offerte anlegen/ }).click();
  await page.locator("#offer-workspace").waitFor();

  return (await exportCase(context)).offers[0].id;
}

/** Plans twelve months and adds one monthly cost of 25; returns the id of that cost. */
async function planCostsWithParkingSpace(context) {
  const { page, origin, casePath } = context;

  await page.goto(`${origin}${casePath}/costs`);
  await page.getByLabel("Beginn der Planung").fill("2027-10");
  await page.getByLabel("Dauer in Monaten").fill("12");
  await saveAndWaitForLoad(page, () =>
    page.getByRole("button", { name: "Zeitraum speichern" }).click(),
  );
  await page.locator("#cost-workspace").waitFor();

  await page.getByRole("link", { name: /Kostenposition hinzufügen/ }).click();
  await page.getByLabel("Bezeichnung der Kostenposition").fill("Parkplatz");
  await page.getByLabel("Betrag je Zahlung", { exact: false }).fill("25");
  await saveAndWaitForLoad(page, () =>
    page.getByRole("button", { name: "Speichern", exact: true }).click(),
  );
  await page.locator("#cost-workspace").waitFor();

  return (await exportCase(context)).costs[0].id;
}

/** Without an assumed investment return the expected figures can be checked by hand. */
async function setAnnualReturnToZero({ page }) {
  await page.getByRole("link", { name: "Auswertungen", exact: true }).click();
  await page.getByRole("link", { name: "Kostenvergleich", exact: true }).click();
  await page.getByLabel("Angenommene Anlagerendite pro Jahr (%)").fill("0");
  await saveAndWaitForLoad(page, () =>
    page.getByRole("button", { name: "Speichern", exact: true }).click(),
  );
  await page.locator("#scenario-workspace").waitFor();
}

// ---------------------------------------------------------------------------
// Purchase, leasing return, leasing buyout and credit
// ---------------------------------------------------------------------------

async function checkProcurementCalculations(context, costId) {
  const { page, origin, scenariosPath, record } = context;

  const cashId = await createScenario(context, "Barkauf", "Purchase", {
    PurchasePrice: "1000",
    InitialFees: "20",
    AnnualKilometres: "1000",
  });
  assert.match(
    await page.locator("[data-covered-costs]").innerText(),
    /Servicepaket: Service, 6 Monate/,
  );
  assert.equal(
    await page.locator("[data-result-complete]").getAttribute("data-result-complete"),
    "false",
  );
  await saveResidualCheckpoint(context, cashId, 12, "300", "400", "500");
  assert.equal(await effectiveCost(page), "920");

  // A residual value applies to its exact month only; month 6 has none yet.
  await page.goto(`${origin}${scenariosPath}/${cashId}?month=6`);
  assert.equal(await effectiveCost(page), "");
  await page.getByText("Erwarteter Fahrzeugwert genau für Monat 6.", { exact: true }).waitFor();

  const leaseId = await createScenario(context, "Leasing", "LeaseReturn", {
    PurchasePrice: "1000",
    InitialFees: "10",
    DownPayment: "40",
    MonthlyPayment: "50",
    TermMonths: "12",
    AnnualKilometres: "1100",
    IncludedKilometresPerYear: "1000",
    ExcessKilometrePrice: "0.2",
  });
  assert.equal(await effectiveCost(page), "970.0");

  await page.locator("#open-adjustment").click();
  await page.getByLabel("Kostenposition", { exact: true }).selectOption(costId);
  await page.getByLabel("Abweichender Betrag je Zahlung (EUR)").fill("0");
  await page.getByLabel("Letzter Monat (einschliesslich)").fill("6");
  await page.getByLabel("Grundlage / Vergünstigung").fill("Sechs Monate Parkplatz inklusive");
  await saveAndWaitForLoad(page, () =>
    page.getByRole("button", { name: "Kostenanpassung speichern" }).click(),
  );
  await page.locator("#scenario-detail").waitFor();
  assert.equal(Number(await effectiveCost(page)), 820);

  const buyoutId = await createScenario(context, "Übernahme", "LeaseBuyout", {
    PurchasePrice: "1000",
    InitialFees: "10",
    DownPayment: "40",
    MonthlyPayment: "50",
    TermMonths: "12",
    FinalPayment: "400",
  });
  await saveResidualCheckpoint(context, buyoutId, 12, "350", "400", "450");
  assert.equal(Number(await effectiveCost(page)), 950);

  const creditId = await createScenario(context, "Kredit", "Financing", {
    PurchasePrice: "1000",
    InitialFees: "0",
    LoanAmount: "1000",
    LoanInterestPercent: "12",
    MonthlyPayment: "500",
    TermMonths: "2",
    FinalPayment: "15.10",
  });
  await saveResidualCheckpoint(context, creditId, 2, "700", "750", "800");
  assert.equal(Number(await effectiveCost(page)), 315.1);

  record(
    "Cash, leasing return, buyout and credit scenarios calculate payments, exact residual checkpoints, mileage and temporary benefits",
  );

  return { cashId, leaseId };
}

// ---------------------------------------------------------------------------
// Comparison, mobile screens, validation and offer revisions
// ---------------------------------------------------------------------------

async function checkComparisonAndOfferRevisions(context, { cashId, leaseId, offerId }) {
  const { page, origin, output, post, record, restart, casePath, scenariosPath } = context;

  await page.goto(`${origin}${scenariosPath}/compare?leftId=${cashId}&rightId=${leaseId}&month=12`);
  assert.equal(
    Number(await page.locator("[data-cost-difference]").getAttribute("data-cost-difference")),
    100,
  );
  assert.match(await page.locator(".scenario-threshold").textContent(), /500,00 EUR/);
  await page.screenshot({
    path: join(output, "24-scenario-comparison-desktop.png"),
    fullPage: true,
  });

  await page.setViewportSize(MOBILE_VIEWPORT);
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  await page.screenshot({
    path: join(output, "25-scenario-comparison-mobile.png"),
    fullPage: true,
  });

  await page.goto(`${origin}${scenariosPath}/edit/${cashId}`);
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  await page.screenshot({ path: join(output, "26-scenario-edit-mobile.png"), fullPage: true });

  // Browser validation is switched off so the server has to reject the third decimal place.
  await page.locator("#scenario-PurchasePrice").fill("1000.001");
  await page.locator("[data-scenario-form]").evaluate((form) => {
    form.noValidate = true;
  });
  const invalidSave = waitForPostToCurrentUrl(page);
  await page.locator("#save-scenario").click();
  assert.equal((await invalidSave).status(), 422);
  assert.equal(await page.locator("#scenario-PurchasePrice").inputValue(), "1000.001");
  await page.setViewportSize(DESKTOP_VIEWPORT);

  await page.goto(`${origin}${scenariosPath}/${cashId}`);
  await page.getByRole("link", { name: "Jahre", exact: true }).click();
  assert.equal(await page.locator("[data-payment-month]").count(), 2);

  // A changed offer creates revision 2; the scenario keeps revision 1 until it is switched explicitly.
  const caseVersion = await readCaseVersion(context);
  const offerUpdate = await post(page, `${casePath}/offers/edit/${offerId}`, {
    Version: caseVersion,
    "Input.Name": "Neue Rechenofferte",
    "Input.Kind": "Purchase",
    "Input.Commitment": "NonBinding",
    "Input.StatedTotal": "9999",
  });
  assert.equal(offerUpdate.status(), 0);

  await page.goto(`${origin}${scenariosPath}/${cashId}`);
  await page.getByText("Offertversion 2 ist verfügbar.", { exact: false }).waitFor();
  assert.equal(Number(await effectiveCost(page)), 920);

  await page.getByRole("link", { name: "Szenario bearbeiten" }).click();
  await page.getByLabel("Verwendete Offertversion").selectOption("2");
  await saveAndWaitForLoad(page, () => page.locator("#save-scenario").click());
  await page.locator("#scenario-detail").waitFor();
  assert.equal(Number(await effectiveCost(page)), 920);

  const pinned = await exportCase(context);
  assert.equal(pinned.scenarios.find((scenario) => scenario.id === cashId).input.revisionNumber, 2);
  assert.equal(
    pinned.scenarios.find((scenario) => scenario.id === leaseId).input.revisionNumber,
    1,
  );

  await restart();
  await page.goto(`${origin}${scenariosPath}/${leaseId}`);
  assert.equal(Number(await effectiveCost(page)), 820);

  record(
    "Scenario comparison, residual threshold, calendar years, mobile screens, validation and explicit source-version updates survive restart",
  );
}

// ---------------------------------------------------------------------------
// Reader and editor access, revocation, CSRF and concurrent edits
// ---------------------------------------------------------------------------

async function checkAccessAndConcurrency(context, cashId) {
  const { page, reader, readerEmail, origin, post, browserRequest, record } = context;
  const { casePath, scenariosPath } = context;

  let caseVersion = await readCaseVersion(context);
  const readerShare = await post(page, casePath, {
    action: "share",
    email: readerEmail,
    access: "Reader",
    version: caseVersion,
  });
  assert.equal(readerShare.status(), 200);

  // A reader sees the result but can neither open the editor nor write.
  await reader.page.goto(`${origin}${scenariosPath}/${cashId}`);
  assert.equal(Number(await effectiveCost(reader.page)), 920);
  assert.equal(await reader.page.getByRole("link", { name: "Szenario bearbeiten" }).count(), 0);
  assert.equal(
    (await browserRequest(reader.page, `${scenariosPath}/edit/${cashId}`)).status(),
    403,
  );

  const readerVersion = await reader.page.locator("#scenario-detail").getAttribute("data-version");
  const deniedResidual = await post(reader.page, `${scenariosPath}/${cashId}`, {
    action: "residual",
    Version: readerVersion,
    "Residual.Month": "12",
    "Residual.Expected": "1",
  });
  assert.equal(deniedResidual.status(), 403);
  const deniedSettings = await post(reader.page, scenariosPath, {
    action: "settings",
    Version: readerVersion,
    AnnualReturn: "50",
  });
  assert.equal(deniedSettings.status(), 403);

  caseVersion = await readCaseVersion(context);
  const editorShare = await post(page, casePath, {
    action: "share",
    email: readerEmail,
    access: "Editor",
    version: caseVersion,
  });
  assert.equal(editorShare.status(), 200);

  // The owner starts a draft, the editor saves first: the owner's save must conflict and keep the draft.
  await page.goto(`${origin}${scenariosPath}/edit/${cashId}`);
  const staleVersion = await page.locator("[name=Version]").inputValue();
  await page.locator("#scenario-PurchasePrice").fill("1111");

  await reader.page.goto(`${origin}${scenariosPath}/edit/${cashId}`);
  await reader.page
    .getByLabel("Annahmen und Begründung", { exact: true })
    .fill("Reviewed by editor");
  await saveAndWaitForLoad(reader.page, () => reader.page.locator("#save-scenario").click());
  await reader.page.locator("#scenario-detail").waitFor();

  const conflictingSave = waitForPostToCurrentUrl(page);
  await page.locator("#save-scenario").click();
  assert.equal((await conflictingSave).status(), 409);
  assert.equal(await page.locator("#scenario-PurchasePrice").inputValue(), "1111");
  assert.equal(await page.locator("[name=Version]").inputValue(), staleVersion);

  // A direct request without the antiforgery token must be rejected.
  const withoutToken = await browserRequest(page, scenariosPath, {
    action: "settings",
    Version: staleVersion,
    AnnualReturn: "20",
  });
  assert.equal(withoutToken.status(), 400);

  caseVersion = await readCaseVersion(context);
  const revocation = await post(page, casePath, {
    action: "revoke",
    userId: await page.locator(".share-row input[name=userId]").inputValue(),
    version: caseVersion,
  });
  assert.equal(revocation.status(), 200);
  assert.equal((await browserRequest(reader.page, `${scenariosPath}/${cashId}`)).status(), 403);

  record(
    "Scenario reads and writes enforce reader/editor access, immediate revocation, CSRF and draft-preserving concurrency",
  );
}

// ---------------------------------------------------------------------------
// Independent copies, JSON import and deletion
// ---------------------------------------------------------------------------

async function checkCopyJsonImportAndDeletion(context, { leaseId, costId }) {
  const { page, origin, browserRequest, record, scenariosPath } = context;

  await page.goto(`${origin}${scenariosPath}/${leaseId}`);
  await page.locator("#open-scenario-copy").click();
  await page.getByLabel("Name der unabhängigen Kopie").fill("Leasingkopie");
  await saveAndWaitForLoad(page, () => page.locator("#save-scenario-copy").click());
  await page.waitForURL(
    (url) => url.pathname.startsWith(scenariosPath + "/") && !url.pathname.endsWith(leaseId),
  );
  const copiedScenarioId = lastPathSegment(page);
  assert.equal(Number(await effectiveCost(page)), 820);

  // Changing the adjustment of the copy must leave the original untouched.
  await page.goto(`${origin}${scenariosPath}/${copiedScenarioId}?costId=${costId}`);
  await page.getByLabel("Abweichender Betrag je Zahlung (EUR)").fill("5");
  await saveAndWaitForLoad(page, () =>
    page.getByRole("button", { name: "Kostenanpassung speichern" }).click(),
  );
  await page.locator("#scenario-detail").waitFor();
  assert.equal(Number(await effectiveCost(page)), 850);
  await page.goto(`${origin}${scenariosPath}/${leaseId}`);
  assert.equal(Number(await effectiveCost(page)), 820);

  const exported = await exportCase(context);
  assert.equal(exported.version, 9);
  assert.equal(exported.scenarios.length, 5);

  const importedPath = await importEvaluationFile(page, origin, {
    name: "scenarios.json",
    mimeType: "application/json",
    buffer: Buffer.from(JSON.stringify(exported)),
  });
  const importedExport = await browserRequest(page, `${importedPath}?handler=Export`);
  const imported = JSON.parse(await importedExport.text());
  assert.equal(imported.scenarios.length, 5);

  // The import receives new ids, and every reference points into the imported evaluation.
  assert.ok(
    imported.scenarios.every(
      (scenario) => !exported.scenarios.some((original) => original.id === scenario.id),
    ),
  );
  assert.ok(
    imported.scenarios.every((scenario) =>
      imported.offers.some((offer) => offer.id === scenario.input.offerId),
    ),
  );
  assert.ok(
    imported.scenarios
      .flatMap((scenario) => scenario.costAdjustments)
      .every((adjustment) => imported.costs.some((cost) => cost.id === adjustment.costId)),
  );

  await page.goto(`${origin}${scenariosPath}`);
  await page
    .locator(`[data-scenario-id="${copiedScenarioId}"]`)
    .getByRole("button", { name: "Szenario löschen" })
    .click();
  await saveAndWaitForLoad(page, () => page.locator("[data-confirm-accept]").click());
  await page.locator("#scenario-workspace").waitFor();
  assert.equal(await page.locator("[data-scenario-id]").count(), 4);

  record(
    "Scenario copies, JSON import and deletion preserve independent assumptions, residuals and mapped cost adjustments",
  );
}

// ---------------------------------------------------------------------------
// ZIP export and import together with an offer document
// ---------------------------------------------------------------------------

async function checkZipRoundTrip(context, offerId) {
  const { page, origin, record, casePath } = context;

  // With a stored document the export downloads as a ZIP archive.
  await page.goto(`${origin}${casePath}/offers/${offerId}`);
  await page.getByRole("link", { name: "Dokument hinzufügen", exact: true }).click();
  await page.getByLabel("Name des Dokuments").fill("Rechengrundlage");
  await page.getByLabel("Datei", { exact: true }).setInputFiles({
    name: "basis.txt",
    mimeType: "text/plain",
    buffer: Buffer.from("Independent calculation attachment"),
  });
  await page.getByRole("button", { name: "Datei hochladen" }).click();
  await page.locator("[data-document-id]").waitFor();

  const downloadEvent = page.waitForEvent("download");
  await page
    .locator(".workflow-nav")
    .getByRole("link", { name: "Grundlagen", exact: true })
    .click();
  await page
    .locator(".workspace-subnav")
    .getByRole("link", { name: "Allgemein", exact: true })
    .click();
  await page
    .locator("#evaluation-data")
    .getByRole("link", { name: "Exportieren", exact: true })
    .click();
  const downloaded = await downloadEvent;
  const zip = await readFile(await downloaded.path());
  assert.equal(zip.subarray(0, 2).toString(), "PK");

  const zippedCasePath = await importEvaluationFile(page, origin, {
    name: "scenarios.zip",
    mimeType: "application/zip",
    buffer: zip,
  });
  await page.goto(`${origin}${zippedCasePath}/scenarios`);
  assert.equal(await page.locator("[data-scenario-id]").count(), 4);
  await page.getByRole("link", { name: "Barkauf", exact: true }).click();
  assert.equal(Number(await effectiveCost(page)), 920);

  record("ZIP export and import carry scenarios and calculations together with offer documents");
}
