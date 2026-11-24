import assert from "node:assert/strict";
import { join } from "node:path";

const EVALUATION_NAME =
  "Eine gemeinsame Entscheidung mit Platz für längere Namen und eigene Anforderungen";
const CRITERION_DRAFT_NAME = "Lokaler Entwurf bleibt erhalten";
const PURPOSE_DRAFT = "Eine erhaltene Notiz zur Entscheidung.";
const UNCONFIRMED_EMAIL = "not-confirmed@example.test";
const READER_GROUP_DRAFT = "Nicht verlieren";

const VIEWPORT_WIDTHS = [320, 390, 768, 1024, 1440];
const SCREENSHOT_WIDTHS = [390, 1440];
const NARROW_WIDTH_LIMIT = 700;
const NARROW_VIEWPORT_HEIGHT = 844;
const WIDE_VIEWPORT_HEIGHT = 1050;

export async function checkEvaluations({
  page,
  reader,
  readerEmail,
  origin,
  output,
  post,
  browserRequest,
  mutate,
  record,
}) {
  const returnUrl = page.url();

  await checkCreationRequiresRegion({ page, origin, post, record });
  const path = await createEvaluationAndOpenCriteria(page);

  const workspace = {
    page,
    reader,
    readerEmail,
    origin,
    output,
    post,
    browserRequest,
    mutate,
    record,
    path,
  };

  await checkGroupCreationSurvivesNetworkFailure(workspace);

  const groupId = (await exportCase(workspace)).groups[0].id;
  const criterionId = await checkStaleSortingKeepsCriterionDraft(workspace, groupId);
  await checkGroupRenameConflict(workspace, groupId);
  await checkFailedGroupRemovalKeepsDraft(workspace, groupId);
  await checkSettingsAndPermissions(workspace);
  await checkViewportsLegacyLinksAndRevocation(workspace);
  const ungrouped = await checkCriterionDeletionAndGroupDissolving(workspace, groupId, criterionId);
  await checkEvaluationDeletion(workspace);
  await checkLegacyImport(workspace, ungrouped);

  await page.goto(returnUrl);
}

// ── Shared locators and interactions ────────────────────────────────────────

function currentVersion(page) {
  return page.locator("#evaluation-workspace").getAttribute("data-version");
}

/** Clicks a button and expects the given HTTP status of the resulting workspace post. */
async function submitExpectingStatus({ page, path }, button, status) {
  const workspacePost = page.waitForResponse(
    (response) =>
      new URL(response.url()).pathname === path && response.request().method() === "POST",
  );

  await button.click();
  assert.equal((await workspacePost).status(), status);
  await page.waitForFunction(() => !document.querySelector(".htmx-settling, .htmx-request"));
}

async function exportCase({ page, path, browserRequest }) {
  const response = await browserRequest(page, `${path}?handler=Export`);
  assert.equal(response.status(), 200, "Export must be authorized and available");
  return JSON.parse(await response.text());
}

/** Renames the group through a direct request, as a second user would do in the meantime. */
async function renameGroupConcurrently({ page, path, post }, groupId, name) {
  return post(page, path, {
    action: "rename-group",
    itemId: groupId,
    name,
    version: await currentVersion(page),
  });
}

function isFocused(locator) {
  return locator.evaluate((element) => element === document.activeElement);
}

async function activeSubnavLabel(page) {
  return (await page.locator(".workspace-subnav [aria-current=page]").textContent()).trim();
}

async function openSubnav(page, name) {
  await page.locator(".workspace-subnav").getByRole("link", { name, exact: true }).click();
}

function groupEditorOf(page, groupId) {
  return page.locator(`#group-editor-${groupId}`);
}

function criterionEditorOf(page, criterionId) {
  return page.locator(`#criterion-editor-${criterionId}`);
}

function confirmAccept(page) {
  return page.locator("#action-confirmation [data-confirm-accept]");
}

// ── Creation ────────────────────────────────────────────────────────────────

async function checkCreationRequiresRegion({ page, origin, post, record }) {
  await page.goto(origin);
  await page.locator("#add-evaluation").click();

  assert.equal(await page.locator("#new-region-country").inputValue(), "");
  assert.equal(await page.locator("#new-region-currency").inputValue(), "");

  const incompleteRegions = [
    { Currency: "EUR" },
    { Country: "CH" },
    { Country: "ZZ", Currency: "CHF" },
  ];
  for (const fields of incompleteRegions) {
    const response = await post(page, "/", { Name: "Must not create", ...fields });
    assert.equal(response.status(), 422);
  }

  record("Creation requires an explicit valid country and currency on the server without defaults");
}

/** Creates the evaluation in the already open dialog and returns its path. */
async function createEvaluationAndOpenCriteria(page) {
  const creation = page.locator("#new-evaluation");

  await creation.getByLabel("Name der Evaluation").fill(EVALUATION_NAME);
  await page.locator("#new-region-country").selectOption("CH");
  await page.locator("#new-region-currency").fill("EUR");
  await creation.getByRole("button", { name: "Evaluation anlegen", exact: true }).click();
  await page.waitForURL(/\/evaluations\//);
  const path = new URL(page.url()).pathname;

  // A new evaluation opens on its general page; the criteria are one tab further.
  assert.equal(await activeSubnavLabel(page), "Allgemein");
  assert.equal(await page.locator(".criteria-main").isVisible(), false);
  await openSubnav(page, "Kriterien");
  assert.equal(new URL(page.url()).searchParams.get("section"), "criteria");

  return path;
}

// ── Groups and criteria ─────────────────────────────────────────────────────

async function checkGroupCreationSurvivesNetworkFailure({ page, path, mutate, record }) {
  const workspaceRequestPattern = `**${path}`;
  const addGroupButton = page.locator("#add-group");
  const groupEditor = page.locator("#new-group-editor");
  const nameInput = groupEditor.getByLabel("Gruppenname");
  const saveButton = groupEditor.getByRole("button", { name: "Gruppe hinzufügen", exact: true });

  await addGroupButton.click();
  await nameInput.fill("Alltag");

  await page.route(workspaceRequestPattern, (route) =>
    route.request().method() === "POST" ? route.abort() : route.continue(),
  );
  await saveButton.click();
  await groupEditor.locator(".editor-error:not([hidden])").waitFor();
  assert.equal(await nameInput.inputValue(), "Alltag");
  await page.unroute(workspaceRequestPattern);

  await mutate(page, () => saveButton.click());
  assert.equal(await isFocused(addGroupButton), true);

  record(
    "Group creation retains its draft after a network failure and restores opener focus after retry",
  );
}

/** Adds two criteria, provokes a stale sorting request and returns the edited criterion's id. */
async function checkStaleSortingKeepsCriterionDraft(workspace, groupId) {
  const { page, mutate, record } = workspace;

  for (const criterion of ["Lange Strecken ohne Ladestopp", "Gute Sicht beim Einparken"]) {
    await page.locator("#add-criterion").click();
    const newCriterionEditor = page.locator("#new-criterion-editor");
    await newCriterionEditor.getByLabel("Bezeichnung").fill(criterion);
    await newCriterionEditor.getByLabel("Gruppe", { exact: true }).selectOption(groupId);
    await mutate(page, () =>
      newCriterionEditor.getByRole("button", { name: "Kriterium hinzufügen", exact: true }).click(),
    );
  }

  const data = await exportCase(workspace);
  const criterionId = data.criteria[0].id;
  const editor = criterionEditorOf(page, criterionId);
  const nameInput = editor.getByLabel("Bezeichnung");

  await page.locator(`#edit-criterion-${criterionId}`).click();
  await nameInput.fill(CRITERION_DRAFT_NAME);

  // Another user changes the group before the local sorting request.
  assert.equal((await renameGroupConcurrently(workspace, groupId, "Unterwegs")).status(), 200);
  await submitExpectingStatus(
    workspace,
    page.getByRole("button", { name: `Kriterium ${data.criteria[1].name} nach oben`, exact: true }),
    409,
  );
  assert.equal(await editor.isVisible(), true);
  assert.equal(await nameInput.inputValue(), CRITERION_DRAFT_NAME);
  await page.getByRole("heading", { name: "Unterwegs", exact: true }).waitFor();

  // Cancelling asks first; continuing keeps the draft, which can then be saved.
  await editor.getByRole("button", { name: "Abbrechen", exact: true }).click();
  await page.getByRole("button", { name: "Weiterbearbeiten", exact: true }).click();
  assert.equal(await nameInput.inputValue(), CRITERION_DRAFT_NAME);
  await mutate(page, () => editor.getByRole("button", { name: "Speichern", exact: true }).click());
  assert.equal(
    (await exportCase(workspace)).criteria.find((criterion) => criterion.id === criterionId).name,
    CRITERION_DRAFT_NAME,
  );

  record(
    "A stale sorting request shows the current server state while preserving the open criterion draft",
  );

  return criterionId;
}

async function checkGroupRenameConflict(workspace, groupId) {
  const { page, mutate, record } = workspace;
  const editGroup = groupEditorOf(page, groupId);
  const nameInput = editGroup.getByLabel("Gruppenname");
  const saveButton = editGroup.getByRole("button", { name: "Speichern", exact: true });

  await page.locator(`#edit-group-${groupId}`).click();
  await nameInput.fill("Mein Gruppenname");
  assert.equal(
    (await renameGroupConcurrently(workspace, groupId, "Anderer Gruppenname")).status(),
    200,
  );

  await submitExpectingStatus(workspace, saveButton, 409);
  assert.equal(await nameInput.inputValue(), "Mein Gruppenname");
  assert.equal((await exportCase(workspace)).groups[0].name, "Anderer Gruppenname");

  await editGroup.getByRole("button", { name: "Aktuellen Stand ansehen", exact: true }).click();
  await page.locator(`#edit-group-${groupId}`).click();
  await mutate(page, () => saveButton.click());
  assert.equal((await exportCase(workspace)).groups[0].name, "Mein Gruppenname");

  record(
    "Group conflicts retain the local name and require an explicit save after reviewing the current state",
  );
}

async function checkFailedGroupRemovalKeepsDraft(workspace, groupId) {
  const { page, record } = workspace;
  const editGroup = groupEditorOf(page, groupId);
  const nameInput = editGroup.getByLabel("Gruppenname");

  await page.locator(`#edit-group-${groupId}`).click();
  await nameInput.fill("Entwurf vor dem Auflösen");
  assert.equal((await renameGroupConcurrently(workspace, groupId, "Aktueller Name")).status(), 200);

  await editGroup.getByRole("button", { name: "Gruppe auflösen", exact: true }).click();
  await submitExpectingStatus(workspace, confirmAccept(page), 409);
  assert.equal(await nameInput.inputValue(), "Entwurf vor dem Auflösen");
  assert.equal((await exportCase(workspace)).groups.length, 1);

  await editGroup.getByRole("button", { name: "Abbrechen", exact: true }).click();
  await page.getByRole("button", { name: "Änderungen verwerfen", exact: true }).click();

  record("A failed group removal preserves the adjacent rename draft and does not remove criteria");
}

// ── Settings, data actions and permissions ──────────────────────────────────

async function checkSettingsAndPermissions(workspace) {
  await checkGeneralNavigation(workspace.page);
  await checkSettingsKeepValidationDraft(workspace);
  await checkGeneralSectionsAndDataActions(workspace.page);
  await checkShareEditor(workspace);

  workspace.record(
    "Settings and permissions use the shared editor, retain validation drafts and update the reading view",
  );
}

async function checkGeneralNavigation(page) {
  assert.equal(await page.locator(".evaluation-tools").count(), 0);
  assert.equal(await page.getByText("Evaluation verwalten").count(), 0);

  await openSubnav(page, "Allgemein");
  assert.equal(await activeSubnavLabel(page), "Allgemein");
  assert.deepEqual(
    (await page.locator(".workspace-subnav a").allTextContents()).map((text) => text.trim()),
    ["Allgemein", "Kriterien"],
  );
  assert.equal((await page.locator(".workflow-nav a.active").textContent()).trim(), "Grundlagen");
}

async function checkSettingsKeepValidationDraft(workspace) {
  const { page, mutate } = workspace;
  const nameInput = page.locator("#case-name");
  const purposeInput = page.locator("#case-purpose");
  const countrySelect = page.locator("#case-region-country");
  const currencyInput = page.locator("#case-region-currency");

  await page.locator("#edit-evaluation").click();
  await nameInput.fill("   ");
  await purposeInput.fill(PURPOSE_DRAFT);
  await countrySelect.selectOption("DE");
  await currencyInput.fill("CHF");
  await submitExpectingStatus(workspace, page.locator("#save-evaluation"), 422);

  assert.equal(await purposeInput.inputValue(), PURPOSE_DRAFT);
  assert.equal(await countrySelect.inputValue(), "DE");
  assert.equal(await currencyInput.inputValue(), "CHF");

  await nameInput.fill(EVALUATION_NAME);
  await mutate(page, () => page.locator("#save-evaluation").click());
  assert.match(await page.locator(".evaluation-facts").innerText(), /Eine erhaltene Notiz/);
  assert.equal((await exportCase(workspace)).country, "DE");
  assert.equal((await exportCase(workspace)).currency, "CHF");
}

async function checkGeneralSectionsAndDataActions(page) {
  assert.deepEqual(
    await page.locator("#evaluation-workspace > section:not([hidden]) h2").allTextContents(),
    ["Allgemein", "Freigaben", "Daten"],
  );

  const dataActions = ["Importieren", "Exportieren", "Löschen"];
  assert.deepEqual(
    (await page.locator("#evaluation-data h3").allTextContents()).filter((text) =>
      dataActions.includes(text),
    ),
    dataActions,
  );
  assert.deepEqual(
    (await page.locator("#evaluation-data .button").allTextContents())
      .map((text) => text.trim())
      .filter((text) => dataActions.includes(text)),
    dataActions,
  );
  assert.equal(
    await page
      .locator("#evaluation-data")
      .getByRole("link", { name: "Evaluation importieren", exact: true })
      .getAttribute("href"),
    "/#import-evaluation",
  );
}

async function checkShareEditor(workspace) {
  const { page, readerEmail, mutate } = workspace;
  const share = page.locator("#share-editor-new");
  const emailInput = share.getByLabel("E-Mail-Adresse");
  const grantButton = share.getByRole("button", { name: "Zugriff freigeben", exact: true });

  // An address without a confirmed account is rejected and stays in the field.
  await page.locator("#add-share").click();
  await emailInput.fill(UNCONFIRMED_EMAIL);
  await submitExpectingStatus(workspace, grantButton, 422);
  assert.equal(await emailInput.inputValue(), UNCONFIRMED_EMAIL);

  await emailInput.fill(readerEmail);
  await mutate(page, () => grantButton.click());

  await page.locator(".share-row [data-editor-open]").click();
  const roleEditor = page.locator(".share-entry [data-editor]");
  await roleEditor.getByLabel("Berechtigung").selectOption("Editor");
  await mutate(page, () =>
    roleEditor.getByRole("button", { name: "Speichern", exact: true }).click(),
  );
  assert.match(await page.locator(".share-row").innerText(), /Bearbeiter/);
}

// ── Viewports, legacy links and revocation ──────────────────────────────────

async function checkViewportsLegacyLinksAndRevocation(workspace) {
  await checkSectionsFitViewports(workspace);
  await checkLegacySharingLink(workspace);
  await checkRevocationProtectsReaderDraft(workspace);

  workspace.record(
    "Desktop/mobile sections fit the viewport; old links work and real permission revocation protects the draft",
  );
}

async function checkSectionsFitViewports({ page, origin, path, output }) {
  for (const width of VIEWPORT_WIDTHS) {
    const height = width < NARROW_WIDTH_LIMIT ? NARROW_VIEWPORT_HEIGHT : WIDE_VIEWPORT_HEIGHT;
    await page.setViewportSize({ width, height });

    for (const section of ["criteria", "sharing", "settings"]) {
      await page.goto(`${origin}${path}?section=${section}`);
      assert.ok(
        await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth),
        `${section} overflows at ${width}`,
      );

      if (SCREENSHOT_WIDTHS.includes(width)) {
        await page.screenshot({
          path: join(output, `phase2-${section}-${width}.png`),
          fullPage: true,
        });
      }
    }
  }
}

async function checkLegacySharingLink({ page, origin, path }) {
  await page.goto(`${origin}${path}#sharing`);
  await page.waitForURL(`**?section=sharing`);
  assert.equal(await activeSubnavLabel(page), "Allgemein");
  assert.equal(await page.locator("#sharing").isVisible(), true);
}

async function checkRevocationProtectsReaderDraft(workspace) {
  const { page, reader, origin, path, mutate } = workspace;
  const readerPage = reader.page;

  // The reader starts a draft, then loses access before saving it.
  await readerPage.goto(`${origin}${path}?section=criteria`);
  await readerPage.locator("#add-group").click();
  await readerPage.locator("#new-group-editor").getByLabel("Gruppenname").fill(READER_GROUP_DRAFT);
  await mutate(page, () =>
    page.locator(".share-row").getByRole("button", { name: "Entziehen", exact: true }).click(),
  );

  const deniedPost = readerPage.waitForResponse(
    (response) =>
      response.request().method() === "POST" && new URL(response.url()).pathname === path,
  );
  await readerPage.locator("#save-group-new").click();
  assert.equal((await deniedPost).status(), 403);

  await readerPage.locator("#new-group-editor .editor-error:not([hidden])").waitFor();
  assert.equal(await readerPage.locator("#group-name-new").inputValue(), READER_GROUP_DRAFT);
  assert.equal(await readerPage.locator("#save-group-new").isDisabled(), true);
  assert.equal((await exportCase(workspace)).groups.length, 1);

  // Remove the stale page without interacting with any other user's data.
  await readerPage.goto(origin);
}

// ── Deletion ────────────────────────────────────────────────────────────────

/** Deletes one criterion, dissolves the group and returns the export of the remaining state. */
async function checkCriterionDeletionAndGroupDissolving(workspace, groupId, criterionId) {
  const { page, mutate, record } = workspace;
  const editor = criterionEditorOf(page, criterionId);
  const editGroup = groupEditorOf(page, groupId);
  const deleteCriterionButton = editor.getByRole("button", {
    name: "Kriterium löschen",
    exact: true,
  });

  await page.getByRole("link", { name: "Grundlagen", exact: true }).click();
  await openSubnav(page, "Kriterien");

  // The confirmation names the criterion; cancelling deletes nothing.
  await page.locator(`#edit-criterion-${criterionId}`).click();
  await deleteCriterionButton.click();
  assert.match(
    await page.locator("#confirmation-title").innerText(),
    /Lokaler Entwurf bleibt erhalten/,
  );
  await page.keyboard.press("Escape");
  assert.equal((await exportCase(workspace)).criteria.length, 2);

  await deleteCriterionButton.click();
  await mutate(page, () => confirmAccept(page).click());
  assert.equal((await exportCase(workspace)).criteria.length, 1);
  assert.equal(await isFocused(page.locator("#criteria-title")), true);

  await page.locator(`#edit-group-${groupId}`).click();
  await editGroup.getByRole("button", { name: "Gruppe auflösen", exact: true }).click();
  await mutate(page, () => confirmAccept(page).click());

  const ungrouped = await exportCase(workspace);
  assert.equal(ungrouped.groups.length, 0);
  assert.equal(ungrouped.criteria.length, 1);
  assert.equal(ungrouped.criteria[0].groupId, null);

  record(
    "Criterion deletion targets only the confirmed criterion; dissolving a group retains its remaining criteria",
  );

  return ungrouped;
}

async function checkEvaluationDeletion(workspace) {
  const { page, origin, path, browserRequest, record } = workspace;
  const deleteButton = page.getByRole("button", { name: "Evaluation löschen", exact: true });
  const confirmation = page.locator("#action-confirmation");

  await openSubnav(page, "Allgemein");

  // The confirmation names the evaluation and focuses the safe choice; cancelling keeps it.
  await deleteButton.click();
  assert.match(await confirmation.innerText(), new RegExp(EVALUATION_NAME));
  assert.equal(await isFocused(confirmation.locator("[data-confirm-cancel]")), true);
  await page.keyboard.press("Escape");
  assert.equal((await exportCase(workspace)).name, EVALUATION_NAME);

  await deleteButton.click();
  await confirmation.locator("[data-confirm-accept]").click();
  await page.waitForURL(origin + "/");
  assert.equal((await browserRequest(page, path)).status(), 404);

  record(
    "Evaluation deletion names its subject, safely cancels, and redirects only after confirmed removal",
  );
}

// ── Legacy import ───────────────────────────────────────────────────────────

async function checkLegacyImport({ page, browserRequest, mutate, record }, ungrouped) {
  // Exports of an older format version carry no country.
  const legacy = { ...ungrouped, name: "Imported legacy evaluation", version: 7 };
  delete legacy.country;

  await page.locator("#import-evaluation").click();
  await page.locator("#upload").setInputFiles({
    name: "legacy.json",
    mimeType: "application/json",
    buffer: Buffer.from(JSON.stringify(legacy)),
  });
  await page.locator("#save-import").click();
  await page.waitForURL(/\/evaluations\//);
  const importedPath = new URL(page.url()).pathname;

  // The notice leads to the properties, where only the country is missing.
  const notice = page.locator("[data-regional-settings-notice]");
  const countrySelect = page.locator("#case-region-country");
  await notice.getByRole("link").click();
  assert.equal(await page.locator("#evaluation-editor").isVisible(), true);
  assert.equal(await countrySelect.inputValue(), "");
  assert.equal(await page.locator("#case-region-currency").inputValue(), "CHF");

  await countrySelect.selectOption("LI");
  await mutate(page, () => page.locator("#save-evaluation").click());
  assert.equal(await notice.count(), 0);

  const completed = JSON.parse(
    await (await browserRequest(page, `${importedPath}?handler=Export`)).text(),
  );
  assert.equal(completed.country, "LI");
  assert.equal(completed.currency, "CHF");
  assert.equal(completed.version, 9);

  record(
    "Legacy imports keep their currency and unknown country, then allow explicit completion in properties",
  );
}
