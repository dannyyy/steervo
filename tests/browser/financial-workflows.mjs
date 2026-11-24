import assert from "node:assert/strict";

const MOBILE_VIEWPORT = { width: 390, height: 844 };
const DESKTOP_VIEWPORT = { width: 1440, height: 1050 };

const EXPIRED_SESSION_TIMEOUT_MS = 10_000;

const EDIT_LINK = "Bearbeiten";
const RETRY_SAVE_BUTTON = "Stand geprüft – erneut speichern";
const RETRY_CONFIRMATION_BUTTON = "Stand geprüft – erneut bestätigen";
const REMOVE_COST_BUTTON = "Kostenposition löschen";
const REMOVE_SCENARIO_BUTTON = "Szenario löschen";
const REMOVE_CHECKPOINT_BUTTON = "Stichtag löschen";

const COST_EDIT_REQUESTS = "**/costs/edit/**";
const COST_LIST_REQUESTS = "**/costs?**";
const SCENARIO_REQUESTS = "**/scenarios/**";

// ---------------------------------------------------------------------------
// Shared interaction helpers
// ---------------------------------------------------------------------------

/** Runs an operation that submits a form and waits for the full page load it triggers. */
async function saveAndWaitForLoad(page, operation) {
  await Promise.all([page.waitForNavigation({ waitUntil: "load" }), operation()]);
}

/** Runs an operation that posts to the current page and checks the status it is answered with. */
async function expectPostStatus(page, status, operation) {
  const path = new URL(page.url()).pathname;
  const pending = page.waitForResponse(
    (response) =>
      response.request().method() === "POST" && new URL(response.url()).pathname === path,
  );

  await operation();

  assert.equal((await pending).status(), status);
  await page.waitForFunction(() => !document.querySelector(".htmx-request"));
}

async function waitForFocus(page, id) {
  await page.waitForFunction((expectedId) => document.activeElement?.id === expectedId, id);
}

/** Lets every POST matching the pattern fail like a lost connection; other requests pass. */
async function abortPosts(page, urlPattern) {
  await page.route(urlPattern, (route) =>
    route.request().method() === "POST" ? route.abort("failed") : route.continue(),
  );
}

function effectiveCost(page) {
  return page.locator("[data-effective-cost]").getAttribute("data-effective-cost");
}

async function share({ page, post, origin, casePath, readerEmail }, access) {
  await page.goto(origin + casePath);
  const version = await page.locator("#evaluation-workspace").getAttribute("data-version");

  assert.equal(
    (await post(page, casePath, { action: "share", email: readerEmail, access, version })).status(),
    200,
  );
}

async function revoke({ page, post, origin, casePath }) {
  await page.goto(origin + casePath);
  const version = await page.locator("#evaluation-workspace").getAttribute("data-version");
  const userId = await page.locator(".share-row input[name=userId]").inputValue();

  assert.equal((await post(page, casePath, { action: "revoke", userId, version })).status(), 200);
}

/**
 * Two users have the same list open. The other user first changes something, later removes the
 * item itself. The removal on the first page must conflict, ask for a fresh confirmation and
 * finally report that the item is gone instead of retrying forever.
 */
async function checkConcurrentRemoval(
  page,
  otherPage,
  url,
  removalSelector,
  removeButtonLabel,
  changeByOtherUser,
) {
  await page.goto(url);
  await otherPage.goto(url);
  await changeByOtherUser(otherPage);

  // The first attempt works on an outdated version.
  await page
    .locator(removalSelector)
    .getByRole("button", { name: removeButtonLabel, exact: true })
    .click();
  await expectPostStatus(page, 409, () => page.locator("[data-confirm-accept]").click());
  const error = page.locator(removalSelector + " .editor-error");
  await error.getByText(/Die Aktion wurde nicht ausgeführt/).waitFor();

  // Retrying opens the confirmation again with the safe choice focused; Escape keeps the notice.
  await error.getByRole("button", { name: RETRY_CONFIRMATION_BUTTON, exact: true }).click();
  await page.locator("#action-confirmation[open]").waitFor();
  assert.equal(
    await page
      .locator("[data-confirm-cancel]")
      .evaluate((element) => element === document.activeElement),
    true,
  );
  await page.keyboard.press("Escape");
  assert.equal(await error.isVisible(), true);

  // Meanwhile the other user removes the item.
  await otherPage
    .locator(removalSelector)
    .getByRole("button", { name: removeButtonLabel, exact: true })
    .click();
  await saveAndWaitForLoad(otherPage, () => otherPage.locator("[data-confirm-accept]").click());

  await error.getByRole("button", { name: RETRY_CONFIRMATION_BUTTON, exact: true }).click();
  await expectPostStatus(page, 404, () => page.locator("[data-confirm-accept]").click());
  await error.getByText(/nicht mehr verfügbar/).waitFor();
  assert.equal(await page.locator(removalSelector + " button[type=submit]").isDisabled(), true);

  await page.reload();
  assert.equal(await page.locator(removalSelector).count(), 0);
}

// ---------------------------------------------------------------------------
// Cost planning workflows
// ---------------------------------------------------------------------------

/** Editing, conflict and deletion workflows of cost planning for one car of a prepared case. */
export async function checkCostWorkflows(options) {
  const { page, reader, origin, casePath, candidateId, costId, record } = options;
  const costsPath = casePath + "/costs";

  await checkCostEditKeepsContext(options, costsPath);
  await checkCostPlanConflict(options, costsPath);
  await checkCostPermissionAndNamedDeletion(options, costsPath);

  await share(options, "Editor");
  await checkConcurrentRemoval(
    page,
    reader.page,
    `${origin}${costsPath}?candidateId=${candidateId}`,
    "#remove-cost-" + costId,
    REMOVE_COST_BUTTON,
    async (otherPage) => {
      await otherPage.locator("#open-cost-plan").click();
      await otherPage.locator("#Plan_Start").fill("2029-01");
      await saveAndWaitForLoad(otherPage, () => otherPage.locator("#save-cost-plan").click());
    },
  );
  record(
    "Cost deletion conflicts require explicit confirmation; an already removed item reports its absence and stops retrying",
  );
}

async function openCostEditor(page, costId) {
  await page.locator(`#cost-${costId}`).getByRole("link", { name: EDIT_LINK, exact: true }).click();
  await page.locator("#cost-page-editor[data-initialized=true]").waitFor();
}

async function checkCostEditKeepsContext(options, costsPath) {
  const { page, origin, candidateId, costId, record } = options;

  // 48 months give the monthly timeline a second page, which starts with month 25.
  await page.goto(`${origin}${costsPath}?candidateId=${candidateId}`);
  await page.locator("#open-cost-plan").click();
  await page.locator("#Plan_Months").fill("48");
  await saveAndWaitForLoad(page, () => page.locator("#save-cost-plan").click());
  await waitForFocus(page, "cost-plan-title");

  await page.goto(`${origin}${costsPath}?candidateId=${candidateId}&period=month&pageNumber=2`);
  assert.equal(
    await page.locator("[data-cost-period]").first().getAttribute("data-cost-period"),
    "25",
  );

  // Leaving the untouched editor with Escape returns to the same timeline page.
  await openCostEditor(page, costId);
  await page.keyboard.press("Escape");
  await page.locator("#cost-workspace").waitFor();
  assert.equal(new URL(page.url()).searchParams.get("pageNumber"), "2");

  // A failed save and a declined cancellation both keep the draft.
  await openCostEditor(page, costId);
  await page.locator("#Input_Notes").fill("Entwurf mit Zeitraum und Fahrzeugkontext");
  await abortPosts(page, COST_EDIT_REQUESTS);
  await page.locator("#save-cost").click();
  await page
    .locator(".editor-error")
    .getByText(/nicht gespeichert/)
    .waitFor();
  await page.unroute(COST_EDIT_REQUESTS);
  await page.getByRole("link", { name: "Abbrechen", exact: true }).click();
  await page.locator("[data-confirm-cancel]").click();
  assert.equal(
    await page.locator("#Input_Notes").inputValue(),
    "Entwurf mit Zeitraum und Fahrzeugkontext",
  );

  await saveAndWaitForLoad(page, () => page.locator("#save-cost").click());
  await waitForFocus(page, "cost-" + costId);
  assert.equal(new URL(page.url()).searchParams.get("candidateId"), candidateId);
  assert.equal(new URL(page.url()).searchParams.get("pageNumber"), "2");
  assert.equal(
    await page.locator("[data-cost-period]").first().getAttribute("data-cost-period"),
    "25",
  );

  record(
    "Cost edits retain the vehicle, timeline page, notes and return focus through cancellation and network failure",
  );
}

async function checkCostPlanConflict(options, costsPath) {
  const { page, reader, origin, candidateId, record } = options;

  await share(options, "Editor");

  // The owner drafts a new duration while the editor saves another start month first.
  await page.goto(`${origin}${costsPath}?candidateId=${candidateId}`);
  await page.locator("#open-cost-plan").click();
  await page.locator("#Plan_Months").fill("60");
  const staleVersion = await page.locator("#cost-plan-editor [name=Version]").inputValue();

  await reader.page.goto(origin + costsPath);
  await reader.page.locator("#open-cost-plan").click();
  await reader.page.locator("#Plan_Start").fill("2028-01");
  await saveAndWaitForLoad(reader.page, () => reader.page.locator("#save-cost-plan").click());

  await expectPostStatus(page, 409, () => page.locator("#save-cost-plan").click());
  assert.equal(await page.locator("#Plan_Months").inputValue(), "60");
  assert.equal(await page.locator("#cost-plan-editor [name=Version]").inputValue(), staleVersion);
  assert.match(await page.locator("#cost-plan-editor .editor-error").textContent(), /01.2028/);

  await saveAndWaitForLoad(page, () =>
    page.getByRole("button", { name: RETRY_SAVE_BUTTON, exact: true }).click(),
  );
  assert.match(await page.locator(".cost-plan-heading").textContent(), /60 Monate/);

  await page.locator("#open-cost-plan").click();
  await page.locator("#Plan_Months").fill("48");
  await saveAndWaitForLoad(page, () => page.locator("#save-cost-plan").click());
  assert.match(await page.locator(".cost-plan-heading").textContent(), /48 Monate/);
  assert.equal(await page.locator("#cost-plan-editor").isVisible(), false);
  await waitForFocus(page, "cost-plan-title");

  record(
    "Planning conflicts retain the draft until explicit retry; repeated saves refresh the same page and restore focus",
  );
}

async function checkCostPermissionAndNamedDeletion(options, costsPath) {
  const { page, reader, origin, candidateId, costId, record } = options;

  // Access is revoked while the editor still has an unsaved draft open.
  await reader.page.goto(`${origin}${costsPath}/edit/${costId}`);
  await reader.page.locator("#Input_Notes").fill("Entwurf ohne Schreibrecht");
  await revoke(options);
  await reader.page.locator("#save-cost").click();
  await reader.page
    .locator(".editor-error")
    .getByText(/nicht mehr bearbeiten/)
    .waitFor();
  assert.equal(await reader.page.locator("#Input_Notes").inputValue(), "Entwurf ohne Schreibrecht");
  assert.equal(await reader.page.locator("#save-cost").isDisabled(), true);

  // Any cost other than the one that is still needed by the following checks.
  await page.goto(`${origin}${costsPath}?candidateId=${candidateId}`);
  const removableCost = page
    .locator("[data-cost-id]")
    .filter({ hasNot: page.locator(`[id="remove-cost-${costId}"]`) })
    .first();
  const removedCostId = await removableCost.getAttribute("data-cost-id");
  const removedCostName = await removableCost.locator("h4").textContent();
  const removeButton = removableCost.getByRole("button", { name: REMOVE_COST_BUTTON, exact: true });

  // Deleting next to an unsaved plan draft asks before discarding that draft.
  await page.locator("#open-cost-plan").click();
  await page.locator("#Plan_Months").fill("72");
  await removeButton.click();
  assert.ok((await page.locator("#confirmation-title").textContent()).includes(removedCostName));
  await page.locator("[data-confirm-accept]").click();
  await page.getByRole("heading", { name: "Ungespeicherte Änderungen verwerfen?" }).waitFor();
  await page.locator("[data-confirm-cancel]").click();
  assert.equal(await page.locator("#Plan_Months").inputValue(), "72");
  assert.equal(await page.locator("#cost-" + removedCostId).count(), 1);
  await page.locator("#cost-plan-editor [data-editor-close]").first().click();
  await page.locator("[data-confirm-accept]").click();

  // A lost connection leaves the cost in place and reports the failure next to it.
  await abortPosts(page, COST_LIST_REQUESTS);
  await removeButton.click();
  await page.locator("[data-confirm-accept]").click();
  await page
    .locator("#remove-cost-" + removedCostId)
    .getByText(/nicht gespeichert/)
    .waitFor();
  assert.equal(await page.locator("#cost-" + removedCostId).count(), 1);
  await page.unroute(COST_LIST_REQUESTS);

  await removeButton.click();
  await saveAndWaitForLoad(page, () => page.locator("[data-confirm-accept]").click());
  assert.equal(await page.locator("#cost-" + removedCostId).count(), 0);
  await waitForFocus(page, "cost-items");

  record(
    "Cost permissions and named deletion protect adjacent drafts, recover from network failure and return focus",
  );
}

// ---------------------------------------------------------------------------
// Scenario workflows
// ---------------------------------------------------------------------------

/** Editing, conflict and deletion workflows of the cost comparison for a prepared case. */
export async function checkScenarioWorkflows(options) {
  const { page, reader, origin, casePath, cash, lease, record } = options;
  const scenariosPath = casePath + "/scenarios";

  await checkScenarioEditKeepsComparisonContext(options, scenariosPath);
  await checkResidualValidationAndConflict(options, scenariosPath);
  const copyId = await checkAdjustmentAndCopyDrafts(options, scenariosPath);
  await checkSharedSettingsAndExpiredSession(options, scenariosPath);
  await checkNamedResidualDeletion(options, scenariosPath);

  await checkConcurrentRemoval(
    page,
    reader.page,
    origin + scenariosPath,
    "#remove-scenario-" + copyId,
    REMOVE_SCENARIO_BUTTON,
    async (otherPage) => {
      await otherPage.locator("#open-scenario-settings").click();
      await otherPage.locator("#AnnualReturn").fill("3");
      await saveAndWaitForLoad(otherPage, () =>
        otherPage.locator("#save-scenario-settings").click(),
      );
    },
  );
  assert.equal(await page.locator("#remove-scenario-" + cash).count(), 1);
  assert.equal(await page.locator("#remove-scenario-" + lease).count(), 1);
  record(
    "Concurrent scenario deletion keeps the other scenarios intact and requires a new explicit confirmation",
  );
}

async function checkScenarioEditKeepsComparisonContext(options, scenariosPath) {
  const { page, origin, cash, lease, record } = options;

  await page.setViewportSize(MOBILE_VIEWPORT);
  await page.goto(`${origin}${scenariosPath}/compare?leftId=${cash}&rightId=${lease}&month=12`);
  await page.locator(`[data-scenario-column="${lease}"] .comparison-candidate-link`).click();
  await page.getByRole("link", { name: "Jahre", exact: true }).click();
  await page.getByRole("link", { name: "Szenario bearbeiten", exact: true }).click();

  // Switching the offer revision changes the source link but not the entered assumptions.
  await page.locator("#Input_RevisionNumber").selectOption("2");
  assert.equal(await page.locator("#scenario-MonthlyPayment").inputValue(), "50");
  const sourceLink = await page.locator("[data-scenario-source]").getAttribute("href");
  assert.equal(new URL(sourceLink, origin).searchParams.get("revision"), "2");

  await saveAndWaitForLoad(page, () => page.locator("#save-scenario").click());
  await waitForFocus(page, "scenario-title");
  assert.equal(new URL(page.url()).searchParams.get("period"), "year");
  assert.equal(new URL(page.url()).searchParams.get("month"), "12");
  assert.equal(Number(await effectiveCost(page)), 820);

  await page.getByRole("link", { name: "Szenarien vergleichen", exact: true }).click();
  assert.equal(await page.locator("#scenario-left").inputValue(), cash);
  assert.equal(await page.locator("#scenario-right").inputValue(), lease);

  // On a phone the comparison scrolls to the scenario just edited and keeps its header pinned.
  await page.waitForFunction(
    () => document.querySelector("#scenario-comparison-scroll").scrollLeft > 50,
  );
  assert.equal(
    await page
      .locator(".scenario-table thead th")
      .first()
      .evaluate((element) => getComputedStyle(element).position),
    "sticky",
  );
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));

  record(
    "Scenario editing preserves the pair, checkpoint and yearly view; source links follow the selected revision without replacing assumptions",
  );
}

async function checkResidualValidationAndConflict(options, scenariosPath) {
  const { page, reader, origin, cash, record } = options;

  // Pessimistic above expected above optimistic is rejected; the draft and its note stay.
  await page.setViewportSize(DESKTOP_VIEWPORT);
  await page.goto(`${origin}${scenariosPath}/${cash}?month=6`);
  await page.locator("#open-residual").click();
  await page.locator("#Residual_Low").fill("600");
  await page.locator("#Residual_Expected").fill("400");
  await page.locator("#Residual_High").fill("300");
  await page.locator("#Residual_Notes").fill("Entwurf für den genauen Stichtag");
  await expectPostStatus(page, 422, () => page.locator("#save-residual").click());
  assert.equal(await page.locator("#Residual_Low").inputValue(), "600");
  assert.equal(
    await page.locator("#Residual_Notes").inputValue(),
    "Entwurf für den genauen Stichtag",
  );

  await page.keyboard.press("Escape");
  await page.locator("[data-confirm-cancel]").click();
  await page.locator("#Residual_Low").fill("300");
  await page.locator("#Residual_High").fill("500");
  await saveAndWaitForLoad(page, () => page.locator("#save-residual").click());
  await waitForFocus(page, "scenario-residuals");
  assert.equal(Number(await effectiveCost(page)), 770);

  // Both users edit the same checkpoint; the editor saves first.
  await share(options, "Editor");
  await page.goto(`${origin}${scenariosPath}/${cash}?month=6&residualMonth=6`);
  await page.locator("#Residual_Expected").fill("420");
  const staleVersion = await page.locator("#residual-form [name=Version]").inputValue();

  await reader.page.goto(`${origin}${scenariosPath}/${cash}?month=6&residualMonth=6`);
  await reader.page.locator("#Residual_Expected").fill("450");
  await saveAndWaitForLoad(reader.page, () => reader.page.locator("#save-residual").click());

  await expectPostStatus(page, 409, () => page.locator("#save-residual").click());
  assert.equal(await page.locator("#Residual_Expected").inputValue(), "420");
  assert.equal(await page.locator("#residual-form [name=Version]").inputValue(), staleVersion);

  await saveAndWaitForLoad(page, () =>
    page.getByRole("button", { name: RETRY_SAVE_BUTTON, exact: true }).click(),
  );
  assert.equal(Number(await effectiveCost(page)), 750);

  record(
    "Residual validation and conflicts retain exact checkpoint assumptions and notes; explicit retry updates the calculated result",
  );
}

/** Returns the id of the scenario copy created here; a later check removes it again. */
async function checkAdjustmentAndCopyDrafts(options, scenariosPath) {
  const { page, origin, lease, cost, record } = options;

  // A failed save keeps the adjustment draft; the repeated save applies it.
  await page.goto(`${origin}${scenariosPath}/${lease}?costId=${cost}`);
  await page.locator("#Adjustment_Amount").fill("7");
  await page.locator("#Adjustment_Notes").fill("Lokale Anpassung");
  await abortPosts(page, SCENARIO_REQUESTS);
  await page.locator("#save-adjustment").click();
  await page
    .locator("#adjustment-editor .editor-error")
    .getByText(/nicht gespeichert/)
    .waitFor();
  assert.equal(await page.locator("#Adjustment_Amount").inputValue(), "7");
  assert.equal(await page.locator("#Adjustment_Notes").inputValue(), "Lokale Anpassung");
  await page.unroute(SCENARIO_REQUESTS);

  await saveAndWaitForLoad(page, () => page.locator("#save-adjustment").click());
  await waitForFocus(page, "scenario-adjustments");
  assert.equal(Number(await effectiveCost(page)), 862);

  // A blank copy name is rejected, and a declined cancellation keeps the corrected name.
  await page.locator("#open-scenario-copy").click();
  await page.locator("#CopyName").fill("   ");
  await expectPostStatus(page, 422, () => page.locator("#save-scenario-copy").click());
  await page.locator("#CopyName").fill("Geschützte Kopie");
  await page.keyboard.press("Escape");
  await page.locator("[data-confirm-cancel]").click();
  assert.equal(await page.locator("#CopyName").inputValue(), "Geschützte Kopie");

  await saveAndWaitForLoad(page, () => page.locator("#save-scenario-copy").click());
  const copyId = new URL(page.url()).pathname.split("/").at(-1);
  assert.notEqual(new URL(page.url()).pathname, scenariosPath + "/" + lease);
  assert.equal(Number(await effectiveCost(page)), 862);

  record(
    "Cost adjustments and scenario copies use the same guarded editors, retain failed drafts and keep independent calculations",
  );

  return copyId;
}

async function checkSharedSettingsAndExpiredSession(options, scenariosPath) {
  const { page, reader, origin, cash, record } = options;

  // Both users change the shared return assumption; the editor saves first.
  await page.goto(origin + scenariosPath);
  await page.locator("#open-scenario-settings").click();
  await page.locator("#AnnualReturn").fill("2");

  await reader.page.goto(origin + scenariosPath);
  await reader.page.locator("#open-scenario-settings").click();
  await reader.page.locator("#AnnualReturn").fill("1");
  await saveAndWaitForLoad(reader.page, () =>
    reader.page.locator("#save-scenario-settings").click(),
  );

  await expectPostStatus(page, 409, () => page.locator("#save-scenario-settings").click());
  assert.equal(await page.locator("#AnnualReturn").inputValue(), "2");
  await saveAndWaitForLoad(page, () =>
    page.getByRole("button", { name: RETRY_SAVE_BUTTON, exact: true }).click(),
  );
  await waitForFocus(page, "scenario-settings-title");

  await page.locator("#open-scenario-settings").click();
  assert.equal(await page.locator("#AnnualReturn").inputValue(), "2");
  await page.keyboard.press("Escape");
  await waitForFocus(page, "open-scenario-settings");

  // A second signed-in browser loses its session while a scenario draft is open.
  const expiredContext = await page
    .context()
    .browser()
    .newContext({
      storageState: await page.context().storageState(),
      ignoreHTTPSErrors: origin.startsWith("https:"),
    });
  const expiredPage = await expiredContext.newPage();
  expiredPage.setDefaultTimeout(EXPIRED_SESSION_TIMEOUT_MS);
  await expiredPage.goto(`${origin}${scenariosPath}/edit/${cash}`);
  await expiredPage.locator("#Input_Notes").fill("Erhaltene Rechenannahmen ohne Sitzung");
  await expiredContext.clearCookies();
  await expiredPage.locator("#save-scenario").click();
  await expiredPage
    .locator(".editor-error")
    .getByText(/nicht mehr bearbeiten/)
    .waitFor();
  assert.equal(
    await expiredPage.locator("#Input_Notes").inputValue(),
    "Erhaltene Rechenannahmen ohne Sitzung",
  );
  assert.equal(await expiredPage.locator("#save-scenario").isDisabled(), true);
  await expiredContext.close();

  record(
    "Shared return assumptions require explicit conflict retry, and expired sessions retain full scenario drafts",
  );
}

async function checkNamedResidualDeletion(options, scenariosPath) {
  const { page, origin, cash, record } = options;

  await page.goto(`${origin}${scenariosPath}/${cash}?month=6`);
  const checkpoint = page.locator('[data-residual-month="6"]');

  // The confirmation names the checkpoint; Escape leaves it in place.
  await checkpoint.getByRole("button", { name: REMOVE_CHECKPOINT_BUTTON, exact: true }).click();
  assert.match(await page.locator("#confirmation-title").textContent(), /Monat 6/);
  await page.keyboard.press("Escape");
  assert.equal(await checkpoint.count(), 1);

  await checkpoint.getByRole("button", { name: REMOVE_CHECKPOINT_BUTTON, exact: true }).click();
  await saveAndWaitForLoad(page, () => page.locator("[data-confirm-accept]").click());
  assert.equal(await page.locator('[data-residual-month="6"]').count(), 0);
  assert.equal(await page.locator('[data-residual-month="12"]').count(), 1);
  assert.equal(await effectiveCost(page), "");
  await waitForFocus(page, "scenario-residuals");

  record(
    "Named residual deletion preserves other checkpoints and makes the missing result explicitly unknown again",
  );
}
