import assert from "node:assert/strict";
import { join } from "node:path";

const MOBILE_VIEWPORT = { width: 390, height: 844 };
const OVERFLOW_CHECK_WIDTHS = [320, 768, 1024, 1440];
const OVERFLOW_CHECK_HEIGHT = 1000;
const STALE_PAGE_TIMEOUT_MS = 10000;

const UNSAVED_CANDIDATE_NAME = "Ungespeicherter Kandidat";
const DISCARDED_NOTE = "Dieser Entwurf bleibt bis zum bewussten Verwerfen.";
const NETWORK_FAILURE_NOTE = "Bei einem Verbindungsfehler bleibt diese Notiz.";
const INVALID_RATING_NOTE = "Auch eine ungültige Angabe bleibt zur Korrektur erhalten.";
const CONFLICT_NOTE = "Lokaler Entwurf während einer gleichzeitigen Änderung.";
const EXPIRED_SESSION_NOTE = "Entwurf mit inzwischen abgelaufener Anmeldung.";

export async function checkCanvas({
  page,
  origin,
  casePath,
  firstId,
  secondId,
  boolean,
  rating,
  output,
  record,
}) {
  const canvas = createCanvas({
    page,
    origin,
    casePath,
    firstId,
    secondId,
    boolean,
    rating,
    output,
    record,
  });

  await checkVehicleCreationDrafts(canvas);

  await page.goto(origin + canvas.comparePath);
  const original = await canvas.exportedBooleanAssessment();

  await checkPairAndEditorRules(canvas, original);
  await checkMobileSecondColumnEditing(canvas);
  await checkFailedSavesKeepDrafts(canvas);
  await checkConflictKeepsLocalDraft(canvas);
  await restoreBooleanAssessment(canvas, original);
  await checkRecordAndComparisonWidths(canvas);
  await checkExpiredSessionKeepsDraft(canvas);
}

// ── Shared locators and interactions ────────────────────────────────────────

/**
 * Bundles the scenario parameters with the locators and interactions of the
 * assessment editors in the second comparison column.
 */
function createCanvas(parameters) {
  const { page, casePath, firstId, secondId, boolean } = parameters;

  const editor = (criterionId) => page.locator(`#editor-assessment-${secondId}-${criterionId}`);
  const opener = (criterionId) => page.locator(`#open-assessment-${secondId}-${criterionId}`);
  const input = (criterionId) => editor(criterionId).getByLabel("Deine Angabe", { exact: true });
  const note = (criterionId) => editor(criterionId).locator("textarea[name=notes]");
  const save = (criterionId) => editor(criterionId).getByRole("button", { name: /speichern$/ });

  async function settle() {
    await page.waitForFunction(() => !document.querySelector(".htmx-request, .htmx-settling"));
  }

  /** Saves the editor of a criterion and expects the given HTTP status of the comparison post. */
  async function submit(criterionId, status = 200) {
    const comparisonPost = page.waitForResponse(
      (response) =>
        response.request().method() === "POST" &&
        new URL(response.url()).pathname === `${casePath}/compare`,
    );

    await save(criterionId).click();
    assert.equal((await comparisonPost).status(), status);
    await settle();
  }

  async function exportData() {
    return page.evaluate(async (path) => (await fetch(path + "?handler=Export")).json(), casePath);
  }

  async function exportedBooleanAssessment() {
    return (await exportData()).assessments.find(
      (value) => value.candidateId === secondId && value.criterionId === boolean,
    );
  }

  return {
    ...parameters,
    comparePath: `${casePath}/compare?first=${firstId}&second=${secondId}`,
    firstPath: `${casePath}/candidates/${firstId}`,
    confirmation: page.locator("#action-confirmation"),
    editor,
    opener,
    input,
    note,
    save,
    submit,
    exportedBooleanAssessment,
  };
}

function isFocused(locator) {
  return locator.evaluate((element) => element === document.activeElement);
}

function hasNoDocumentOverflow(page) {
  return page.evaluate(() => document.documentElement.scrollWidth <= innerWidth);
}

// ── Vehicle creation and removal ────────────────────────────────────────────

async function checkVehicleCreationDrafts({ page, origin, casePath, confirmation, record }) {
  await page.goto(origin + casePath + "/candidates");

  const candidateCount = await page.locator(".candidate-card").count();
  const addButton = page.locator("#add-candidate");
  const creation = page.getByRole("dialog", { name: "Auto hinzufügen", exact: true });
  const modelInput = creation.getByLabel("Modell / Bezeichnung");

  // Escape on a changed draft offers to continue and keeps the draft.
  await addButton.click();
  await modelInput.fill(UNSAVED_CANDIDATE_NAME);
  await page.keyboard.press("Escape");
  await confirmation.getByRole("button", { name: "Weiterbearbeiten" }).click();
  assert.equal(await modelInput.inputValue(), UNSAVED_CANDIDATE_NAME);

  // Clicking outside the dialog does not close it.
  await page.mouse.click(20, 20);
  assert.equal(await creation.isVisible(), true);

  // Discarding returns focus to the opener and the next dialog starts empty.
  await creation.getByRole("button", { name: "Abbrechen", exact: true }).click();
  await confirmation.getByRole("button", { name: "Änderungen verwerfen", exact: true }).click();
  assert.equal(await isFocused(addButton), true);
  await addButton.click();
  assert.equal(await modelInput.inputValue(), "");
  await page.keyboard.press("Escape");

  // Removing a car lives in its record and asks first; cancelling keeps the car.
  await page.locator(".candidate-card h3 a").first().click();
  await page.getByRole("button", { name: "Auto löschen", exact: true }).click();
  assert.equal(await isFocused(confirmation.locator("[data-confirm-cancel]")), true);
  await page.keyboard.press("Escape");
  await page.getByRole("link", { name: /Alle Autos/ }).click();
  assert.equal(await page.locator(".candidate-card").count(), candidateCount);

  record(
    "Vehicle creation protects drafts and restores focus; cancelling removal leaves the collection unchanged",
  );
}

// ── Comparison pair and editor rules ────────────────────────────────────────

async function checkPairAndEditorRules(canvas, original) {
  const { page, firstId, secondId, boolean, confirmation, editor, opener, input, note, record } =
    canvas;

  // The pair remains explicit in links, including returning from the second vehicle.
  await page.locator(".comparison-candidate-link").nth(1).click();
  assert.equal(new URL(page.url()).searchParams.get("first"), firstId);
  assert.equal(new URL(page.url()).searchParams.get("second"), secondId);
  await page
    .locator(".section-nav")
    .getByRole("link", { name: "Auswertungen", exact: true })
    .click();
  assert.deepEqual(
    await page
      .locator("[data-comparison-column]")
      .evaluateAll((cells) => cells.map((cell) => cell.dataset.comparisonColumn)),
    [firstId, secondId],
  );

  // Unchanged Escape closes directly; changed Escape offers a safe continuation.
  await opener(boolean).click();
  assert.equal(await isFocused(input(boolean)), true);
  await page.keyboard.press("Escape");
  assert.equal(await editor(boolean).isVisible(), false);
  assert.equal(await isFocused(opener(boolean)), true);

  await opener(boolean).click();
  await note(boolean).fill(DISCARDED_NOTE);
  await page.keyboard.press("Escape");
  assert.equal(await isFocused(confirmation.locator("[data-confirm-cancel]")), true);
  await confirmation.getByRole("button", { name: "Weiterbearbeiten" }).click();
  assert.equal(await note(boolean).inputValue(), DISCARDED_NOTE);

  // After an explicit discard the editor shows the stored note again.
  await editor(boolean).getByRole("button", { name: "Abbrechen", exact: true }).click();
  await confirmation.getByRole("button", { name: "Änderungen verwerfen", exact: true }).click();
  await opener(boolean).click();
  assert.equal(await note(boolean).inputValue(), original?.notes || "");
  await page.keyboard.press("Escape");

  record(
    "Canvas capture/comparison retains the pair; Escape, discard and focus follow the same editor rules",
  );
}

// ── Mobile editing in the second column ─────────────────────────────────────

async function checkMobileSecondColumnEditing(canvas) {
  const { page, boolean, output, editor, opener, note, submit, record } = canvas;

  // Keyboard-accessible horizontal comparison with a full-width form below it.
  await page.setViewportSize(MOBILE_VIEWPORT);
  const scroll = page.locator("#vehicle-comparison-scroll");
  await scroll.evaluate((element) => {
    element.scrollLeft = element.scrollWidth;
  });
  const horizontal = await scroll.evaluate((element) => element.scrollLeft);
  assert.ok(horizontal > 0);

  const pinned = await page.locator(`[data-comparison-criterion="${boolean}"] th`).boundingBox();
  assert.ok(pinned.x >= 0 && pinned.x < 50);

  await opener(boolean).click();
  const editorBounds = await editor(boolean).boundingBox();
  assert.ok(editorBounds.x >= 0 && editorBounds.x + editorBounds.width <= MOBILE_VIEWPORT.width);
  assert.equal(
    await editor(boolean)
      .locator("form")
      .first()
      .evaluate((form) => !!form.closest(".comparison-scroll")),
    false,
  );

  await note(boolean).fill("Auf dem Handy geprüft.");
  await submit(boolean);
  assert.equal(await editor(boolean).isVisible(), false);
  assert.equal(await isFocused(opener(boolean)), true);
  assert.ok(Math.abs((await scroll.evaluate((element) => element.scrollLeft)) - horizontal) <= 1);
  assert.ok(await hasNoDocumentOverflow(page));

  for (const heading of await page.locator(".comparison-group-label").all()) {
    const headingBounds = await heading.boundingBox();
    assert.ok(
      headingBounds.x >= 0 && headingBounds.x + headingBounds.width <= MOBILE_VIEWPORT.width,
      "Group labels remain visible in the second comparison column",
    );
  }

  await page.screenshot({ path: join(output, "canvas-comparison-mobile.png"), fullPage: true });
  record(
    "Mobile second-column editing preserves pinned criteria, viewport, horizontal position and return focus",
  );
}

// ── Network failure and validation ──────────────────────────────────────────

async function checkFailedSavesKeepDrafts(canvas) {
  const { page, casePath, boolean, rating, editor, opener, input, note, save, submit, record } =
    canvas;
  const comparisonRequestPattern = `**${casePath}/compare?*`;

  // A failed request cannot look like a successful save or erase the draft.
  await opener(boolean).click();
  await note(boolean).fill(NETWORK_FAILURE_NOTE);
  const rejectPost = (route) =>
    route.request().method() === "POST" ? route.abort("failed") : route.continue();
  await page.route(comparisonRequestPattern, rejectPost);
  await save(boolean).click();
  await editor(boolean).getByRole("alert").filter({ hasText: "nicht gespeichert" }).waitFor();
  assert.equal(await note(boolean).inputValue(), NETWORK_FAILURE_NOTE);
  await page.unroute(comparisonRequestPattern, rejectPost);
  await submit(boolean);

  // Force a rating outside the offered range so the server has to reject it.
  await opener(rating).click();
  await input(rating).evaluate((select) => {
    select.add(new Option("9 / 5", "9"));
    select.value = "9";
    select.dispatchEvent(new Event("change", { bubbles: true }));
  });
  await note(rating).fill(INVALID_RATING_NOTE);
  await submit(rating, 422);
  assert.equal(await input(rating).inputValue(), "9");
  assert.equal(await note(rating).inputValue(), INVALID_RATING_NOTE);
  assert.equal(await editor(rating).isVisible(), true);

  await input(rating).selectOption("4");
  await submit(rating);

  record(
    "Network failure and validation keep values and notes in the open editor; retry saves successfully",
  );
}

// ── Concurrent change ───────────────────────────────────────────────────────

async function checkConflictKeepsLocalDraft(canvas) {
  const { page, casePath, secondId, boolean, confirmation, editor, opener, input, note, record } =
    canvas;
  const { submit, exportedBooleanAssessment } = canvas;

  await opener(boolean).click();
  await input(boolean).selectOption("true");
  await note(boolean).fill(CONFLICT_NOTE);

  // The same assessment is changed through the vehicle record, as another window would do.
  const remoteStatus = await page.evaluate(
    async ({ path, candidateId, criterionId }) => {
      const token = document.querySelector("[name=__RequestVerificationToken]").value;
      const version = document.querySelector("#candidate-workspace").dataset.version;
      return (
        await fetch(`${path}/candidates/${candidateId}`, {
          method: "POST",
          body: new URLSearchParams({
            __RequestVerificationToken: token,
            version,
            action: "assess-candidate",
            criterionId,
            booleanValue: "false",
            source: "Inspection",
            notes: "In einem anderen Fenster geändert.",
          }),
        })
      ).status;
    },
    { path: casePath, candidateId: secondId, criterionId: boolean },
  );
  assert.equal(remoteStatus, 200);

  await submit(boolean, 409);
  assert.equal(await input(boolean).inputValue(), "true");
  assert.equal(await note(boolean).inputValue(), CONFLICT_NOTE);
  assert.equal((await exportedBooleanAssessment()).booleanValue, false);

  // Reviewing the server state and an attempted navigation both keep the local draft.
  await editor(boolean).getByRole("button", { name: "Aktuellen Stand ansehen" }).click();
  await page.locator(".section-nav").getByRole("link", { name: "Autos", exact: true }).click();
  await confirmation.getByRole("button", { name: "Weiterbearbeiten" }).click();
  assert.equal(new URL(page.url()).pathname, `${casePath}/compare`);
  await opener(boolean).click();
  assert.equal(await note(boolean).inputValue(), CONFLICT_NOTE);

  await submit(boolean);
  assert.equal((await exportedBooleanAssessment()).booleanValue, true);

  record(
    "Conflict shows the newer server value and retains the local draft even while reviewing or attempting navigation",
  );
}

async function restoreBooleanAssessment(canvas, original) {
  const { boolean, editor, opener, input, note, submit } = canvas;

  // Restore the shared fixture so the remaining domain assertions stay independent.
  await opener(boolean).click();
  await input(boolean).selectOption(
    original?.booleanValue == null ? "" : String(original.booleanValue),
  );
  await editor(boolean)
    .getByLabel("Grundlage", { exact: true })
    .selectOption(original?.source || "Unknown");
  await note(boolean).fill(original?.notes || "");
  await submit(boolean);
}

// ── Record tabs and comparison at all widths ────────────────────────────────

async function checkRecordAndComparisonWidths({
  page,
  origin,
  comparePath,
  firstPath,
  output,
  record,
}) {
  const specifications = page.locator("#record-specifications");
  const assessments = page.locator("#record-assessments");

  for (const width of OVERFLOW_CHECK_WIDTHS) {
    await page.setViewportSize({ width, height: OVERFLOW_CHECK_HEIGHT });
    assert.ok(await hasNoDocumentOverflow(page), `comparison overflow at ${width}`);

    await page.goto(origin + firstPath);
    assert.ok(await hasNoDocumentOverflow(page), `record overflow at ${width}`);
    assert.equal(await specifications.isVisible(), true);

    // The selected tab is part of the address and survives a reload.
    await page.getByRole("tab", { name: "Anforderungen", exact: true }).click();
    assert.equal(await assessments.isVisible(), true);
    assert.equal(new URL(page.url()).hash, "#requirements");
    await page.reload();
    assert.equal(await assessments.isVisible(), true);

    await page.getByRole("tab", { name: "Technische Daten", exact: true }).click();
    assert.equal(await specifications.isVisible(), true);
    assert.equal(new URL(page.url()).hash, "");

    await page.goto(origin + comparePath);
  }

  await page.screenshot({ path: join(output, "canvas-comparison-desktop.png"), fullPage: true });
  record("Canvas record, tabs and comparison fit at 320/768/1024/1440px without document overflow");
}

// ── Expired session ─────────────────────────────────────────────────────────

async function checkExpiredSessionKeepsDraft({
  page,
  origin,
  casePath,
  comparePath,
  secondId,
  boolean,
  record,
}) {
  // A real expired session redirects to login; that response must never replace the editor.
  const expired = await page
    .context()
    .browser()
    .newContext({
      storageState: await page.context().storageState(),
      ignoreHTTPSErrors: origin.startsWith("https:"),
    });

  try {
    const stalePage = await expired.newPage();
    stalePage.setDefaultTimeout(STALE_PAGE_TIMEOUT_MS);
    await stalePage.goto(origin + comparePath);
    await stalePage.locator(`#open-assessment-${secondId}-${boolean}`).click();

    const staleEditor = stalePage.locator(`#editor-assessment-${secondId}-${boolean}`);
    const staleNote = staleEditor.locator("textarea");
    const staleSave = staleEditor.getByRole("button", { name: /speichern$/ });

    await staleNote.fill(EXPIRED_SESSION_NOTE);
    await expired.clearCookies();
    await staleSave.click();
    await staleEditor.getByRole("alert").filter({ hasText: "nicht mehr bearbeiten" }).waitFor();

    assert.equal(await staleNote.inputValue(), EXPIRED_SESSION_NOTE);
    assert.equal(await staleSave.isDisabled(), true);
    assert.equal(new URL(stalePage.url()).pathname, `${casePath}/compare`);
  } finally {
    await expired.close();
  }

  record(
    "Expired authentication retains the draft and blocks further saves instead of swapping login into the workspace",
  );
}
