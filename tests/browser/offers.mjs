import assert from "node:assert/strict";
import { join } from "node:path";

const DESKTOP_VIEWPORT = { width: 1440, height: 1050 };
const PHONE_VIEWPORT = { width: 390, height: 844 };
const UNKNOWN_OFFER_ID = "00000000-0000-0000-0000-000000000001";
const OFFER_EDIT_REQUESTS = "**/offers/edit/**";

/**
 * Covers offers in an evaluation of its own: incomplete drafts, price lists, immutable versions,
 * comparison, ordering, roles, conflicts and export/import. The steps build on each other and
 * must run in this order.
 */
export async function checkOffers({
  page,
  reader,
  readerEmail,
  origin,
  output,
  post,
  browserRequest,
  record,
}) {
  const originalPath = new URL(page.url()).pathname;
  const context = { page, reader, readerEmail, origin, output, post, browserRequest, record };

  context.casePath = await createEvaluation(context);
  context.candidateId = await addCandidateForOffers(context);
  Object.assign(context, await addOfferCriteria(context));

  Object.assign(context, await createIncompleteOffer(context));
  await checkCurrencyIsLockedAfterFirstOffer(context);
  await checkRecordActions(context);
  await checkSecondVersionWithPriceList(context);
  await checkNegotiatedVersion(context);

  context.secondId = await createSecondOffer(context);
  await checkComparedValuesAndNumberFormats(context);
  await checkOverviewSelectionAndComparison(context);
  await checkPhoneComparison(context);
  await checkLineFulfilsRequirement(context);
  await checkOrderingAndDeletion(context);

  await checkReaderCannotWrite(context);
  await checkEditorConflict(context);
  await checkRevokedEditorKeepsDraft(context);
  context.exported = await checkExportedOffers(context);
  await checkImportedOffers(context);

  await page.goto(`${origin}${originalPath}`);
  return { casePath: context.casePath, firstId: context.firstId };
}

// ---------------------------------------------------------------------------------------------
// Shared interactions
// ---------------------------------------------------------------------------------------------

async function exportEvaluation(page, browserRequest, casePath) {
  return JSON.parse(await (await browserRequest(page, `${casePath}?handler=Export`)).text());
}

/** Opens the evaluation on the owner's page and returns its current version. */
async function evaluationVersion({ page, origin, casePath }) {
  await page.goto(`${origin}${casePath}`);
  return page.locator("#evaluation-workspace").getAttribute("data-version");
}

function shareEvaluation({ page, post, casePath, readerEmail }, access, version) {
  return post(page, casePath, { action: "share", email: readerEmail, access, version });
}

function waitForHtmxToSettle(target) {
  return target.waitForFunction(() => !document.querySelector(".htmx-settling, .htmx-request"));
}

/** Runs a write and waits until the offer record has re-rendered with a new version. */
async function awaitOfferChange(page, operation) {
  const version = await page.locator("#offer-workspace").getAttribute("data-version");
  await operation();
  await page.waitForFunction(
    (old) => document.querySelector("#offer-workspace")?.dataset.version !== old,
    version,
  );
  await waitForHtmxToSettle(page);
}

/** Starts listening for the response to a form post back to the page that is currently open. */
function waitForPostToCurrentPage(page) {
  return page.waitForResponse(
    (response) => response.request().method() === "POST" && response.url() === page.url(),
  );
}

/** Starts listening for the response to a post to the given path. */
function waitForPostTo(target, path) {
  return target.waitForResponse(
    (response) =>
      new URL(response.url()).pathname === path && response.request().method() === "POST",
  );
}

function newVersionLink(page) {
  return page.getByRole("link", { name: "Neue Version erfassen", exact: true });
}

function waitForOfferVersion(page, revision) {
  return page.locator(`#offer-workspace[data-revision="${revision}"]`).waitFor();
}

function offerLines(page) {
  return page.locator("[data-offer-line]");
}

function lineName(line) {
  return line.getByLabel("Bezeichnung der Position", { exact: true });
}

function benefitValue(line) {
  return line.getByLabel("Wert der Gratisleistung", { exact: false });
}

function moveLineUpButton(line) {
  return line.getByRole("button", { name: "Position nach oben" });
}

/** Appends a price line to the offer form; the amount is left untouched when omitted. */
async function addOfferLine(page, name, kind, price, amount) {
  await page.getByRole("button", { name: /Position hinzufügen/ }).click();
  const line = offerLines(page).last();
  await lineName(line).fill(name);
  await line.getByLabel("Art", { exact: true }).selectOption(kind);
  await line.getByLabel("Preisangabe", { exact: true }).selectOption(price);
  if (amount !== undefined) {
    await line.getByLabel("Betrag", { exact: false }).fill(amount);
  }
  return line;
}

function offerAssessment(target, criterionId) {
  return target.locator(`[data-offer-assessment="${criterionId}"]`);
}

function assessmentAnswer(assessment) {
  return assessment.getByLabel("Deine Angabe");
}

function assessmentSubmitButton(assessment) {
  return assessment.locator(".value-editor button[type=submit]");
}

async function submitAssessment(page, assessment) {
  await awaitOfferChange(page, () => assessmentSubmitButton(assessment).click());
}

/** Offer ids of the comparison columns, from left to right. */
function comparedOfferIds(page) {
  return page
    .locator("[data-offer-column]")
    .evaluateAll((cells) => cells.map((cell) => cell.dataset.offerColumn));
}

/** Offer ids of the overview cards, from top to bottom. */
function offerOrder(page) {
  return page
    .locator(".offer-card")
    .evaluateAll((cards) => cards.map((card) => card.dataset.offerId));
}

async function compareButtonLabel(page) {
  return (await page.locator("#compare-selected").innerText()).trim();
}

function pairComparisonUrl({ origin, casePath, firstId, secondId }) {
  return `${origin}${casePath}/offers/compare?first=${firstId}&second=${secondId}`;
}

function fitsViewportWidth(page) {
  return page.evaluate(() => document.documentElement.scrollWidth <= innerWidth);
}

function comparisonParameter(page, name) {
  return new URL(page.url()).searchParams.get(name);
}

// ---------------------------------------------------------------------------------------------
// Setup: an evaluation with one car and three offer requirements
// ---------------------------------------------------------------------------------------------

async function createEvaluation({ page, origin }) {
  await page.goto(origin);
  await page.locator('[data-dialog-open="new-evaluation"]').first().click();
  const creation = page.getByRole("dialog", { name: "Neue Evaluation", exact: true });
  await creation.getByLabel("Name der Evaluation").fill("Offertenprüfung");
  await page.locator("#new-region-country").selectOption("CH");
  await page.locator("#new-region-currency").fill("EUR");
  await creation.getByRole("button", { name: "Evaluation anlegen", exact: false }).click();
  await page.waitForURL(/\/evaluations\//);
  return new URL(page.url()).pathname;
}

async function addCandidateForOffers({ page, origin, post, browserRequest, casePath }) {
  // Without a car there is nothing an offer could belong to.
  await page.getByRole("link", { name: "Offerten & Kosten", exact: true }).click();
  await page
    .locator(".workspace-subnav")
    .getByRole("link", { name: "Offerten", exact: true })
    .click();
  await page
    .getByText("Erfasse zuerst ein Auto, damit du ihm Offerten zuordnen kannst.", { exact: false })
    .waitFor();

  await page.goto(`${origin}${casePath}/candidates`);
  const version = await page.locator("#candidate-workspace").getAttribute("data-version");
  assert.equal(
    (
      await post(page, `${casePath}/candidates`, {
        action: "add-candidate",
        "Input.Model": "Angebotsauto",
        version,
      })
    ).status(),
    0,
  );
  const initial = await exportEvaluation(page, browserRequest, casePath);
  return initial.candidates[0].id;
}

async function addCriterion(context, fields) {
  const { page, post, casePath } = context;
  const version = await evaluationVersion(context);
  return post(page, casePath, { action: "add-criterion", ...fields, version });
}

async function addOfferCriteria(context) {
  const { page, browserRequest, casePath } = context;

  assert.equal(
    (
      await addCriterion(context, {
        name: "Kaufrecht",
        type: "Boolean",
        priority: "Required",
        scope: "Offer",
      })
    ).status(),
    200,
  );
  assert.equal(
    (
      await addCriterion(context, {
        name: "Zins",
        type: "Measurement",
        priority: "Required",
        scope: "Offer",
        threshold: "3",
        unit: "%",
        comparison: "AtMost",
      })
    ).status(),
    200,
  );
  assert.equal(
    (
      await addCriterion(context, {
        name: "Winterräder",
        type: "Boolean",
        priority: "Preferred",
        scope: "Offer",
      })
    ).status(),
    200,
  );

  const definition = await exportEvaluation(page, browserRequest, casePath);
  return {
    booleanId: definition.criteria.find((criterion) => criterion.type === "Boolean").id,
    measurementId: definition.criteria.find((criterion) => criterion.type === "Measurement").id,
    wheelsId: definition.criteria.find((criterion) => criterion.name === "Winterräder").id,
  };
}

// ---------------------------------------------------------------------------------------------
// First offer: incomplete start, locked currency, record actions
// ---------------------------------------------------------------------------------------------

async function createIncompleteOffer({ page, origin, record, casePath, candidateId }) {
  await page.goto(`${origin}${casePath}/offers`);
  await page.getByRole("link", { name: /Offerte erfassen/ }).click();
  await page.getByRole("link", { name: /Alle Offerten/ }).click();
  await page.waitForURL(`${origin}${casePath}/offers`);
  assert.equal(new URL(page.url()).pathname, `${casePath}/offers`);

  // Started from a car, the form already knows the car and the evaluation currency.
  await page.goto(`${origin}${casePath}/candidates/${candidateId}`);
  await page.getByRole("link", { name: "Offerten für dieses Auto", exact: false }).click();
  await page.getByRole("link", { name: /Offerte erfassen/ }).click();
  assert.equal(await page.locator("#offer-candidate").inputValue(), candidateId);
  assert.equal(await page.locator("#offer-VehicleDescription").inputValue(), "Angebotsauto");
  assert.equal(await page.locator("#offer-currency").inputValue(), "EUR");
  await page.locator("#offer-Name").fill("Anbieter Nord");
  await page.getByRole("button", { name: /Offerte anlegen/ }).click();
  await page.locator("#offer-workspace").waitFor();

  const firstPath = new URL(page.url()).pathname;
  const firstId = firstPath.split("/").at(-1);
  assert.equal(await page.locator("#offer-workspace").getAttribute("data-revision"), "1");
  assert.match(await page.locator(".offer-missing").textContent(), /Preis/);
  assert.match(await page.locator('[data-offer-price="Total"]').textContent(), /Offen/);

  record("Offers can start incomplete, inherit their candidate and use the evaluation currency");
  return { firstPath, firstId };
}

async function checkCurrencyIsLockedAfterFirstOffer({
  page,
  origin,
  post,
  browserRequest,
  record,
  casePath,
  firstPath,
}) {
  await page.goto(`${origin}${casePath}?section=settings`);
  await page.locator("#edit-evaluation").click();
  assert.equal(await page.locator("#case-region-currency").isEditable(), false);

  // A forged change of country and currency is rejected as a whole, the new name included.
  const settingsVersion = await page.locator("#evaluation-workspace").getAttribute("data-version");
  assert.equal(
    (
      await post(page, casePath, {
        action: "rename",
        name: "Must not change",
        Country: "DE",
        Currency: "USD",
        version: settingsVersion,
      })
    ).status(),
    422,
  );
  const retained = await exportEvaluation(page, browserRequest, casePath);
  assert.equal(retained.country, "CH");
  assert.equal(retained.currency, "EUR");
  assert.equal(retained.name, "Offertenprüfung");
  await page.goto(`${origin}${firstPath}`);

  record(
    "Evaluation properties lock the currency after the first offer and reject forged changes atomically",
  );
}

async function checkRecordActions({ page }) {
  // The record actions follow the car record: below the title, main action first, deletion last.
  const recordActions = (
    await page
      .locator(".offer-introduction .vehicle-actions")
      .locator("a, button")
      .allTextContents()
  ).map((text) => text.trim());
  assert.deepEqual(
    [recordActions[0], recordActions[1], recordActions.at(-1)],
    ["Neue Version erfassen", "Offerten vergleichen", "Offerte löschen"],
  );
  assert.equal(
    await page
      .locator(".offer-introduction")
      .evaluate(
        (node) =>
          node.querySelector(".vehicle-actions").getBoundingClientRect().top >
          node.querySelector("h2").getBoundingClientRect().bottom,
      ),
    true,
  );
}

// ---------------------------------------------------------------------------------------------
// Second version: leasing terms, price list, failed saves, totals and assessments
// ---------------------------------------------------------------------------------------------

async function checkSecondVersionWithPriceList(context) {
  const { page, record } = context;

  await fillLeasingTerms(page);
  await arrangePriceLines(page);
  await checkDraftSurvivesFailedSaves(page);
  await checkTotalsWhileTyping(page);

  await page.locator("#offer-StatedTotal").fill("27000");
  await page.locator("#save-offer").click();
  await waitForOfferVersion(page, 2);

  await checkSavedPriceList(context);
  await checkAssessmentEditing(context);

  record(
    "Structured offer fields, line ordering and validation retain drafts; zero, inclusive and open prices remain distinct",
  );
  record(
    "Long offer forms retain reordered positions and notes after network failure and cancelled navigation",
  );
  record(
    "Shared offer assessment editing distinguishes zero from unknown, retains invalid drafts and restores focus",
  );
}

async function fillLeasingTerms(page) {
  await newVersionLink(page).click();
  assert.equal(await page.locator("#offer-currency").inputValue(), "EUR");
  assert.equal(await page.locator("#offer-currency").isEditable(), false);
  await page.locator("#offer-Provider").fill("Anbieter Nord");
  await page.locator("#offer-Lender").fill("Finanzierungspartner");
  await page.locator("#offer-Intermediary").fill("Vermittlungsplattform");
  await page.locator("#offer-kind").selectOption("Leasing");
  await page.locator("#offer-commitment").selectOption("NonBinding");

  // "Valid until" precedes the issue date here; it is corrected after the rejected save.
  const terms = {
    TermMonths: "24",
    AnnualInterestPercent: "2.75",
    MonthlyPayment: "640",
    DownPayment: "0",
    ResidualPayment: "14500",
    IncludedKilometresPerYear: "12000",
    ExcessKilometrePrice: "0.18",
    UnusedKilometreCredit: "0",
    IssuedOn: "2026-10-01",
    ValidUntil: "2026-09-01",
    DeliveryNotes: "Nach Absprache",
  };
  for (const [name, value] of Object.entries(terms)) {
    await page.locator(`#offer-${name}`).fill(value);
  }
}

async function arrangePriceLines(page) {
  await addOfferLine(page, "Übergabe", "Fee", "Amount", "275");
  const benefit = await addOfferLine(page, "Servicepaket", "Benefit", "Included");
  await benefit.getByLabel("Wirkung", { exact: true }).selectOption("CostSaving");
  await benefit.getByLabel("Kostenbereich").selectOption("Service");
  await benefit.getByLabel("Dauer in Monaten").fill("24");
  await addOfferLine(page, "Zubehör", "Accessory", "Unknown");
  const temporary = await addOfferLine(page, "Entfernen", "Equipment", "Included");
  await temporary.getByRole("button", { name: "Position entfernen", exact: true }).click();

  // Moving a line keeps the focus on its button at the new place.
  await moveLineUpButton(offerLines(page).last()).click();
  assert.equal(
    await moveLineUpButton(offerLines(page).nth(1)).evaluate(
      (element) => element === document.activeElement,
    ),
    true,
  );
}

async function checkDraftSurvivesFailedSaves(page) {
  await page.locator("#offer-change-note").fill("Angaben vom Anbieter ergänzt.");

  // Rejected by validation.
  const invalid = waitForPostToCurrentPage(page);
  await page.locator("#save-offer").click();
  assert.equal((await invalid).status(), 422);
  assert.equal(await page.locator("#offer-AnnualInterestPercent").inputValue(), "2.75");
  assert.equal(await offerLines(page).count(), 3);
  assert.equal(await lineName(offerLines(page).nth(1)).inputValue(), "Zubehör");
  await page.locator("#offer-ValidUntil").fill("2026-11-01");

  // Lost on the way to the server.
  await page.route(OFFER_EDIT_REQUESTS, (route) =>
    route.request().method() === "POST" ? route.abort("failed") : route.continue(),
  );
  await page.locator("#save-offer").click();
  await page
    .locator(".editor-error")
    .getByText(/nicht gespeichert/)
    .waitFor();
  assert.equal(await lineName(offerLines(page).nth(1)).inputValue(), "Zubehör");
  assert.equal(
    await page.locator("#offer-change-note").inputValue(),
    "Angaben vom Anbieter ergänzt.",
  );

  // Leaving the form, with the discard prompt declined.
  await page.getByRole("link", { name: "Abbrechen", exact: true }).click();
  await page.locator("[data-confirm-cancel]").click();
  assert.equal(await page.locator("#offer-ValidUntil").inputValue(), "2026-11-01");
  await page.unroute(OFFER_EDIT_REQUESTS);
}

async function checkTotalsWhileTyping(page) {
  // The price list adds up while typing: fees outside the leasing are due at handover, discounts reduce the total.
  const totals = page.locator("[data-offer-totals]");
  assert.match(await totals.locator('[data-total="total"]').textContent(), /^275,00\sEUR$/);
  const fee = offerLines(page).first();
  assert.equal(await fee.getByLabel("Im Leasing").isChecked(), true);
  await fee.getByLabel("Im Leasing").uncheck();
  await addOfferLine(page, "Angebotsauto Touring", "Vehicle", "Amount", "27800");
  const discount = await addOfferLine(page, "Flottenrabatt", "Discount", "Amount", "500");
  assert.equal(await benefitValue(discount).isVisible(), false);
  await benefitValue(offerLines(page).nth(2)).fill("900");
  assert.match(await totals.locator('[data-total="total"]').textContent(), /^27\.575,00\sEUR$/);
  assert.match(await totals.locator('[data-total="financed"]').textContent(), /^27\.300,00\sEUR$/);
  assert.match(await totals.locator('[data-total="handover"]').textContent(), /^275,00\sEUR$/);
  assert.match(
    await totals.locator('[data-total="concession"]').textContent(),
    /^1\.400,00\sEUR · 5 %$/,
  );

  // A purchase has no leasing basis; switching back restores the earlier choice.
  await page.locator("#offer-kind").selectOption("Purchase");
  assert.equal(await fee.getByLabel("Im Leasing").isVisible(), false);
  assert.equal(await totals.locator("[data-total-contract]").first().isVisible(), false);
  await page.locator("#offer-kind").selectOption("Leasing");
  assert.equal(await fee.getByLabel("Im Leasing").isChecked(), false);
}

async function checkSavedPriceList({ page, record }) {
  // Seller, lender and intermediary are separate roles and read as one line.
  assert.equal(
    (await page.locator(".offer-provider").innerText()).trim(),
    "Anbieter Nord · Finanzierung Finanzierungspartner · via Vermittlungsplattform",
  );
  const price = (key) =>
    page.locator(`.offer-positions [data-offer-price="${key}"] dd`).textContent();
  assert.match(await price("Vehicle"), /27\.800,00 EUR/);
  assert.match(await price("Extras"), /275,00 EUR/);
  assert.match(await price("Discounts"), /−500,00 EUR/);
  assert.match(await price("Total"), /27\.575,00 EUR/);
  assert.match(await price("Financed"), /27\.300,00 EUR/);
  assert.match(await price("DueAtHandover"), /275,00 EUR/);
  assert.match(await price("BenefitValue"), /900,00 EUR/);
  assert.match(await price("Concession"), /1\.400,00 EUR\s+· 5 %/);
  assert.match(
    await page.locator("[data-offer-open-lines]").textContent(),
    /Positionen ohne Preis: 1/,
  );

  // The instalment does not follow from price, residual, term and interest here, and the offer says so.
  assert.equal(
    await page.locator("[data-offer-instalment-check]").getAttribute("data-offer-instalment-check"),
    "differs",
  );
  assert.match(
    await page.locator("[data-offer-difference]").textContent(),
    /27\.000,00 EUR.*575,00 EUR/,
  );
  assert.equal(await page.locator(".offer-positions [data-offer-line-handover]").count(), 1);
  assert.match(
    await page.locator('.offer-positions [data-offer-line-kind="Discount"]').textContent(),
    /−500,00 EUR/,
  );
  assert.equal(await page.locator(".offer-missing").getByText("Preis", { exact: true }).count(), 0);

  record(
    "The price list sums vehicle, equipment, fees and discounts, separates the leasing basis from the handover payment and shows the concession",
  );

  assert.match(
    await page.locator('.offer-value-groups [data-offer-field="DownPayment"]').textContent(),
    /0,00\s+EUR/,
  );
  assert.match(
    await page.locator('.offer-positions [data-offer-line-kind="Benefit"]').textContent(),
    /Inklusive/,
  );
  assert.match(
    await page.locator('.offer-positions [data-offer-line-kind="Accessory"]').textContent(),
    /Preis offen/,
  );
}

async function checkAssessmentEditing({ page, firstPath, booleanId, measurementId }) {
  const boolean = offerAssessment(page, booleanId);
  const measurement = offerAssessment(page, measurementId);

  await boolean.locator("[data-editor-open]").click();
  await assessmentAnswer(boolean).selectOption("true");
  await submitAssessment(page, boolean);

  // Zero is a value; an emptied field is unknown again.
  await measurement.locator("[data-editor-open]").click();
  await assessmentAnswer(measurement).fill("0");
  await submitAssessment(page, measurement);
  assert.equal(await measurement.getAttribute("data-result"), "Met");
  await measurement.locator("[data-editor-open]").click();
  await assessmentAnswer(measurement).fill("");
  await submitAssessment(page, measurement);
  assert.equal(await measurement.getAttribute("data-result"), "Unknown");

  // A forged source is rejected and the draft stays in the editor.
  await measurement.locator("[data-editor-open]").click();
  await assessmentAnswer(measurement).fill("2.75");
  await measurement.locator("select[name=source]").selectOption("Offer");
  await measurement.locator("textarea[name=notes]").fill("Schriftliche Zinsangabe");
  const invalidValue = waitForPostTo(page, firstPath);
  await measurement.locator("select[name=source]").evaluate((select) => {
    select.add(new Option("Invalid", "Invalid"));
    select.value = "Invalid";
  });
  await assessmentSubmitButton(measurement).click();
  assert.equal((await invalidValue).status(), 422);
  await waitForHtmxToSettle(page);
  assert.equal(await assessmentAnswer(measurement).inputValue(), "2.75");
  assert.equal(
    await measurement.locator("textarea[name=notes]").inputValue(),
    "Schriftliche Zinsangabe",
  );

  await measurement.locator("select[name=source]").selectOption("Offer");
  await submitAssessment(page, measurement);
  await page.waitForFunction(
    (id) => document.activeElement?.id === `open-offer-assessment-${id}`,
    measurementId,
  );
  assert.equal(await measurement.getAttribute("data-result"), "Met");
}

// ---------------------------------------------------------------------------------------------
// Third version: negotiation, stale assessments and immutable history
// ---------------------------------------------------------------------------------------------

async function checkNegotiatedVersion({
  page,
  origin,
  output,
  record,
  firstPath,
  booleanId,
  measurementId,
}) {
  const boolean = offerAssessment(page, booleanId);
  const measurement = offerAssessment(page, measurementId);

  // An untouched form closes without asking.
  await newVersionLink(page).click();
  // Dependent line controls are disabled before the initial draft snapshot.
  await page.locator("#offer-page-editor[data-initialized=true]").waitFor();
  await page.keyboard.press("Escape");
  await page.locator("#offer-workspace").waitFor();
  assert.equal(await page.locator("#action-confirmation").isVisible(), false);

  await newVersionLink(page).click();
  await page.locator("#offer-MonthlyPayment").fill("610");
  await page.locator("#offer-commitment").selectOption("Binding");
  await offerLines(page).first().getByLabel("Preisangabe").selectOption("Included");
  await page.locator("#offer-change-note").fill("Rate und Übergabe nachverhandelt.");
  await page.locator("#save-offer").click();
  await waitForOfferVersion(page, 3);

  // Answers given for the previous version no longer count until they are confirmed.
  assert.equal(await boolean.getAttribute("data-stale"), "true");
  assert.equal(await boolean.getAttribute("data-result"), "Unknown");
  assert.equal(await assessmentAnswer(boolean).inputValue(), "true");
  assert.match(
    await page.locator('[data-offer-change="MonthlyPayment"]').textContent(),
    /640,00.*610,00/s,
  );
  await boolean.locator("[data-editor-open]").click();
  await awaitOfferChange(page, () =>
    boolean.getByRole("button", { name: "Für aktuelle Version bestätigen" }).click(),
  );
  assert.equal(await boolean.getAttribute("data-result"), "Met");
  assert.equal(await measurement.getAttribute("data-stale"), "true");

  // An earlier version is read-only.
  await page
    .locator(".offer-history")
    .getByRole("link", { name: "Version 2", exact: true })
    .click();
  await waitForOfferVersion(page, 2);
  assert.match(
    await page.locator('.offer-value-groups [data-offer-field="MonthlyPayment"]').textContent(),
    /640,00\s+EUR/,
  );
  assert.equal(await page.locator("[data-offer-assessment]").count(), 0);
  assert.equal(await newVersionLink(page).count(), 0);

  await page.goto(`${origin}${firstPath}`);
  await page.evaluate(() => scrollTo(0, 0));
  await page.screenshot({ path: join(output, "15-offer-history-desktop.png"), fullPage: true });

  record(
    "Negotiation appends immutable versions and differences; assessments require confirmation for a new version",
  );
}

// ---------------------------------------------------------------------------------------------
// Second offer and the comparison
// ---------------------------------------------------------------------------------------------

async function createSecondOffer({ page, origin, post, casePath, candidateId }) {
  await page.goto(`${origin}${casePath}/offers/edit?candidateId=${candidateId}`);
  assert.equal(await page.locator("#offer-currency").inputValue(), "EUR");
  const badCurrency = await post(page, `${casePath}/offers/edit`, {
    Version: await page.locator("[name=Version]").inputValue(),
    "Input.CandidateId": candidateId,
    "Input.Name": "Blocked currency",
    "Input.Currency": "USD",
  });
  assert.equal(badCurrency.status(), 422);

  await page.locator("#offer-Name").fill("Anbieter Süd");
  await page.locator("#offer-Provider").fill("Anbieter Süd");
  await page.locator("#offer-kind").selectOption("Leasing");
  await page.locator("#offer-MonthlyPayment").fill("625");
  await page.locator("#save-offer").click();
  await page.locator("#offer-workspace").waitFor();
  return new URL(page.url()).pathname.split("/").at(-1);
}

// Fetches the current page again for another browser language. The request runs inside the
// page, so it reaches the application the same way the browser does.
async function fetchCurrentPageIn(page, acceptLanguage) {
  return page.evaluate(async (language) => {
    const response = await fetch(location.href, {
      headers: { "Accept-Language": language },
      credentials: "same-origin",
    });
    return response.text();
  }, acceptLanguage);
}

async function checkComparedValuesAndNumberFormats(context) {
  const { page, firstId, secondId } = context;

  await page.goto(pairComparisonUrl(context));
  const comparedTotal = page.locator('[data-offer-comparison-price="Total"]');
  assert.equal(
    (await comparedTotal.locator(`[data-offer-cell="${firstId}"]`).textContent()).trim(),
    "27.300,00",
  );

  // Numbers follow a German-language browser region; other languages keep the default format.
  const swiss = await fetchCurrentPageIn(page, "de-CH,de;q=0.9");
  assert.match(swiss, /<html lang="de-CH">/);
  assert.match(swiss, /27(’|&#x2019;|'|&#x27;)300\.00/);
  assert.doesNotMatch(swiss, /27\.300,00/);
  const french = await fetchCurrentPageIn(page, "fr-CH");
  assert.match(french, /<html lang="de">/);
  assert.match(french, /27\.300,00/);

  assert.equal(
    (await comparedTotal.locator(`[data-offer-cell="${secondId}"]`).textContent()).trim(),
    "Offen",
  );
  assert.match(
    await page
      .locator(`[data-offer-comparison-price="Concession"] [data-offer-cell="${firstId}"]`)
      .textContent(),
    /1\.400,00\s+· 5 %/,
  );
  const monthly = page.locator('[data-offer-comparison-field="MonthlyPayment"]');
  assert.equal(
    (await monthly.locator(`[data-offer-cell="${firstId}"]`).textContent()).trim(),
    "610,00",
  );
  assert.equal(
    (await monthly.locator(`[data-offer-cell="${secondId}"]`).textContent()).trim(),
    "625,00",
  );
}

async function checkOverviewSelectionAndComparison({
  page,
  origin,
  output,
  browserRequest,
  record,
  casePath,
  firstId,
  secondId,
}) {
  // Offers are marked in the overview and compared side by side; the comparison adds and removes offers.
  assert.deepEqual(await comparedOfferIds(page), [firstId, secondId]);
  assert.equal(await page.locator(".comparison-picker, .comparison-introduction").count(), 0);

  await page.goto(`${origin}${casePath}/offers`);
  assert.equal(await page.locator(".offer-card-actions").count(), 0);
  assert.equal(await compareButtonLabel(page), "Vergleichen (0/5)");
  const firstOfferCard = page.locator(`[data-offer-id="${firstId}"]`);
  assert.ok((await firstOfferCard.locator(".fulfillment-counts > li").count()) > 0);
  const offerChip = firstOfferCard.locator(".summary-chip").first();
  await offerChip.hover();
  assert.equal(await firstOfferCard.locator(".summary-popover").first().isVisible(), true);
  assert.equal(
    await firstOfferCard.locator(".summary-popover").first().locator("ol li").count(),
    Number((await offerChip.innerText()).match(/\d+/)[0]),
  );
  await page.mouse.move(5, 5);

  await page.locator(`#compare-${secondId}`).check();
  await page.locator(`#compare-${firstId}`).check();
  assert.equal(await compareButtonLabel(page), "Vergleichen (2/5)");
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: join(output, "16-offer-overview.png"), fullPage: true });
  await page.locator("#compare-selected").click();
  await page.waitForURL(/\/offers\/compare\?/);
  assert.deepEqual((await comparedOfferIds(page)).sort(), [firstId, secondId].sort());

  await page.locator(`[data-offer-column="${secondId}"] .comparison-remove`).click();
  assert.deepEqual(await comparedOfferIds(page), [firstId]);
  assert.equal(await page.locator(".comparison-remove").count(), 0);
  await page.locator("#compare-add").selectOption(secondId);
  await page.locator("#compare-add-submit").click();
  assert.deepEqual(await comparedOfferIds(page), [firstId, secondId]);

  // Repeated and unknown selections degrade instead of failing.
  await page.goto(
    `${origin}${casePath}/offers/compare?first=${firstId}&second=${firstId}&more=${firstId}&more=${UNKNOWN_OFFER_ID}`,
  );
  assert.deepEqual(await comparedOfferIds(page), [firstId]);
  assert.equal(
    (await browserRequest(page, `${casePath}/offers/compare?first=${UNKNOWN_OFFER_ID}`)).status(),
    404,
  );

  record(
    "Offers are selected in the overview, summarised by state, and added to or removed from the comparison",
  );
}

/** Leaves the page at phone size; the ordering check that follows resets it. */
async function checkPhoneComparison(context) {
  const { page, output, record, firstId, secondId } = context;
  const secondOfferLink = page.locator(
    `[data-offer-column="${secondId}"] .comparison-candidate-link`,
  );

  await page.goto(pairComparisonUrl(context));
  await page.screenshot({ path: join(output, "16-offer-comparison-desktop.png"), fullPage: true });
  await page.setViewportSize(PHONE_VIEWPORT);
  assert.ok(await fitsViewportWidth(page));
  await page.screenshot({ path: join(output, "17-offer-comparison-mobile.png"), fullPage: true });

  // Opening an offer from the comparison carries the compared pair along.
  await secondOfferLink.click();
  assert.equal(comparisonParameter(page, "first"), firstId);
  assert.equal(comparisonParameter(page, "second"), secondId);
  await newVersionLink(page).click();
  await page.locator("#offer-Notes").fill("Mobile Ergänzung.");
  assert.ok(await fitsViewportWidth(page));
  await page.locator("#save-offer").click();
  await waitForOfferVersion(page, 2);
  await page.getByRole("link", { name: "Offerten vergleichen", exact: true }).click();
  assert.deepEqual(await comparedOfferIds(page), [firstId, secondId]);
  await page.waitForFunction(
    () => document.querySelector("#offer-comparison-scroll").scrollLeft > 50,
  );
  assert.equal(
    await page
      .locator(".vehicle-table thead th")
      .first()
      .evaluate((element) => getComputedStyle(element).position),
    "sticky",
  );
  await secondOfferLink.click();

  record(
    "Offer comparison keeps the selected pair and restores the second mobile column after editing",
  );

  await page.getByRole("link", { name: "Dokument hinzufügen", exact: true }).click();
  assert.equal(comparisonParameter(page, "first"), firstId);
  assert.equal(comparisonParameter(page, "second"), secondId);
  await page.getByRole("link", { name: "Abbrechen", exact: true }).click();
  await page.locator("#offer-workspace").waitFor();
  assert.equal(comparisonParameter(page, "second"), secondId);
}

// ---------------------------------------------------------------------------------------------
// Price lines that fulfil a requirement
// ---------------------------------------------------------------------------------------------

async function checkLineFulfilsRequirement({
  page,
  post,
  record,
  casePath,
  secondId,
  measurementId,
  wheelsId,
}) {
  // A line that names a requirement fulfils it; an own answer still wins.
  await newVersionLink(page).click();
  const wheelsLine = await addOfferLine(page, "Winterkompletträder", "Benefit", "Included");
  await benefitValue(wheelsLine).fill("1800");
  await wheelsLine.getByLabel("Erfüllt Anforderung", { exact: false }).selectOption(wheelsId);
  await page.locator("#save-offer").click();
  await waitForOfferVersion(page, 3);

  const wheels = offerAssessment(page, wheelsId);
  assert.equal(await wheels.getAttribute("data-result"), "Met");
  assert.match(await wheels.textContent(), /Erfüllt durch «Winterkompletträder»/);
  assert.match(
    await page.locator(".offer-positions .offer-line-criterion").textContent(),
    /Winterräder/,
  );
  assert.match(
    await page.locator('.offer-positions [data-offer-price="BenefitValue"] dd').textContent(),
    /1\.800,00 EUR/,
  );

  await wheels.locator("[data-editor-open]").click();
  await assessmentAnswer(wheels).selectOption("false");
  await submitAssessment(page, wheels);
  assert.equal(await offerAssessment(page, wheelsId).getAttribute("data-result"), "Unmet");
  assert.equal(await page.locator("#offer-archive-toggle").count(), 0);

  // Only yes/no requirements can be named by a line.
  assert.equal(
    (
      await post(page, `${casePath}/offers/edit/${secondId}`, {
        Version: await page.locator("#offer-workspace").getAttribute("data-version"),
        "Input.Name": "Fremde Anforderung",
        "Input.Lines.Index": "0",
        "Input.Lines[0].Name": "Zins",
        "Input.Lines[0].PriceKind": "Unknown",
        "Input.Lines[0].CriterionId": measurementId,
      })
    ).status(),
    422,
  );

  record(
    "A price line fulfils the requirement it names; measurements cannot be linked and an own answer overrides the line",
  );
}

// ---------------------------------------------------------------------------------------------
// Manual order and deletion
// ---------------------------------------------------------------------------------------------

async function checkOrderingAndDeletion({
  page,
  origin,
  browserRequest,
  record,
  casePath,
  candidateId,
  firstId,
  secondId,
}) {
  // Offers keep a manual order and are deleted from their record, like cars.
  await page.goto(`${origin}${casePath}/offers`);
  assert.deepEqual(await offerOrder(page), [firstId, secondId]);
  assert.equal(await page.getByLabel("Archivierte Offerten anzeigen").count(), 0);
  assert.equal(await page.locator(`#move-offer-${firstId}--1`).isDisabled(), true);
  await page.locator(`#move-offer-${secondId}--1`).click();
  await page.waitForFunction(
    (id) => document.querySelector(".offer-card")?.dataset.offerId === id,
    secondId,
  );
  assert.deepEqual(await offerOrder(page), [secondId, firstId]);

  await page.goto(`${origin}${casePath}/offers/edit?candidateId=${candidateId}`);
  await page.locator("#offer-Name").fill("Wegwerfofferte");
  await page.locator("#save-offer").click();
  await page.locator("#offer-workspace").waitFor();
  const thirdPath = new URL(page.url()).pathname;
  await page.locator("#remove-offer").click();
  assert.match(await page.locator("#action-confirmation").textContent(), /Wegwerfofferte/);
  await page.locator("#action-confirmation [data-confirm-cancel]").click();
  assert.equal(new URL(page.url()).pathname, thirdPath);
  await page.locator("#remove-offer").click();
  await page.locator("#action-confirmation [data-confirm-accept]").click();
  await page.waitForURL(`${origin}${casePath}/offers`);
  assert.deepEqual(await offerOrder(page), [secondId, firstId]);
  assert.equal((await browserRequest(page, thirdPath)).status(), 404);
  await page.setViewportSize(DESKTOP_VIEWPORT);

  record(
    "Offers are reordered in the overview and deleted from their record after confirmation; nothing is archived",
  );
}

// ---------------------------------------------------------------------------------------------
// Readers, editors, concurrent edits and revoked access
// ---------------------------------------------------------------------------------------------

async function checkReaderCannotWrite(context) {
  const { reader, origin, post, browserRequest, casePath, firstPath, firstId, booleanId } = context;

  const ownerVersion = await evaluationVersion(context);
  assert.equal((await shareEvaluation(context, "Reader", ownerVersion)).status(), 200);

  await reader.page.goto(`${origin}${firstPath}`);
  assert.equal(await reader.page.locator("[data-offer-version]").count(), 3);
  assert.equal(await reader.page.locator(".value-editor").count(), 0);
  assert.equal(
    (await browserRequest(reader.page, `${casePath}/offers/edit/${firstId}`)).status(),
    403,
  );

  await reader.page.goto(`${origin}${firstPath}`);
  const version = await reader.page.locator("#offer-workspace").getAttribute("data-version");
  for (const action of ["remove-offer", "archive-offer", "assess-offer", "clear-assessment"]) {
    assert.equal(
      (
        await post(reader.page, firstPath, {
          action,
          version,
          criterionId: booleanId,
          booleanValue: "false",
        })
      ).status(),
      403,
    );
  }
  assert.equal(
    (
      await post(reader.page, `${casePath}/offers/edit/${firstId}`, {
        Version: version,
        "Input.Name": "Forbidden edit",
      })
    ).status(),
    403,
  );
  assert.equal(
    (
      await post(reader.page, `${casePath}/offers`, {
        action: "move-offer",
        itemId: firstId,
        direction: "-1",
        version,
      })
    ).status(),
    403,
  );
  assert.equal(await reader.page.locator("#remove-offer").count(), 0);
}

async function checkEditorConflict(context) {
  const {
    page,
    reader,
    origin,
    post,
    browserRequest,
    record,
    casePath,
    firstPath,
    firstId,
    booleanId,
    measurementId,
  } = context;
  const editPath = `${casePath}/offers/edit/${firstId}`;

  const ownerVersion = await evaluationVersion(context);
  assert.equal((await shareEvaluation(context, "Editor", ownerVersion)).status(), 200);

  await reader.page.goto(`${origin}${firstPath}`);
  const readerBoolean = offerAssessment(reader.page, booleanId);
  await readerBoolean.locator("[data-editor-open]").click();
  await assessmentAnswer(readerBoolean).selectOption("false");
  const assessed = waitForPostTo(reader.page, firstPath);
  await assessmentSubmitButton(readerBoolean).click();
  assert.equal((await assessed).status(), 200);
  await waitForHtmxToSettle(reader.page);

  // The owner opens the form; the editor then writes, which makes the owner's form stale.
  await page.goto(`${origin}${editPath}`);
  const staleVersion = await page.locator("[name=Version]").inputValue();
  assert.equal(
    (
      await post(reader.page, firstPath, {
        action: "assess-offer",
        version: staleVersion,
        criterionId: measurementId,
        measurementValue: "2.75",
      })
    ).status(),
    200,
  );
  await page.locator("#offer-MonthlyPayment").fill("599");
  const conflict = waitForPostToCurrentPage(page);
  await page.locator("#save-offer").click();
  assert.equal((await conflict).status(), 409);
  assert.equal(await page.locator("#offer-MonthlyPayment").inputValue(), "599");
  assert.equal(await page.locator("[name=Version]").inputValue(), staleVersion);

  // A post without the anti-forgery token is refused outright.
  assert.equal(
    (
      await browserRequest(page, editPath, {
        Version: staleVersion,
        "Input.Name": "No token",
      })
    ).status(),
    400,
  );
  assert.equal(await page.locator(".editor-error a[target=_blank]").count(), 1);
  await page.getByRole("button", { name: "Stand geprüft – erneut speichern", exact: true }).click();
  await waitForOfferVersion(page, 4);
  assert.match(await page.locator('[data-offer-field="MonthlyPayment"]').textContent(), /599,00/);
  await page
    .locator(".offer-history")
    .getByRole("link", { name: "Version 3", exact: true })
    .click();
  assert.match(await page.locator('[data-offer-field="MonthlyPayment"]').textContent(), /610,00/);

  record(
    "An explicit conflict retry creates a new offer version and leaves the previous version immutable",
  );
}

async function checkRevokedEditorKeepsDraft(context) {
  const { page, reader, origin, post, browserRequest, record, casePath, firstPath, firstId } =
    context;

  await reader.page.goto(`${origin}${casePath}/offers/edit/${firstId}`);
  await reader.page.locator("#offer-Notes").fill("Local draft after permission loss");

  const version = await evaluationVersion(context);
  assert.equal(
    (
      await post(page, casePath, {
        action: "revoke",
        userId: await page.locator(".share-row form input[name=userId]").inputValue(),
        version,
      })
    ).status(),
    200,
  );
  assert.equal((await browserRequest(reader.page, `${firstPath}?revision=1`)).status(), 403);

  await reader.page.locator("#save-offer").click();
  await reader.page
    .locator(".editor-error")
    .getByText(/nicht mehr bearbeiten/)
    .waitFor();
  assert.equal(
    await reader.page.locator("#offer-Notes").inputValue(),
    "Local draft after permission loss",
  );
  assert.equal(await reader.page.locator("#save-offer").isDisabled(), true);

  record(
    "Revoked editing permission keeps the long offer draft visible and prevents further saves",
  );
}

// ---------------------------------------------------------------------------------------------
// Export and import
// ---------------------------------------------------------------------------------------------

async function checkExportedOffers({
  page,
  browserRequest,
  record,
  casePath,
  firstId,
  secondId,
  wheelsId,
}) {
  const exported = await exportEvaluation(page, browserRequest, casePath);
  assert.equal(exported.version, 9);
  assert.equal(exported.currency, "EUR");
  assert.equal(exported.offers.length, 2);
  assert.equal(exported.offers.find((offer) => offer.id === firstId).revisions.length, 4);
  assert.deepEqual(
    exported.offers.map((offer) => offer.id),
    [secondId, firstId],
  );
  assert.equal(exported.offers[0].archived, undefined);
  assert.equal(exported.offers[0].revisions.at(-1).lines.at(-1).criterionId, wheelsId);
  assert.equal(
    exported.offers[1].revisions[1].lines.find((line) => line.kind === "Fee").financed,
    false,
  );

  record(
    "Offer access checks cover history and direct writes; concurrency preserves drafts and revocation is immediate",
  );
  return exported;
}

async function checkImportedOffers({ page, origin, browserRequest, record, exported }) {
  await page.goto(origin);
  await page.getByRole("button", { name: "Evaluation importieren", exact: true }).click();
  await page.getByLabel("Exportdatei (JSON oder ZIP)").setInputFiles({
    name: "offers.json",
    mimeType: "application/json",
    buffer: Buffer.from(JSON.stringify(exported)),
  });
  await page.getByRole("button", { name: "Als neue Evaluation importieren", exact: true }).click();
  await page.waitForURL(/\/evaluations\//);

  const imported = await exportEvaluation(page, browserRequest, new URL(page.url()).pathname);
  assert.equal(imported.currency, "EUR");
  assert.equal(imported.offers.length, 2);
  assert.ok(
    imported.offers.every((offer) => !exported.offers.some((original) => original.id === offer.id)),
  );
  assert.equal(
    imported.offers.find((offer) => offer.revisions[0].terms.name === "Anbieter Nord").revisions
      .length,
    4,
  );
  assert.equal(imported.offerAssessments.length, 3);
  assert.deepEqual(
    imported.offers.map((offer) => offer.revisions[0].terms.name),
    ["Anbieter Süd", "Anbieter Nord"],
  );
  assert.equal(
    imported.offers[0].revisions.at(-1).lines.at(-1).criterionId,
    imported.criteria.find((criterion) => criterion.name === "Winterräder").id,
  );
  assert.equal(await page.locator(".share-row").count(), 0);

  record(
    "JSON import keeps offer histories, positions, currency and assessments with independent IDs and private access",
  );
}
