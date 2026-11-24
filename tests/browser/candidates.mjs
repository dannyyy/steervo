import assert from "node:assert/strict";
import { checkCanvas } from "./canvas.mjs";
import { join } from "node:path";
import {
  fillSpecifications,
  checkSpecificationValidation,
  editCopiedSpecifications,
  checkSpecificationsComparison,
} from "./specifications.mjs";

const DESKTOP_VIEWPORT = { width: 1440, height: 1050 };
const PHONE_VIEWPORT = { width: 390, height: 844 };
const UNKNOWN_CANDIDATE_ID = "00000000-0000-0000-0000-000000000001";
const CANDIDATE_TOTAL = 8;
const COMPARISON_LIMIT = 5;

/**
 * Walks through the car records of the evaluation that is currently open: creating, assessing,
 * copying, comparing and sharing them. The steps build on each other and must run in this order.
 */
export async function checkCandidates({
  page,
  reader,
  origin,
  output,
  post,
  browserRequest,
  record,
}) {
  const casePath = new URL(page.url()).pathname;
  const data = await exportEvaluation(page, browserRequest, casePath);
  const context = {
    page,
    reader,
    origin,
    output,
    post,
    browserRequest,
    record,
    casePath,
    booleanCriterion: data.criteria.find((criterion) => criterion.type === "Boolean").id,
    measurementCriterion: data.criteria.find((criterion) => criterion.type === "Measurement").id,
    ratingCriterion: data.criteria.find((criterion) => criterion.type === "Rating").id,
  };

  Object.assign(context, await createFirstCandidate(context));
  await checkRecordLayout(context);
  await correctSpecificationsAndNotes(context);
  await checkDistinctAssessmentValues(context);
  await checkUnsavedObservationIsProtected(context);
  await checkExclusionRequiresReason(context);

  context.secondId = await copyCandidateAndAssessIndependently(context);
  await checkPairComparison(context);

  await checkCanvas({
    page,
    origin,
    casePath,
    firstId: context.firstId,
    secondId: context.secondId,
    boolean: context.booleanCriterion,
    rating: context.ratingCriterion,
    output,
    record,
  });

  await fillUpToEightCandidates(context);
  context.cardIds = await checkOverviewSelection(context);
  await checkComparisonOfFiveCars(context);
  await checkPhoneLayout(context);
  await checkReaderAndEditorRoles(context);
  await checkExportedObservations(context);

  await page.goto(`${origin}${casePath}`);
  return {
    firstId: context.firstId,
    boolean: context.booleanCriterion,
    rating: context.ratingCriterion,
  };
}

// ---------------------------------------------------------------------------------------------
// Shared interactions
// ---------------------------------------------------------------------------------------------

async function exportEvaluation(page, browserRequest, casePath) {
  return JSON.parse(await (await browserRequest(page, `${casePath}?handler=Export`)).text());
}

/** Runs a write and waits until the candidate workspace has re-rendered with a new version. */
async function awaitWorkspaceChange(target, operation) {
  const version = await candidateWorkspaceVersion(target);
  await operation();
  await target.waitForFunction((previous) => {
    const workspace = document.querySelector("#candidate-workspace");
    return workspace && workspace.dataset.version !== previous;
  }, version);
  await target.waitForFunction(() => !document.querySelector(".htmx-settling, .htmx-request"));
}

function candidateWorkspaceVersion(target) {
  return target.locator("#candidate-workspace").getAttribute("data-version");
}

function evaluationWorkspaceVersion(target) {
  return target.locator("#evaluation-workspace").getAttribute("data-version");
}

function assessmentCard(target, criterionId) {
  return target.locator(`article[data-assessment-criterion="${criterionId}"]`);
}

function assessmentValue(target, criterionId) {
  return assessmentCard(target, criterionId).getByLabel("Deine Angabe", { exact: true });
}

function observationNote(target, criterionId) {
  return assessmentCard(target, criterionId).getByLabel("Was hast du festgestellt?");
}

function saveAssessmentButton(target, criterionId) {
  return assessmentCard(target, criterionId).getByRole("button", { name: /speichern$/ });
}

async function saveAssessment(target, criterionId) {
  await awaitWorkspaceChange(target, () => saveAssessmentButton(target, criterionId).click());
}

async function openAssessmentEditor(target, criterionId) {
  if (!(await target.locator("#record-assessments").isVisible())) {
    await target.locator("#tab-assessments").click();
  }

  if (!(await assessmentCard(target, criterionId).locator("[data-editor]").isVisible())) {
    await assessmentCard(target, criterionId).locator("[data-editor-open]").click();
  }
}

async function openClassificationEditor(target) {
  if (!(await target.locator("#candidate-classification").isVisible())) {
    await target.locator("#edit-classification").click();
  }
}

function classificationSelect(page) {
  return page.getByLabel("Einstufung", { exact: true });
}

function saveClassificationButton(page) {
  return page
    .locator("#candidate-classification")
    .getByRole("button", { name: "Speichern", exact: true });
}

function saveDetailsButton(details) {
  return details.getByRole("button", { name: "Speichern", exact: true });
}

function moveUpButton(page, candidateId) {
  return page
    .locator(`[data-candidate-id="${candidateId}"]`)
    .getByRole("button", { name: /nach oben$/ });
}

/** Candidate ids of the comparison columns, from left to right. */
function comparedCandidateIds(page) {
  return page
    .locator("[data-comparison-column]")
    .evaluateAll((cells) => cells.map((cell) => cell.dataset.comparisonColumn));
}

async function compareButtonLabel(page) {
  return (await page.locator("#compare-selected").innerText()).trim();
}

function pairComparisonPath({ casePath, firstId, secondId }) {
  return `${casePath}/compare?first=${firstId}&second=${secondId}`;
}

function fitsViewportWidth(page) {
  return page.evaluate(() => document.documentElement.scrollWidth <= innerWidth);
}

async function captureFromTop(page, output, fileName) {
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: join(output, fileName), fullPage: true });
}

// ---------------------------------------------------------------------------------------------
// Creating and editing a single car
// ---------------------------------------------------------------------------------------------

async function createFirstCandidate({ page }) {
  await page.locator(".workflow-nav").getByRole("link", { name: "Autos", exact: true }).click();
  assert.equal(await page.locator(".candidate-card").count(), 0);

  await page.getByRole("button", { name: "Auto hinzufügen", exact: false }).click();
  const dialog = page.getByRole("dialog", { name: "Auto hinzufügen", exact: true });
  await dialog.getByLabel("Modell / Bezeichnung").fill("Nord");
  await dialog.getByLabel("Marke", { exact: false }).fill("Beispiel");
  await fillSpecifications(dialog);

  // An unsafe link is rejected without losing what was already typed.
  await dialog.getByLabel("Quellen und Links").fill("javascript:alert(1)");
  await dialog.getByRole("button", { name: "Auto anlegen", exact: false }).click();
  await dialog.locator(".editor-error").waitFor();
  assert.equal(await dialog.getByLabel("Modell / Bezeichnung").inputValue(), "Nord");
  assert.equal(await dialog.locator("#spec-AccelerationSeconds").inputValue(), "4.5");

  await dialog.getByLabel("Quellen und Links").fill("https://example.test/specification");
  await dialog.getByLabel("Notizen", { exact: false }).fill("Eine frei erfasste Beobachtung.");
  await dialog.getByRole("button", { name: "Auto anlegen", exact: false }).click();
  await page.getByRole("heading", { name: "Beispiel Nord", exact: true }).waitFor();

  const firstPath = new URL(page.url()).pathname;
  const firstId = firstPath.split("/").at(-1);
  return { firstPath, firstId };
}

async function checkRecordLayout({ page }) {
  // The record opens on its technical data; the header only compares, copies and deletes.
  assert.equal(await page.locator("#record-specifications").isVisible(), true);
  assert.equal(await page.locator("#record-assessments").isVisible(), false);
  assert.deepEqual(await page.locator("[role=tab]").allTextContents(), [
    "Technische Daten",
    "Anforderungen",
  ]);
  assert.deepEqual(
    (await page.locator(".vehicle-actions").locator("a, button").allTextContents()).map((text) =>
      text.trim(),
    ),
    ["Im Vergleich ansehen", "Auto kopieren", "Auto löschen"],
  );
  assert.equal(await page.locator("#edit-candidate, .rating-explanation").count(), 0);
}

async function correctSpecificationsAndNotes({ page }) {
  const details = page.locator("#candidate-edit");
  await page.locator("#edit-specifications").click();
  await checkSpecificationValidation(page, details);
  await details.getByLabel("Notizen", { exact: false }).fill("Aktualisierte Beobachtung.");
  await awaitWorkspaceChange(page, () => saveDetailsButton(details).click());
  assert.equal(await page.locator(".candidate-notes").textContent(), "Aktualisierte Beobachtung.");
  assert.equal(await details.isVisible(), false);
}

async function checkDistinctAssessmentValues({
  page,
  record,
  booleanCriterion,
  measurementCriterion,
  ratingCriterion,
}) {
  // Nothing recorded yet is "unknown", which differs from an explicit "no".
  assert.equal(await assessmentCard(page, booleanCriterion).getAttribute("data-result"), "Unknown");
  assert.equal(await assessmentValue(page, booleanCriterion).inputValue(), "");

  await openAssessmentEditor(page, booleanCriterion);
  await assessmentValue(page, booleanCriterion).selectOption("false");
  await assessmentCard(page, booleanCriterion)
    .getByLabel("Grundlage", { exact: true })
    .selectOption("Inspection");
  await observationNote(page, booleanCriterion).fill("Bei der Besichtigung nicht vorhanden.");
  await saveAssessment(page, booleanCriterion);
  assert.equal(await assessmentCard(page, booleanCriterion).getAttribute("data-result"), "Unmet");
  await page.locator(".candidate-warning").filter({ hasText: "Gute Sicht" }).waitFor();
  assert.equal(await classificationSelect(page).inputValue(), "Watching");

  await openAssessmentEditor(page, measurementCriterion);
  await assessmentValue(page, measurementCriterion).fill("0");
  await saveAssessment(page, measurementCriterion);
  assert.equal(await assessmentValue(page, measurementCriterion).inputValue(), "0");

  await openAssessmentEditor(page, ratingCriterion);
  await assessmentValue(page, ratingCriterion).selectOption("1");
  await saveAssessment(page, ratingCriterion);
  assert.equal(await assessmentCard(page, ratingCriterion).getAttribute("data-result"), "Rated");

  record(
    "Candidates accept incomplete details and safe links; unknown, false, zero and subjective ratings stay distinct",
  );
}

async function checkUnsavedObservationIsProtected({
  page,
  booleanCriterion,
  measurementCriterion,
}) {
  // Switching editors must never silently discard an unfinished observation.
  await openAssessmentEditor(page, measurementCriterion);
  await observationNote(page, measurementCriterion).fill("Noch nicht gespeichert");
  await assessmentCard(page, booleanCriterion).locator("[data-editor-open]").click();
  const discard = page.getByRole("dialog", { name: "Ungespeicherte Änderungen verwerfen?" });
  await discard.getByRole("button", { name: "Weiterbearbeiten" }).click();
  assert.equal(
    await observationNote(page, measurementCriterion).inputValue(),
    "Noch nicht gespeichert",
  );
  assert.equal(
    await assessmentCard(page, measurementCriterion).locator("[data-editor]").isVisible(),
    true,
  );
  await saveAssessment(page, measurementCriterion);

  await openAssessmentEditor(page, booleanCriterion);
  await assessmentValue(page, booleanCriterion).selectOption("true");
  await saveAssessment(page, booleanCriterion);
}

async function checkExclusionRequiresReason({ page, output, record, firstPath }) {
  await openClassificationEditor(page);
  await classificationSelect(page).selectOption("Excluded");

  // The listener must be registered before the click that triggers the rejected save.
  const failed = page.waitForResponse(
    (response) =>
      response.request().method() === "POST" && new URL(response.url()).pathname === firstPath,
  );
  await saveClassificationButton(page).click();
  assert.equal((await failed).status(), 422);
  await page
    .getByText("Bitte begründe, warum du dieses Auto zurückstellst oder ausscheiden lässt.", {
      exact: true,
    })
    .waitFor();
  assert.equal(await classificationSelect(page).inputValue(), "Excluded");

  await page.getByLabel("Begründung", { exact: true }).fill("Passt nicht zu meinen Anforderungen.");
  await awaitWorkspaceChange(page, () => saveClassificationButton(page).click());
  await page.reload();
  assert.equal(await classificationSelect(page).inputValue(), "Excluded");
  await captureFromTop(page, output, "10-candidate-desktop.png");

  record("Editor switching protects unsaved notes; exclusion requires a reason and persists");
}

// ---------------------------------------------------------------------------------------------
// Copying a car and comparing the pair
// ---------------------------------------------------------------------------------------------

async function copyCandidateAndAssessIndependently({
  page,
  origin,
  record,
  casePath,
  firstPath,
  firstId,
  booleanCriterion,
  measurementCriterion,
}) {
  await page.getByRole("link", { name: /Alle Autos/ }).click();
  assert.equal(
    await page.locator(".candidate-actions, .candidate-open, .candidate-optional").count(),
    0,
  );
  await page.locator(".candidate-card h3 a").first().click();
  await page.locator("#copy-candidate").click();
  await page.getByLabel("Bezeichnung der Kopie").fill("Süd");
  await page
    .getByRole("dialog", { name: "Auto kopieren", exact: true })
    .getByRole("button", { name: "Auto kopieren", exact: true })
    .click();
  await page.getByRole("heading", { name: "Beispiel Süd", exact: true }).waitFor();
  const secondId = new URL(page.url()).pathname.split("/").at(-1);

  // The copy takes over the assessments but not the exclusion.
  await editCopiedSpecifications(page, awaitWorkspaceChange);
  assert.equal(await classificationSelect(page).inputValue(), "Watching");
  assert.equal(await assessmentValue(page, booleanCriterion).inputValue(), "true");

  await openAssessmentEditor(page, measurementCriterion);
  await awaitWorkspaceChange(page, async () => {
    await assessmentCard(page, measurementCriterion)
      .getByRole("button", { name: /zurücksetzen$/ })
      .click();
    await page.locator("#action-confirmation [data-confirm-accept]").click();
  });
  assert.equal(
    await assessmentCard(page, measurementCriterion).getAttribute("data-result"),
    "Unknown",
  );
  assert.equal(await assessmentValue(page, measurementCriterion).inputValue(), "");
  assert.equal(await observationNote(page, measurementCriterion).inputValue(), "");

  await openAssessmentEditor(page, measurementCriterion);
  await assessmentValue(page, measurementCriterion).fill("0");
  await saveAssessment(page, measurementCriterion);
  await openAssessmentEditor(page, booleanCriterion);
  await assessmentValue(page, booleanCriterion).selectOption("false");
  await saveAssessment(page, booleanCriterion);

  // Changing the copy leaves the original untouched.
  await page.goto(`${origin}${firstPath}`);
  assert.equal(await assessmentValue(page, booleanCriterion).inputValue(), "true");
  await page.goto(`${origin}${pairComparisonPath({ casePath, firstId, secondId })}`);
  await checkSpecificationsComparison(page, firstId, secondId);

  record(
    "Optional specifications preserve decimal inputs and calculation sources; validation, independent copies and neutral comparison work",
  );
  return secondId;
}

async function checkPairComparison({ page, output, record, firstId, secondId, booleanCriterion }) {
  const comparison = page.locator(`[data-comparison-criterion="${booleanCriterion}"]`);
  const firstCell = comparison.locator(`[data-comparison-candidate="${firstId}"]`);
  const secondCell = comparison.locator(`[data-comparison-candidate="${secondId}"]`);
  assert.equal(await firstCell.locator(".comparison-value").textContent(), "Ja");
  assert.equal(await secondCell.locator(".comparison-value").textContent(), "Nein");

  await secondCell.locator("[data-editor-open]").click();
  const comparisonEditor = page.locator(`#editor-assessment-${secondId}-${booleanCriterion}`);
  await comparisonEditor.getByLabel("Deine Angabe", { exact: true }).selectOption("true");
  await awaitWorkspaceChange(page, () =>
    comparisonEditor.getByRole("button", { name: /speichern$/ }).click(),
  );
  assert.equal(await secondCell.locator(".comparison-value").textContent(), "Ja");
  assert.deepEqual(await comparedCandidateIds(page), [firstId, secondId]);
  await captureFromTop(page, output, "11-comparison-desktop.png");

  record(
    "Candidate copies have independent assessments; comparison edits preserve the selected pair",
  );
}

// ---------------------------------------------------------------------------------------------
// Overview with eight cars and the comparison of up to five
// ---------------------------------------------------------------------------------------------

async function fillUpToEightCandidates({ page, origin, post, casePath, secondId }) {
  // Eight candidates remain selectable on a phone without widening the document.
  await page.goto(`${origin}${casePath}/candidates`);
  let version = await candidateWorkspaceVersion(page);
  for (let index = 3; index <= CANDIDATE_TOTAL; index++) {
    const response = await post(page, `${casePath}/candidates`, {
      action: "add-candidate",
      "Input.Model": `Kandidat ${index}`,
      version,
    });
    assert.equal(response.status(), 0); // fetch's manual redirect represents a successful create as an opaque redirect.
    await page.reload();
    version = await candidateWorkspaceVersion(page);
  }
  assert.equal(await page.locator(".candidate-card").count(), CANDIDATE_TOTAL);

  await awaitWorkspaceChange(page, () => moveUpButton(page, secondId).click());
  assert.equal(
    await page.locator(".candidate-card").first().getAttribute("data-candidate-id"),
    secondId,
  );
}

/** Marks five cars in the overview and opens their comparison; returns the card ids in order. */
async function checkOverviewSelection({ page, origin, output, casePath, firstId }) {
  // Up to five cars are marked in the overview and compared side by side.
  await page.setViewportSize(DESKTOP_VIEWPORT);
  await page.goto(`${origin}${casePath}/candidates`);
  const cardIds = await page
    .locator(".candidate-card")
    .evaluateAll((cards) => cards.map((card) => card.dataset.candidateId));
  assert.equal(await page.locator(".next-step").count(), 0);
  assert.equal(
    await page
      .locator(`[data-candidate-id="${firstId}"] .candidate-identity .candidate-status`)
      .count(),
    1,
  );
  const firstCard = page.locator(`[data-candidate-id="${firstId}"]`);
  assert.ok((await firstCard.locator(".fulfillment-counts > li").count()) > 0);
  assert.equal(
    await firstCard.locator(".fulfillment-bar rect").count(),
    await firstCard.locator(".fulfillment-counts > li").count(),
  );

  await checkSummaryPopover(page, output, firstCard);

  assert.equal(await page.getByText("Wähle bis zu 5 Autos").count(), 0);
  assert.equal(await compareButtonLabel(page), "Vergleichen (0/5)");
  const sideOrder = await firstCard
    .locator(".candidate-side")
    .evaluate((side) => [...side.children].map((child) => child.className));
  assert.deepEqual(sideOrder, ["row-actions", "candidate-compare"]);

  for (const id of cardIds.slice(0, COMPARISON_LIMIT)) {
    await page.locator(`#compare-${id}`).check();
  }
  assert.equal(await compareButtonLabel(page), "Vergleichen (5/5)");
  assert.equal(await page.locator(`#compare-${cardIds[5]}`).isDisabled(), true);

  // Reordering exchanges the list; the marked cars stay marked.
  await awaitWorkspaceChange(page, () => moveUpButton(page, cardIds[7]).click());
  assert.equal(await page.locator("[data-compare-select]:checked").count(), 5);
  assert.equal(await compareButtonLabel(page), "Vergleichen (5/5)");

  // Releasing one of the five marks frees the remaining cars again.
  await page.locator(`#compare-${cardIds[4]}`).uncheck();
  assert.equal(await page.locator(`#compare-${cardIds[5]}`).isDisabled(), false);
  await page.locator(`#compare-${cardIds[4]}`).check();
  await captureFromTop(page, output, "11-overview-selection.png");

  await page.locator("#compare-selected").click();
  await page.waitForURL(/\/compare\?/);
  return cardIds;
}

async function checkSummaryPopover(page, output, card) {
  // Each count names the criteria behind it, on hover and on keyboard focus.
  const chip = card.locator(".summary-chip").first();
  const popover = card.locator(".summary-popover").first();
  assert.equal(await popover.isVisible(), false);
  await chip.hover();
  assert.equal(await popover.isVisible(), true);
  assert.equal(
    await popover.locator("ol li").count(),
    Number((await chip.innerText()).match(/\d+/)[0]),
  );
  await page.mouse.move(5, 5);
  assert.equal(await popover.isVisible(), false);

  await chip.focus();
  assert.equal(await popover.isVisible(), true);
  const popoverBox = await popover.boundingBox();
  assert.ok(popoverBox.x >= 0 && popoverBox.x + popoverBox.width <= 1440);
  await page.screenshot({ path: join(output, "11-overview-popover.png") });
  await chip.blur();
}

async function checkComparisonOfFiveCars({
  page,
  origin,
  output,
  browserRequest,
  record,
  casePath,
  cardIds,
}) {
  assert.deepEqual(await comparedCandidateIds(page), cardIds.slice(0, COMPARISON_LIMIT));
  assert.equal(await page.locator("#compare-add").count(), 0);
  assert.ok(await fitsViewportWidth(page));
  const columnWidths = await page
    .locator("[data-comparison-column]")
    .evaluateAll((cells) => cells.map((cell) => cell.getBoundingClientRect().width));
  assert.ok(Math.min(...columnWidths) >= 180, `Five compared cars stay readable: ${columnWidths}`);
  assert.equal(
    await page
      .locator("#vehicle-comparison-scroll")
      .evaluate((element) => element.scrollWidth <= element.clientWidth + 1),
    true,
  );
  await page.screenshot({ path: join(output, "11-comparison-five.png"), fullPage: true });

  // A sixth car in the address is dropped and the page says so.
  const additionalCars = cardIds
    .slice(2, 6)
    .map((id) => `&more=${id}`)
    .join("");
  await page.goto(
    `${origin}${casePath}/compare?first=${cardIds[0]}&second=${cardIds[1]}${additionalCars}`,
  );
  assert.equal(await page.locator("[data-comparison-limited]").count(), 1);
  assert.deepEqual(await comparedCandidateIds(page), cardIds.slice(0, COMPARISON_LIMIT));

  await page.locator(`[data-comparison-column="${cardIds[1]}"] .comparison-remove`).click();
  assert.deepEqual(await comparedCandidateIds(page), [
    cardIds[0],
    cardIds[2],
    cardIds[3],
    cardIds[4],
  ]);
  assert.equal(await page.locator("[data-comparison-limited]").count(), 0);
  assert.equal(await page.locator("#compare-add option").count(), 4);
  await page.locator("#compare-add").selectOption(cardIds[7]);
  await page.locator("#compare-add-submit").click();
  const chosen = [cardIds[0], cardIds[2], cardIds[3], cardIds[4], cardIds[7]];
  assert.deepEqual(await comparedCandidateIds(page), chosen);

  // The whole selection survives opening a car and returning to the comparison.
  await page.locator(`[data-comparison-column="${cardIds[7]}"] .comparison-candidate-link`).click();
  await page
    .locator(".section-nav")
    .getByRole("link", { name: "Auswertungen", exact: true })
    .click();
  assert.deepEqual(await comparedCandidateIds(page), chosen);

  // Incomplete, repeated and unknown selections degrade instead of failing.
  await page.goto(`${origin}${casePath}/compare?first=${cardIds[0]}&second=`);
  assert.deepEqual(await comparedCandidateIds(page), [cardIds[0]]);
  assert.equal(await page.locator(".comparison-remove").count(), 0);
  await page.goto(
    `${origin}${casePath}/compare?first=${cardIds[0]}&second=${cardIds[0]}&more=${cardIds[0]}&more=${UNKNOWN_CANDIDATE_ID}`,
  );
  assert.deepEqual(await comparedCandidateIds(page), [cardIds[0]]);
  assert.equal(
    (await browserRequest(page, `${casePath}/compare?first=${UNKNOWN_CANDIDATE_ID}`)).status(),
    404,
  );
  await page.goto(`${origin}${casePath}/compare`);
  assert.equal((await comparedCandidateIds(page)).length, 5);

  record(
    "Up to five cars are selected in the overview, added to or removed from the comparison, and kept across the car record",
  );
}

// ---------------------------------------------------------------------------------------------
// Phone layout
// ---------------------------------------------------------------------------------------------

async function checkPhoneLayout({
  page,
  origin,
  output,
  record,
  casePath,
  firstPath,
  firstId,
  secondId,
  ratingCriterion,
}) {
  await page.setViewportSize(PHONE_VIEWPORT);
  await page.goto(`${origin}${pairComparisonPath({ casePath, firstId, secondId })}`);
  assert.equal(await page.locator("#compare-add option").count(), 6);
  assert.ok(await fitsViewportWidth(page));
  await captureFromTop(page, output, "12-comparison-mobile.png");

  await page.goto(`${origin}${firstPath}`);
  assert.ok(await fitsViewportWidth(page));
  await openAssessmentEditor(page, ratingCriterion);
  await assessmentValue(page, ratingCriterion).selectOption("4");
  await saveAssessment(page, ratingCriterion);
  await captureFromTop(page, output, "13-candidate-mobile.png");

  const mobileDetails = page.locator("#candidate-edit");
  await page.locator("#tab-specifications").click();
  await page.locator("#edit-specifications").click();
  await mobileDetails.locator("#spec-section-Electric > summary").click();
  await mobileDetails.locator("#spec-Charging20To80Minutes").fill("23.5");
  assert.ok(await fitsViewportWidth(page));
  await page.screenshot({
    path: join(output, "14-specifications-mobile-edit.png"),
    fullPage: true,
  });
  await awaitWorkspaceChange(page, () => saveDetailsButton(mobileDetails).click());
  assert.match(
    await page
      .locator('#candidate-specifications [data-specification="Charging20To80Minutes"]')
      .textContent(),
    /23,5 min/,
  );
  await page.setViewportSize(DESKTOP_VIEWPORT);

  record(
    "Eight candidates, manual ordering, mobile assessment and two-car comparison work at 390px",
  );
}

// ---------------------------------------------------------------------------------------------
// Roles, validation, conflicts and export
// ---------------------------------------------------------------------------------------------

async function checkReaderAndEditorRoles(context) {
  const { page, origin, casePath } = context;

  await page.goto(`${origin}${casePath}`);
  const ownerVersion = await evaluationWorkspaceVersion(page);
  const readerEmail = await page.locator(".share-row").locator("span").first().textContent();
  assert.equal((await shareEvaluation(context, readerEmail, "Reader", ownerVersion)).status(), 200);

  await checkReaderCannotWrite(context);

  await page.reload();
  const reloadedOwnerVersion = await evaluationWorkspaceVersion(page);
  assert.equal(
    (await shareEvaluation(context, readerEmail, "Editor", reloadedOwnerVersion)).status(),
    200,
  );

  await checkEditorWritesAndRejectedPosts(context);
}

function shareEvaluation({ page, post, casePath }, email, access, version) {
  return post(page, casePath, {
    action: "share",
    email,
    access,
    version,
  });
}

async function checkReaderCannotWrite({
  reader,
  origin,
  post,
  casePath,
  firstPath,
  firstId,
  secondId,
  booleanCriterion,
}) {
  await reader.page.goto(`${origin}${firstPath}`);
  assert.equal(await reader.page.locator(".value-editor").count(), 0);
  assert.match(
    await reader.page
      .locator('#candidate-specifications [data-specification="PayloadKg"]')
      .textContent(),
    /400 kg/,
  );
  const version = await candidateWorkspaceVersion(reader.page);
  assert.equal(await reader.page.locator("#copy-candidate, #remove-candidate").count(), 0);

  for (const action of [
    "edit-candidate",
    "classify-candidate",
    "assess-candidate",
    "clear-assessment",
    "copy-candidate",
    "remove-candidate",
  ]) {
    assert.equal(
      (
        await post(reader.page, firstPath, {
          action,
          criterionId: booleanCriterion,
          booleanValue: "true",
          version,
        })
      ).status(),
      403,
    );
  }

  for (const action of ["add-candidate", "move-candidate"]) {
    assert.equal(
      (
        await post(reader.page, `${casePath}/candidates`, {
          action,
          itemId: firstId,
          "Input.Model": "Blocked",
          version,
        })
      ).status(),
      403,
    );
  }

  // The general settings stay readable but offer nothing that changes the evaluation.
  await reader.page.goto(`${origin}${casePath}?section=settings`);
  await reader.page.locator("#settings-title").waitFor({ state: "visible" });
  assert.equal(
    (await reader.page.locator(".workspace-subnav [aria-current=page]").textContent()).trim(),
    "Allgemein",
  );
  assert.equal(await reader.page.locator("#edit-evaluation, #evaluation-editor").count(), 0);
  assert.equal(
    await reader.page.getByRole("button", { name: "Evaluation löschen", exact: true }).count(),
    0,
  );
  assert.equal(await reader.page.locator("#add-share").count(), 0);
  assert.equal(
    await reader.page
      .locator("#evaluation-data")
      .getByRole("link", { name: "Exportieren", exact: true })
      .count(),
    1,
  );
  assert.equal(
    (
      await post(reader.page, casePath, {
        action: "rename",
        name: "Blocked",
        version: await evaluationWorkspaceVersion(reader.page),
      })
    ).status(),
    403,
  );

  const comparisonPath = pairComparisonPath({ casePath, firstId, secondId });
  await reader.page.goto(`${origin}${comparisonPath}`);
  assert.equal(await reader.page.locator("[data-editor-open]").count(), 0);
  assert.equal(
    (
      await post(reader.page, comparisonPath, {
        action: "assess-candidate",
        candidateId: firstId,
        criterionId: booleanCriterion,
        booleanValue: "true",
        version,
      })
    ).status(),
    403,
  );
}

async function checkEditorWritesAndRejectedPosts({
  page,
  reader,
  origin,
  post,
  firstPath,
  booleanCriterion,
  ratingCriterion,
}) {
  await reader.page.goto(`${origin}${firstPath}`);
  await openAssessmentEditor(reader.page, ratingCriterion);
  await assessmentValue(reader.page, ratingCriterion).selectOption("2");
  await saveAssessment(reader.page, ratingCriterion);

  await page.goto(`${origin}${firstPath}`);
  const version = await candidateWorkspaceVersion(page);
  assert.equal(await assessmentValue(page, ratingCriterion).inputValue(), "2");

  // A rating outside the scale is invalid; a stale version is a conflict.
  assert.equal(
    (
      await post(page, firstPath, {
        action: "assess-candidate",
        criterionId: ratingCriterion,
        ratingValue: "9",
        version,
      })
    ).status(),
    422,
  );
  assert.equal(
    (
      await post(page, firstPath, {
        action: "assess-candidate",
        criterionId: booleanCriterion,
        booleanValue: "false",
        version: "1",
      })
    ).status(),
    409,
  );
}

async function checkExportedObservations({
  page,
  browserRequest,
  record,
  casePath,
  firstId,
  booleanCriterion,
}) {
  const exported = await exportEvaluation(page, browserRequest, casePath);
  assert.equal(exported.version, 9);
  assert.equal(exported.candidates.length, CANDIDATE_TOTAL);

  const specifications = exported.candidates.find(
    (candidate) => candidate.id === firstId,
  ).specifications;
  assert.equal(specifications.kerbWeightKg, 2100);
  assert.equal(specifications.payloadKg, null);
  assert.equal(specifications.accelerationSeconds, 4.5);
  assert.equal(specifications.powerKw, null);
  assert.equal(specifications.powerPs, 400);
  assert.equal(specifications.charging20To80Minutes, 23.5);

  assert.ok(exported.assessments.length >= 6);
  assert.equal(
    exported.assessments.find(
      (value) => value.candidateId === firstId && value.criterionId === booleanCriterion,
    ).booleanValue,
    true,
  );

  record(
    "Candidate and comparison writes enforce reader/editor roles, validation and conflicts; exports contain all observations",
  );
}
