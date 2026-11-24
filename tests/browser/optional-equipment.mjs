import assert from "node:assert/strict";
import { join } from "node:path";

const DESKTOP_VIEWPORT = { width: 1440, height: 1050 };
const PHONE_VIEWPORT = { width: 390, height: 844 };
const RESPONSIVE_WIDTHS = [320, 390, 768, 1024, 1440];
const OFFER_COMPARISON_WIDTHS = [390, 1440];
const RESPONSIVE_CHECK_HEIGHT = 1000;

/**
 * Covers the third answer to a yes/no requirement, "optionally available": in the car record, the
 * overview, the comparison, for readers, in the export and in offer assessments. The car
 * assessments it changes are restored afterwards because later checks rely on the earlier values.
 */
export async function checkOptionalEquipment({
  page,
  reader,
  readerEmail,
  origin,
  output,
  post,
  browserRequest,
  record,
  casePath,
  firstId,
  boolean,
  offerChecks,
}) {
  const original = await exportEvaluation(page, browserRequest, casePath);
  const secondId = original.candidates.find((candidate) => candidate.id !== firstId).id;
  const context = {
    page,
    reader,
    readerEmail,
    origin,
    output,
    post,
    browserRequest,
    record,
    casePath,
    firstId,
    secondId,
    booleanCriterion: boolean,
    offerChecks,
    original,
    firstPath: `${casePath}/candidates/${firstId}`,
    comparePath: `${casePath}/compare?first=${firstId}&second=${secondId}`,
  };

  await checkOptionalAnswerInRecord(context);
  await checkOverviewSummary(context);
  await checkConflictInPhoneComparison(context);
  await checkInvalidAndUnauthorizedWrites(context);
  await checkExportKeepsOptionalDistinct(context);
  await restoreOriginalAssessments(context);
  await checkOptionalAnswerInOffer(context);

  await page.setViewportSize(DESKTOP_VIEWPORT);
}

// ---------------------------------------------------------------------------------------------
// Shared interactions
// ---------------------------------------------------------------------------------------------

async function exportEvaluation(page, browserRequest, path) {
  return JSON.parse(await (await browserRequest(page, `${path}?handler=Export`)).text());
}

function assessmentFieldId({ booleanCriterion }, candidateId) {
  return `assessment-${candidateId}-${booleanCriterion}`;
}

function assessmentEditor(context, candidateId) {
  return context.page.locator(`#editor-${assessmentFieldId(context, candidateId)}`);
}

function assessmentOpener(context, candidateId) {
  return context.page.locator(`#open-${assessmentFieldId(context, candidateId)}`);
}

function assessmentValue(context, candidateId) {
  return assessmentEditor(context, candidateId).getByLabel("Deine Angabe", { exact: true });
}

function assessmentNote(context, candidateId) {
  return assessmentEditor(context, candidateId).locator("textarea[name=notes]");
}

function submitButton(context, candidateId) {
  return assessmentEditor(context, candidateId).locator(".value-editor button[type=submit]");
}

function waitForHtmxToSettle(page) {
  return page.waitForFunction(() => !document.querySelector(".htmx-request, .htmx-settling"));
}

/** Submits the open assessment editor and expects the given HTTP status for the post. */
async function submitAssessment(context, candidateId, expectedStatus = 200) {
  const { page } = context;
  const path = new URL(page.url()).pathname;
  const saveResponse = page.waitForResponse(
    (response) =>
      new URL(response.url()).pathname === path && response.request().method() === "POST",
  );
  await submitButton(context, candidateId).click();
  assert.equal((await saveResponse).status(), expectedStatus);
  await waitForHtmxToSettle(page);
}

function hasFocus(locator) {
  return locator.evaluate((element) => element === document.activeElement);
}

function fitsViewportWidth(page) {
  return page.evaluate(() => document.documentElement.scrollWidth <= innerWidth);
}

function candidateWorkspaceVersion(target) {
  return target.locator("#candidate-workspace").getAttribute("data-version");
}

// ---------------------------------------------------------------------------------------------
// Car record
// ---------------------------------------------------------------------------------------------

async function checkOptionalAnswerInRecord(context) {
  const { page, origin, output, record, firstId, firstPath, booleanCriterion } = context;
  const recordRequests = `**${firstPath}*`;

  await page.setViewportSize(DESKTOP_VIEWPORT);
  await page.goto(`${origin}${firstPath}#requirements`);
  await assessmentOpener(context, firstId).click();
  await assessmentValue(context, firstId).selectOption("optional");
  await assessmentNote(context, firstId).fill(
    "Separat beim Hersteller oder einem Drittanbieter erhältlich.",
  );

  // A forged source is rejected; the draft stays in the editor.
  await assessmentEditor(context, firstId)
    .locator("select[name=source]")
    .evaluate((select) => {
      select.add(new Option("Invalid", "Invalid"));
      select.value = "Invalid";
    });
  await submitAssessment(context, firstId, 422);
  assert.equal(await assessmentValue(context, firstId).inputValue(), "optional");
  assert.match(await assessmentNote(context, firstId).inputValue(), /Drittanbieter/);

  // A dropped connection keeps the draft as well.
  await assessmentEditor(context, firstId).locator("select[name=source]").selectOption("Research");
  await page.route(recordRequests, (route) =>
    route.request().method() === "POST" ? route.abort("failed") : route.continue(),
  );
  await submitButton(context, firstId).click();
  await assessmentEditor(context, firstId)
    .locator(".editor-error")
    .getByText(/nicht gespeichert/)
    .waitFor();
  assert.equal(await assessmentValue(context, firstId).inputValue(), "optional");
  assert.match(await assessmentNote(context, firstId).inputValue(), /Drittanbieter/);
  await page.unroute(recordRequests);

  await submitAssessment(context, firstId);
  assert.equal(await hasFocus(assessmentOpener(context, firstId)), true);
  assert.equal(
    await assessmentOpener(context, firstId).locator(".fulfillment").textContent(),
    "Optional erhältlich",
  );
  assert.equal(
    await page
      .locator(`article[data-assessment-criterion="${booleanCriterion}"]`)
      .getAttribute("data-result"),
    "Optional",
  );

  await page.reload();
  await assessmentOpener(context, firstId).click();
  assert.equal(await assessmentValue(context, firstId).inputValue(), "optional");
  await page.screenshot({ path: join(output, "optional-equipment-editor.png"), fullPage: true });
  await page.keyboard.press("Escape");
  await page.screenshot({ path: join(output, "optional-equipment-desktop.png"), fullPage: true });

  record(
    "Optional equipment persists in the vehicle record and retains its value and note after validation and network failures",
  );
}

// ---------------------------------------------------------------------------------------------
// Overview
// ---------------------------------------------------------------------------------------------

async function checkOverviewSummary({
  page,
  origin,
  output,
  casePath,
  firstId,
  booleanCriterion,
  original,
}) {
  await page.goto(`${origin}${casePath}/candidates`);
  const card = page.locator(`[data-candidate-id="${firstId}"]`);
  const requirement = original.criteria.find((criterion) => criterion.id === booleanCriterion);
  if (requirement.priority === "Required") {
    assert.equal(await card.locator(".candidate-optional").count(), 0);
    assert.ok(
      (await card.locator('[data-summary="Optional"] .summary-popover').textContent()).includes(
        requirement.name,
      ),
    );
  }

  for (const width of RESPONSIVE_WIDTHS) {
    await page.setViewportSize({ width, height: RESPONSIVE_CHECK_HEIGHT });
    assert.ok(await fitsViewportWidth(page));
  }
  await page.screenshot({ path: join(output, "optional-equipment-list.png"), fullPage: true });
}

// ---------------------------------------------------------------------------------------------
// Comparison on a phone, including a version conflict
// ---------------------------------------------------------------------------------------------

async function checkConflictInPhoneComparison(context) {
  const { page, origin, output, post, record, casePath, secondId, comparePath, booleanCriterion } =
    context;

  await page.goto(`${origin}${comparePath}`);
  await page.setViewportSize(PHONE_VIEWPORT);
  const scroll = page.locator("#vehicle-comparison-scroll");
  await scroll.evaluate((element) => {
    element.scrollLeft = element.scrollWidth;
  });
  const horizontal = await scroll.evaluate((element) => element.scrollLeft);

  await assessmentOpener(context, secondId).click();
  await assessmentValue(context, secondId).selectOption("optional");
  await assessmentNote(context, secondId).fill(
    "Zusätzlicher physischer Schlüssel, separat erhältlich.",
  );

  // A write from elsewhere moves the version on, so the open editor is now stale.
  const version = await candidateWorkspaceVersion(page);
  assert.equal(
    (
      await post(page, `${casePath}/candidates/${secondId}`, {
        action: "assess-candidate",
        criterionId: booleanCriterion,
        booleanValue: "false",
        version,
      })
    ).status(),
    200,
  );
  await submitAssessment(context, secondId, 409);
  assert.equal(await assessmentValue(context, secondId).inputValue(), "optional");
  assert.match(await assessmentNote(context, secondId).inputValue(), /physischer Schlüssel/);

  await assessmentEditor(context, secondId)
    .getByRole("button", { name: "Aktuellen Stand ansehen" })
    .click();
  await assessmentOpener(context, secondId).click();
  assert.equal(await assessmentValue(context, secondId).inputValue(), "optional");
  await submitAssessment(context, secondId);
  assert.equal(await hasFocus(assessmentOpener(context, secondId)), true);
  assert.ok(Math.abs((await scroll.evaluate((element) => element.scrollLeft)) - horizontal) <= 1);
  assert.equal(
    await assessmentOpener(context, secondId).locator(".comparison-value").textContent(),
    "Optional",
  );
  await page.screenshot({ path: join(output, "optional-equipment-mobile.png"), fullPage: true });

  for (const width of RESPONSIVE_WIDTHS) {
    await page.setViewportSize({ width, height: RESPONSIVE_CHECK_HEIGHT });
    assert.ok(await fitsViewportWidth(page));
  }

  record(
    "Optional equipment in the mobile comparison preserves drafts, focus and horizontal position through a version conflict",
  );
}

// ---------------------------------------------------------------------------------------------
// Validation, roles and export
// ---------------------------------------------------------------------------------------------

async function checkInvalidAndUnauthorizedWrites({
  page,
  reader,
  readerEmail,
  origin,
  post,
  casePath,
  firstId,
  firstPath,
  comparePath,
  booleanCriterion,
  original,
}) {
  await page.goto(`${origin}${firstPath}`);
  const ownerVersion = await candidateWorkspaceVersion(page);
  for (const invalid of ["maybe", "OptionalAvailable=true"]) {
    assert.equal(
      (
        await post(page, firstPath, {
          action: "assess-candidate",
          criterionId: booleanCriterion,
          booleanValue: invalid,
          version: ownerVersion,
        })
      ).status(),
      422,
    );
  }

  // "Optional" only exists for yes/no requirements.
  const measurement = original.criteria.find((criterion) => criterion.type === "Measurement");
  assert.equal(
    (
      await post(page, firstPath, {
        action: "assess-candidate",
        criterionId: measurement.id,
        booleanValue: "optional",
        version: ownerVersion,
      })
    ).status(),
    422,
  );

  assert.equal(
    (
      await post(page, casePath, {
        action: "share",
        email: readerEmail,
        access: "Reader",
        version: ownerVersion,
      })
    ).status(),
    200,
  );
  await reader.page.goto(`${origin}${firstPath}`);
  assert.equal(await reader.page.locator(".value-editor").count(), 0);
  assert.match(
    await reader.page
      .locator(`article[data-assessment-criterion="${booleanCriterion}"]`)
      .textContent(),
    /Optional erhältlich/,
  );
  const readerVersion = await candidateWorkspaceVersion(reader.page);
  assert.equal(
    (
      await post(reader.page, firstPath, {
        action: "assess-candidate",
        criterionId: booleanCriterion,
        booleanValue: "optional",
        version: readerVersion,
      })
    ).status(),
    403,
  );
  assert.equal(
    (
      await post(reader.page, comparePath, {
        action: "assess-candidate",
        candidateId: firstId,
        criterionId: booleanCriterion,
        booleanValue: "optional",
        version: readerVersion,
      })
    ).status(),
    403,
  );

  // Hand the editor role back for the checks that follow.
  await page.goto(`${origin}${firstPath}`);
  const currentOwnerVersion = await candidateWorkspaceVersion(page);
  assert.equal(
    (
      await post(page, casePath, {
        action: "share",
        email: readerEmail,
        access: "Editor",
        version: currentOwnerVersion,
      })
    ).status(),
    200,
  );
}

async function checkExportKeepsOptionalDistinct({
  page,
  browserRequest,
  record,
  casePath,
  firstId,
  secondId,
  booleanCriterion,
}) {
  const exported = await exportEvaluation(page, browserRequest, casePath);
  for (const candidateId of [firstId, secondId]) {
    const answer = exported.assessments.find(
      (assessment) =>
        assessment.candidateId === candidateId && assessment.criterionId === booleanCriterion,
    );
    assert.equal(answer.optionalAvailable, true);
    assert.equal(answer.booleanValue, null);
  }

  record(
    "Optional answers remain visible to readers, reject unauthorized or invalid writes, and export without becoming yes or unknown",
  );
}

async function restoreOriginalAssessments({
  page,
  origin,
  post,
  casePath,
  firstId,
  secondId,
  booleanCriterion,
  original,
}) {
  // Restore shared fixtures so the existing restart checks keep their independent expectations.
  for (const candidateId of [firstId, secondId]) {
    const answer = original.assessments.find(
      (assessment) =>
        assessment.candidateId === candidateId && assessment.criterionId === booleanCriterion,
    );
    await page.goto(`${origin}${casePath}/candidates/${candidateId}`);
    const version = await candidateWorkspaceVersion(page);
    assert.equal(
      (
        await post(page, `${casePath}/candidates/${candidateId}`, {
          action: answer ? "assess-candidate" : "clear-assessment",
          criterionId: booleanCriterion,
          booleanValue: answer?.booleanValue == null ? "" : String(answer.booleanValue),
          source: answer?.source || "Unknown",
          notes: answer?.notes || "",
          version,
        })
      ).status(),
      200,
    );
  }
}

// ---------------------------------------------------------------------------------------------
// Offer assessments
// ---------------------------------------------------------------------------------------------

async function checkOptionalAnswerInOffer({ page, origin, browserRequest, record, offerChecks }) {
  const offerData = await exportEvaluation(page, browserRequest, offerChecks.casePath);
  const offerCriterion = offerData.criteria.find((criterion) => criterion.type === "Boolean").id;
  const offerPath = `${offerChecks.casePath}/offers/${offerChecks.firstId}`;

  await page.goto(`${origin}${offerPath}`);
  const row = page.locator(`[data-offer-assessment="${offerCriterion}"]`);
  await row.locator("[data-editor-open]").click();
  await row.getByLabel("Deine Angabe", { exact: true }).selectOption("optional");
  await row.locator("textarea[name=notes]").fill("Zubehör separat bestellbar.");
  const saveResponse = page.waitForResponse(
    (response) =>
      new URL(response.url()).pathname === offerPath && response.request().method() === "POST",
  );
  await row.locator(".value-editor button[type=submit]").click();
  assert.equal((await saveResponse).status(), 200);
  await waitForHtmxToSettle(page);
  assert.equal(await row.getAttribute("data-result"), "Optional");

  await page.reload();
  assert.equal(await row.getByLabel("Deine Angabe", { exact: true }).inputValue(), "optional");
  await page.getByRole("link", { name: "Offerten vergleichen", exact: true }).click();
  const criterionName = offerData.criteria.find(
    (criterion) => criterion.id === offerCriterion,
  ).name;
  const comparisonRow = page.locator("tr").filter({
    has: page.getByText(criterionName, { exact: true }),
  });
  assert.match(await comparisonRow.textContent(), /Optional erhältlich/);

  for (const width of OFFER_COMPARISON_WIDTHS) {
    await page.setViewportSize({ width, height: RESPONSIVE_CHECK_HEIGHT });
    assert.ok(await fitsViewportWidth(page));
  }

  record(
    "The shared optional answer also persists in offer assessments and appears in the offer comparison",
  );
}
