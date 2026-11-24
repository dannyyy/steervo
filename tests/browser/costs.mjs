import { checkCostWorkflows } from "./financial-workflows.mjs";
import assert from "node:assert/strict";
import { join } from "node:path";

const MOBILE_VIEWPORT = { width: 390, height: 844 };
const DESKTOP_VIEWPORT = { width: 1440, height: 1050 };

const EVALUATION_URL = /\/evaluations\//;

const ADD_COST_LINK = /Kostenposition hinzufügen/;
const PLAN_MONTHS_LABEL = "Dauer in Monaten";
const SAVE_PLAN_BUTTON = "Zeitraum speichern";
const AMOUNT_LABEL = "Betrag je Zahlung";

/**
 * Walks through cost planning: payment rhythms with hand-checked totals, plan changes,
 * access rights, copies, JSON import and deletion.
 * The steps build on each other and must run in this order.
 */
export async function checkCosts({
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
    costsPath: `${casePath}/costs`,
  };

  await checkPlanningBeforeAnyOffer(context);
  const candidateId = await addCandidateWithOffer(context);
  const sharedMonthlyCostId = await checkPaymentReconciliation(context, candidateId);
  await checkPlanChangesMobileAndRestart(context, candidateId, sharedMonthlyCostId);
  await checkAccessAndConcurrency(context, candidateId, sharedMonthlyCostId);
  const copiedCandidateId = await checkCopyJsonImportAndDeletion(context, candidateId);

  await restart();
  await checkCostWorkflows({
    page,
    reader,
    readerEmail,
    origin,
    casePath,
    candidateId: copiedCandidateId,
    costId: sharedMonthlyCostId,
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

/** Starts listening for the form post of the page that is currently open. */
function waitForPostToCurrentUrl(page) {
  return page.waitForResponse(
    (response) => response.request().method() === "POST" && response.url() === page.url(),
  );
}

async function savePlan(page) {
  await saveAndWaitForLoad(page, () =>
    page.getByRole("button", { name: SAVE_PLAN_BUTTON, exact: true }).click(),
  );
  await page.locator("#cost-workspace").waitFor();
}

/**
 * Adds a cost position through the editor and returns its id.
 * Without a candidate the cost applies to all cars; an inclusive item has no amount and direction.
 */
async function addCost(
  { page, origin, costsPath },
  { name, frequency = "Monthly", amount, start = "1", end, candidate, direction = "Expense" },
) {
  await page.goto(`${origin}${costsPath}`);
  await page.getByRole("link", { name: ADD_COST_LINK }).click();
  await page.getByLabel("Bezeichnung der Kostenposition").fill(name);
  await page.getByLabel("Gilt für", { exact: true }).selectOption(candidate || "");
  await page.getByLabel("Zahlungsrhythmus", { exact: true }).selectOption(frequency);

  if (frequency !== "Included") {
    await page.getByLabel("Art der Zahlung", { exact: true }).selectOption(direction);
    if (amount !== undefined) {
      await page.getByLabel(AMOUNT_LABEL, { exact: false }).fill(amount);
    }
  } else {
    assert.equal(await page.locator("#Input_Amount").isDisabled(), true);
    assert.equal(await page.locator("#Input_Direction").isDisabled(), true);
  }

  await page.getByLabel("Erster Monat", { exact: true }).fill(start);
  if (end) {
    await page.getByLabel("Letzter Monat", { exact: false }).fill(end);
  }

  await saveAndWaitForLoad(page, () =>
    page.getByRole("button", { name: "Speichern", exact: true }).click(),
  );
  await page.locator("#cost-workspace").waitFor();

  return page
    .locator("[data-cost-id]")
    .filter({ has: page.getByRole("heading", { name, exact: true }) })
    .getAttribute("data-cost-id");
}

// ---------------------------------------------------------------------------
// Preparation: evaluation, first plan, car and offer
// ---------------------------------------------------------------------------

async function createEvaluation(page, origin) {
  await page.goto(origin);
  await page.locator('[data-dialog-open="new-evaluation"]').first().click();

  const creationDialog = page.getByRole("dialog", { name: "Neue Evaluation", exact: true });
  await creationDialog.getByLabel("Name der Evaluation").fill("Kostenprüfung");
  await page.locator("#new-region-country").selectOption("CH");
  await page.locator("#new-region-currency").fill("EUR");
  await creationDialog.getByRole("button", { name: "Evaluation anlegen", exact: false }).click();
  await page.waitForURL(EVALUATION_URL);

  return new URL(page.url()).pathname;
}

async function checkPlanningBeforeAnyOffer({ page, record }) {
  await page.getByRole("link", { name: "Offerten & Kosten", exact: true }).click();
  await page.getByRole("link", { name: "Zusatzkosten", exact: true }).click();
  assert.equal(
    (await page.locator(".workflow-nav a.active").textContent()).trim(),
    "Offerten & Kosten",
  );

  await page.getByLabel("Beginn der Planung", { exact: true }).fill("2027-10");
  await page.getByLabel(PLAN_MONTHS_LABEL, { exact: true }).fill("15");
  await savePlan(page);
  assert.equal(await page.getByRole("link", { name: ADD_COST_LINK }).count(), 1);

  record("Evaluation currency allows cost planning before any offer exists");
}

async function addCandidateWithOffer(context) {
  const { page, origin, post, casePath } = context;

  await page.goto(`${origin}${casePath}/candidates`);
  const version = await page.locator("#candidate-workspace").getAttribute("data-version");
  const addResponse = await post(page, `${casePath}/candidates`, {
    action: "add-candidate",
    "Input.Model": "Planungsauto",
    version,
  });
  assert.equal(addResponse.status(), 0);
  const candidateId = (await exportCase(context)).candidates[0].id;

  await page.goto(`${origin}${casePath}/offers/edit?candidateId=${candidateId}`);
  assert.equal(await page.locator("#offer-currency").inputValue(), "EUR");
  await page.locator("#offer-Name").fill("Kostenbasis");
  await page.getByRole("button", { name: /Offerte anlegen/ }).click();
  await page.locator("#offer-workspace").waitFor();

  return candidateId;
}

// ---------------------------------------------------------------------------
// Payment rhythms, income, inclusive and unknown amounts
// ---------------------------------------------------------------------------

/** Returns the id of the monthly cost that applies to all cars; later checks edit it. */
async function checkPaymentReconciliation(context, candidateId) {
  const { page, output, record } = context;

  const sharedMonthlyCostId = await addCost(context, { name: "Parkplatz", amount: "25" });
  await addCost(context, { name: "Jahresbetrag", frequency: "Yearly", amount: "120", start: "2" });
  await addCost(context, {
    name: "Einmalbetrag",
    frequency: "Once",
    amount: "400",
    start: "3",
    candidate: candidateId,
  });
  await addCost(context, {
    name: "Befristeter Betrag",
    amount: "10",
    start: "4",
    end: "6",
    candidate: candidateId,
  });
  await addCost(context, {
    name: "Gutschrift",
    frequency: "Once",
    amount: "50",
    start: "5",
    direction: "Income",
    candidate: candidateId,
  });
  await addCost(context, {
    name: "Offener Betrag",
    start: "13",
    end: "15",
    candidate: candidateId,
  });
  await addCost(context, {
    name: "Service inklusive",
    frequency: "Included",
    end: "12",
    candidate: candidateId,
  });
  await addCost(context, { name: "Bekannte Null", amount: "0", candidate: candidateId });

  assert.equal(await page.locator("[data-cost-net]").getAttribute("data-cost-net"), "995");
  assert.equal(await page.locator("[data-cost-missing]").getAttribute("data-cost-missing"), "1");
  assert.equal(await page.locator("[data-cost-id]").count(), 8);
  assert.match(await page.locator('[data-cost-period="2"]').textContent(), /145,00/);
  assert.match(await page.locator('[data-cost-period="5"]').textContent(), /-15,00/);

  // A known amount of zero is stored as 0, an amount that was left open as null.
  const exported = await exportCase(context);
  assert.equal(exported.version, 9);
  assert.deepEqual(exported.costPlanning, { start: "2027-10-01", months: 15 });
  assert.equal(exported.costs.find((cost) => cost.input.name === "Bekannte Null").input.amount, 0);
  assert.equal(
    exported.costs.find((cost) => cost.input.name === "Offener Betrag").input.amount,
    null,
  );
  assert.equal(
    exported.costs.find((cost) => cost.input.name === "Service inklusive").input.frequency,
    "Included",
  );

  // The plan starts in October, so the calendar-year view shows two years.
  await page.getByRole("link", { name: "Jahre", exact: true }).click();
  assert.equal(await page.locator("[data-cost-period]").count(), 2);
  assert.match(await page.locator('[data-cost-period="1"]').textContent(), /595,00/);
  assert.match(await page.locator('[data-cost-period="4"]').textContent(), /400,00/);
  await page.screenshot({ path: join(output, "21-cost-planning-desktop.png"), fullPage: true });

  await page.getByRole("link", { name: /Für alle Autos Diese Positionen/ }).click();
  assert.equal(await page.locator("[data-cost-net]").getAttribute("data-cost-net"), "615");
  assert.equal(await page.locator("[data-cost-id]").count(), 2);

  record(
    "Cost planning reconciles one-off, monthly and annual payments, income, inclusive items, unknowns and calendar-year totals",
  );

  return sharedMonthlyCostId;
}

// ---------------------------------------------------------------------------
// Plan changes, mobile screens, validation and restart
// ---------------------------------------------------------------------------

async function checkPlanChangesMobileAndRestart(context, candidateId, sharedMonthlyCostId) {
  const { page, origin, output, record, restart, costsPath } = context;

  // Shortening the plan to 12 months moves the open amount (months 13 to 15) out of the period; it is kept, not deleted.
  await page.goto(`${origin}${costsPath}?candidateId=${candidateId}`);
  await page.locator("#open-cost-plan").click();
  await page.getByLabel(PLAN_MONTHS_LABEL, { exact: true }).fill("12");
  await savePlan(page);
  assert.equal(await page.locator("[data-cost-missing]").count(), 0);
  await page.getByText("Ausserhalb des gewählten Zeitraums", { exact: true }).waitFor();

  await page.locator("#open-cost-plan").click();
  await page.getByLabel(PLAN_MONTHS_LABEL, { exact: true }).fill("15");
  await savePlan(page);
  assert.equal(await page.locator("[data-cost-id]").count(), 8);

  await page.setViewportSize(MOBILE_VIEWPORT);
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  assert.ok(
    await page.locator(".cost-row").evaluateAll((rows) =>
      rows.every((row) => {
        const amount = row.querySelector(".cost-row-amount").getBoundingClientRect();
        const actions = row.querySelector(".cost-row-actions").getBoundingClientRect();
        return amount.bottom <= actions.top;
      }),
    ),
    "Mobile cost actions must not overlap the amount or payment details",
  );
  await page.screenshot({ path: join(output, "22-cost-planning-mobile.png"), fullPage: true });

  await page
    .locator(`[data-cost-id="${sharedMonthlyCostId}"]`)
    .getByRole("link", { name: "Bearbeiten", exact: true })
    .click();
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  await page.screenshot({ path: join(output, "23-cost-edit-mobile.png"), fullPage: true });

  // Browser validation is switched off so the server has to reject the third decimal place.
  await page.getByLabel(AMOUNT_LABEL, { exact: false }).fill("25.001");
  await page.locator("[data-cost-form]").evaluate((form) => {
    form.noValidate = true;
  });
  const invalidSave = waitForPostToCurrentUrl(page);
  await page.locator("#save-cost").click();
  assert.equal((await invalidSave).status(), 422);
  assert.equal(await page.getByLabel(AMOUNT_LABEL, { exact: false }).inputValue(), "25.001");
  await page.setViewportSize(DESKTOP_VIEWPORT);

  await restart();
  await page.goto(`${origin}${costsPath}?candidateId=${candidateId}`);
  assert.equal(await page.locator("[data-cost-net]").getAttribute("data-cost-net"), "995");

  record(
    "Plan changes retain out-of-period items; validation preserves drafts; cost planning and editing work at 390px and after restart",
  );
}

// ---------------------------------------------------------------------------
// Reader and editor access, revocation, antiforgery and concurrent edits
// ---------------------------------------------------------------------------

async function checkAccessAndConcurrency(context, candidateId, sharedMonthlyCostId) {
  const { page, reader, readerEmail, origin, post, browserRequest, record } = context;
  const { casePath, costsPath } = context;

  let caseVersion = await readCaseVersion(context);
  const readerShare = await post(page, casePath, {
    action: "share",
    email: readerEmail,
    access: "Reader",
    version: caseVersion,
  });
  assert.equal(readerShare.status(), 200);

  // A reader sees the plan but can neither open the editor nor write.
  await reader.page.goto(`${origin}${costsPath}?candidateId=${candidateId}`);
  assert.equal(await reader.page.getByRole("link", { name: ADD_COST_LINK }).count(), 0);
  assert.equal(
    (await browserRequest(reader.page, `${costsPath}/edit/${sharedMonthlyCostId}`)).status(),
    403,
  );

  const readerVersion = await reader.page.locator("#cost-workspace").getAttribute("data-version");
  const deniedRemoval = await post(reader.page, costsPath, {
    action: "remove-cost",
    Version: readerVersion,
    costId: sharedMonthlyCostId,
  });
  assert.equal(deniedRemoval.status(), 403);
  const deniedCreation = await post(reader.page, `${costsPath}/edit`, {
    Version: readerVersion,
    "Input.Name": "Denied",
  });
  assert.equal(deniedCreation.status(), 403);

  caseVersion = await readCaseVersion(context);
  const editorShare = await post(page, casePath, {
    action: "share",
    email: readerEmail,
    access: "Editor",
    version: caseVersion,
  });
  assert.equal(editorShare.status(), 200);

  // The owner starts a draft, the editor saves first: the owner's save must conflict and keep the draft.
  await page.goto(`${origin}${costsPath}/edit/${sharedMonthlyCostId}`);
  const staleVersion = await page.locator("[name=Version]").inputValue();
  await page.getByLabel(AMOUNT_LABEL, { exact: false }).fill("111");

  await reader.page.goto(`${origin}${costsPath}/edit/${sharedMonthlyCostId}`);
  await reader.page.getByLabel("Notiz / Grundlage", { exact: false }).fill("Reviewed by editor");
  await saveAndWaitForLoad(reader.page, () => reader.page.locator("#save-cost").click());
  await reader.page.locator("#cost-workspace").waitFor();

  const conflictingSave = waitForPostToCurrentUrl(page);
  await page.locator("#save-cost").click();
  assert.equal((await conflictingSave).status(), 409);
  assert.equal(await page.getByLabel(AMOUNT_LABEL, { exact: false }).inputValue(), "111");
  assert.equal(await page.locator("[name=Version]").inputValue(), staleVersion);

  // A direct request without the antiforgery token must be rejected.
  const withoutToken = await browserRequest(page, `${costsPath}/edit/${sharedMonthlyCostId}`, {
    Version: staleVersion,
    "Input.Name": "No token",
  });
  assert.equal(withoutToken.status(), 400);

  caseVersion = await readCaseVersion(context);
  const revocation = await post(page, casePath, {
    action: "revoke",
    userId: await page.locator(".share-row input[name=userId]").inputValue(),
    version: caseVersion,
  });
  assert.equal(revocation.status(), 200);
  assert.equal((await browserRequest(reader.page, costsPath)).status(), 403);

  record(
    "Cost reads and writes enforce roles, revoked access, antiforgery and concurrent-edit protection",
  );
}

// ---------------------------------------------------------------------------
// Car copies, JSON import and deletion
// ---------------------------------------------------------------------------

/** Removes the original car and returns the id of its copy, which keeps its own costs. */
async function checkCopyJsonImportAndDeletion(context, candidateId) {
  const { page, origin, post, browserRequest, record, casePath, costsPath } = context;

  await page.goto(`${origin}${casePath}/candidates`);
  const version = await page.locator("#candidate-workspace").getAttribute("data-version");
  const copyResponse = await post(page, `${casePath}/candidates/${candidateId}`, {
    action: "copy-candidate",
    copyName: "Kostenkopie",
    version,
  });
  assert.equal(copyResponse.status(), 0);

  // The copy receives its own six car-specific costs next to the eight existing entries.
  const withCopy = await exportCase(context);
  const copiedCandidateId = withCopy.candidates.find(
    (candidate) => candidate.model === "Kostenkopie",
  ).id;
  assert.equal(
    withCopy.costs.filter((cost) => cost.input.candidateId === copiedCandidateId).length,
    6,
  );
  assert.equal(withCopy.costs.length, 14);

  await page.goto(origin);
  await page.getByRole("button", { name: "Evaluation importieren", exact: true }).click();
  await page.getByLabel("Exportdatei (JSON oder ZIP)").setInputFiles({
    name: "costs.json",
    mimeType: "application/json",
    buffer: Buffer.from(JSON.stringify(withCopy)),
  });
  await page.getByRole("button", { name: "Als neue Evaluation importieren", exact: true }).click();
  await page.waitForURL(EVALUATION_URL);
  const importedPath = new URL(page.url()).pathname;
  const importedExport = await browserRequest(page, `${importedPath}?handler=Export`);
  const imported = JSON.parse(await importedExport.text());

  // The import receives new ids, and car-specific costs point at the imported cars.
  assert.deepEqual(imported.costPlanning, withCopy.costPlanning);
  assert.equal(imported.costs.length, 14);
  assert.ok(
    imported.costs.every((cost) => !withCopy.costs.some((original) => original.id === cost.id)),
  );
  assert.ok(
    imported.costs
      .filter((cost) => cost.input.candidateId)
      .every((cost) =>
        imported.candidates.some((candidate) => candidate.id === cost.input.candidateId),
      ),
  );

  // Removing in the car record confirms first and returns to the overview.
  await page.goto(`${origin}${casePath}/candidates/${candidateId}`);
  await page.locator("#remove-candidate").click();
  await page.locator("#action-confirmation [data-confirm-accept]").click();
  await page.waitForURL((url) => url.pathname === `${casePath}/candidates`);
  assert.equal(await page.locator(`[data-candidate-id="${candidateId}"]`).count(), 0);

  const remaining = await exportCase(context);
  assert.equal(remaining.costs.length, 8);
  assert.equal(remaining.costs.filter((cost) => !cost.input.candidateId).length, 2);
  await page.goto(`${origin}${costsPath}?candidateId=${copiedCandidateId}`);
  assert.equal(await page.locator("[data-cost-net]").getAttribute("data-cost-net"), "995");

  // Deleting the zero-amount cost of the copy changes the count but not the total.
  const zeroCostId = remaining.costs.find((cost) => cost.input.name === "Bekannte Null").id;
  await page
    .locator(`[data-cost-id="${zeroCostId}"]`)
    .getByRole("button", { name: "Kostenposition löschen", exact: true })
    .click();
  await saveAndWaitForLoad(page, () => page.locator("[data-confirm-accept]").click());
  await page.waitForURL((url) => url.pathname === costsPath && url.hash === "#cost-items");
  assert.equal(await page.locator("[data-cost-id]").count(), 7);
  assert.equal(await page.locator("[data-cost-net]").getAttribute("data-cost-net"), "995");
  assert.equal(
    (await exportCase(context)).costs.some((cost) => cost.id === zeroCostId),
    false,
  );

  record(
    "Candidate copies and JSON import retain independent costs, timing and scope; candidate and cost deletion preserve unrelated entries",
  );

  return copiedCandidateId;
}
