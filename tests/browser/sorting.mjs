import assert from "node:assert/strict";
import { join } from "node:path";

const MOBILE_VIEWPORT = { width: 390, height: 844 };
const TOUCH_PAGE_TIMEOUT_MS = 10_000;

const GROUP_NAMES = ["Schlüssel", "Alltag", "Komfort"];

// The first group receives these criteria; all further ones are numbered and go to the second group.
const KEY_CRITERION_NAMES = [
  "NFC Karte",
  "Standardschlüssel",
  "UWB Smart Key",
  "App Key (Bluetooth)",
  "Notschlüssel",
];
const CRITERION_COUNT = 23;
const OFFER_CRITERION_NAMES = ["Gratis Service", "Winterräder"];

// The first group once "NFC Karte" has been moved elsewhere.
const KEY_CRITERIA_WITHOUT_NFC = [
  "Standardschlüssel",
  "UWB Smart Key",
  "App Key (Bluetooth)",
  "Notschlüssel",
];

const MAX_DESKTOP_ROW_HEIGHT = 90;

// How far the list may shift in the viewport after a sorting arrow was used, and how long to let it settle.
const VIEWPORT_SETTLE_MS = 100;
const VIEWPORT_TOLERANCE_PX = 2;

// Pointer drag: while the drop target lies outside the comfortable band, the pointer is held near
// the top or bottom edge of the window so that the page scrolls by itself.
const DROP_BAND_TOP = 90;
const DROP_BAND_BOTTOM = 950;
const REACHABLE_BAND_TOP = 80;
const REACHABLE_BAND_BOTTOM = 960;
const POINTER_Y_AT_TOP_EDGE = 10;
const POINTER_Y_AT_BOTTOM_EDGE = 1040;
const AUTO_SCROLL_MAX_ROUNDS = 160;
const AUTO_SCROLL_PAUSE_MS = 50;
const AUTO_SCROLL_MOVE_STEPS = 2;
const DROP_MOVE_STEPS = 12;
const DROP_EDGE_INSET = 4;

/**
 * Checks sorting of criteria and groups with arrows, keyboard, mouse and touch in an evaluation
 * of its own, which is removed again at the end.
 * The steps build on each other and must run in this order.
 */
export async function checkSorting({ page, owner, origin, output, post, mutate, record }) {
  const returnUrl = page.url();

  const path = await createEvaluation(page, origin);
  // `version` is the evaluation version the next seeded write is based on; seedChange advances it.
  const context = { page, owner, origin, output, post, mutate, record, path, version: null };

  const groupIds = await seedGroupsAndCriteria(context);

  await checkCompactRowsAndArrows(context);
  const nfcId = await checkCriterionDragging(context, groupIds);
  await checkGroupDraggingAndEmptyGroups(context, groupIds, nfcId);
  await checkScopeSections(context, groupIds);
  await checkTouchDragging(context);
  await checkPlacementValidationAndRemoveEvaluation(context, groupIds, nfcId);

  await page.goto(returnUrl);
}

// ---------------------------------------------------------------------------
// Locators and readings
// ---------------------------------------------------------------------------

function groupSection(page, groupId) {
  return page.locator(`.criterion-group[data-group-id="${groupId}"]`);
}

function criterionRow(page, name) {
  return page
    .locator(".criterion")
    .filter({ has: page.getByRole("heading", { name, exact: true }) });
}

function criterionDragHandle(page, name) {
  return criterionRow(page, name).locator("[data-drag-handle]");
}

function criterionNamesInGroup(page, groupId) {
  return groupSection(page, groupId).locator(".criterion h4").allTextContents();
}

function groupIdsInDisplayOrder(page) {
  return page
    .locator('.criterion-group[data-sort-kind="group"]')
    .evaluateAll((groups) => groups.map((group) => group.dataset.groupId));
}

function scopeSection(page, scopeName) {
  return page.locator(`.criteria-scope[data-criteria-scope="${scopeName}"]`);
}

function criterionNamesInScope(page, scopeName, groupId) {
  return scopeSection(page, scopeName)
    .locator(`.criterion-group[data-group-id="${groupId}"] .criterion h4`)
    .allTextContents();
}

function readWorkspaceVersion(page) {
  return page.locator("#evaluation-workspace").getAttribute("data-version");
}

function isFocused(locator) {
  return locator.evaluate((element) => element === document.activeElement);
}

function listPositionInViewport(page) {
  return page.evaluate(() => ({
    y: scrollY,
    top: document.querySelector(".criteria-main").getBoundingClientRect().top,
  }));
}

async function assertListStayedInPlace(page, before) {
  await page.waitForTimeout(VIEWPORT_SETTLE_MS);
  const after = await listPositionInViewport(page);

  assert.ok(
    Math.abs(before.top - after.top) < VIEWPORT_TOLERANCE_PX,
    `List moved in viewport: ${JSON.stringify({ before, after })}`,
  );
}

// ---------------------------------------------------------------------------
// Writes and pointer dragging
// ---------------------------------------------------------------------------

/** Posts a change directly and continues with the version the answer reports. */
async function seedChange(context, values) {
  const { page, post, path } = context;

  const response = await post(page, path, { ...values, version: context.version });
  assert.equal(response.status(), 200);
  context.version = (await response.text()).match(/data-version="(\d+)"/)[1];
}

function isOutsideDropBand(box) {
  return box.y < DROP_BAND_TOP || box.y + box.height > DROP_BAND_BOTTOM;
}

/**
 * Drags with the mouse from the source to the upper ("before") or lower ("after") edge of the
 * target, letting the page scroll by itself while the target is out of reach.
 */
async function dragWithMouse({ page, mutate }, source, target, edge = "before") {
  await source.scrollIntoViewIfNeeded();
  const start = await source.boundingBox();
  await page.mouse.move(start.x + start.width / 2, start.y + start.height / 2);
  await page.mouse.down();

  let end = await target.boundingBox();
  for (let round = 0; round < AUTO_SCROLL_MAX_ROUNDS && isOutsideDropBand(end); round++) {
    const pointerY = end.y < DROP_BAND_TOP ? POINTER_Y_AT_TOP_EDGE : POINTER_Y_AT_BOTTOM_EDGE;
    await page.mouse.move(start.x + start.width / 2, pointerY, { steps: AUTO_SCROLL_MOVE_STEPS });
    await page.waitForTimeout(AUTO_SCROLL_PAUSE_MS);
    end = await target.boundingBox();
  }
  assert.ok(
    end.y >= REACHABLE_BAND_TOP && end.y + end.height <= REACHABLE_BAND_BOTTOM,
    "Auto-scroll must bring the drop target into view",
  );

  const dropY = edge === "before" ? end.y + DROP_EDGE_INSET : end.y + end.height - DROP_EDGE_INSET;
  await page.mouse.move(end.x + end.width / 2, dropY, { steps: DROP_MOVE_STEPS });
  await page.locator(".sorting-marker").waitFor({ state: "visible" });
  await mutate(page, () => page.mouse.up());
}

// ---------------------------------------------------------------------------
// Preparation: evaluation with three groups and a long list of criteria
// ---------------------------------------------------------------------------

async function createEvaluation(page, origin) {
  await page.goto(origin);
  await page.getByRole("button", { name: /Neue Evaluation/ }).click();
  await page
    .getByRole("dialog", { name: "Neue Evaluation", exact: true })
    .getByLabel("Name der Evaluation")
    .fill("Anforderungen übersichtlich ordnen");
  await page.locator("#new-region-country").selectOption("CH");
  await page.locator("#new-region-currency").fill("EUR");
  await page
    .getByRole("dialog", { name: "Neue Evaluation", exact: true })
    .getByRole("button", { name: "Evaluation anlegen" })
    .click();
  await page.waitForURL(/\/evaluations\//);

  return new URL(page.url()).pathname;
}

/** Returns the ids of the three groups in their initial order. */
async function seedGroupsAndCriteria(context) {
  const { page, origin, path } = context;

  await page.goto(`${origin}${path}?section=criteria`);
  context.version = await readWorkspaceVersion(page);

  for (const name of GROUP_NAMES) {
    await seedChange(context, { action: "add-group", name });
  }
  await page.reload();
  const groupIds = await groupIdsInDisplayOrder(page);

  for (let index = 0; index < CRITERION_COUNT; index++) {
    const isKeyCriterion = index < KEY_CRITERION_NAMES.length;

    await seedChange(context, {
      action: "add-criterion",
      name: KEY_CRITERION_NAMES[index] || `Anforderung ${index + 1}`,
      type: "Boolean",
      priority: index % 2 ? "Required" : "Preferred",
      scope: "Vehicle",
      groupId: groupIds[isKeyCriterion ? 0 : 1],
    });
  }
  await page.reload();

  return groupIds;
}

// ---------------------------------------------------------------------------
// Compact rows, sorting arrows and keyboard
// ---------------------------------------------------------------------------

async function checkCompactRowsAndArrows({ page, output, mutate, record }) {
  await page.locator(".criteria-main").scrollIntoViewIfNeeded();
  await page.screenshot({ path: join(output, "08-compact-desktop.png") });
  const rowHeight = await criterionRow(page, "NFC Karte").evaluate(
    (element) => element.getBoundingClientRect().height,
  );
  assert.ok(rowHeight <= MAX_DESKTOP_ROW_HEIGHT, `Desktop row too tall: ${rowHeight}`);

  // The arrow keeps the focus after the list was re-rendered, so Enter moves the row once more.
  const upArrow = page.getByRole("button", {
    name: "Kriterium Anforderung 14 nach oben",
    exact: true,
  });
  await upArrow.scrollIntoViewIfNeeded();
  const positionBeforeMove = await listPositionInViewport(page);
  await mutate(page, () => upArrow.click());
  await assertListStayedInPlace(page, positionBeforeMove);
  assert.equal(await isFocused(upArrow), true);
  await mutate(page, () => page.keyboard.press("Enter"));

  const keyboardHandle = criterionDragHandle(page, "Anforderung 14");
  await keyboardHandle.focus();
  await mutate(page, () => page.keyboard.press("ArrowDown"));
  assert.equal(await isFocused(keyboardHandle), true);

  record("Compact rows; sorting arrows preserve viewport and keyboard focus on a long page");
}

// ---------------------------------------------------------------------------
// Dragging a criterion within its group; cancelled drags
// ---------------------------------------------------------------------------

/** Returns the id of "NFC Karte", which later checks use to prove the row kept its identity. */
async function checkCriterionDragging(context, groupIds) {
  const { page, mutate, record } = context;
  const keyGroupId = groupIds[0];

  const nfcId = await criterionRow(page, "NFC Karte").getAttribute("data-criterion-id");
  await page.evaluate(() => window.scrollTo(0, 200));
  await dragWithMouse(
    context,
    criterionDragHandle(page, "NFC Karte"),
    criterionRow(page, "Notschlüssel"),
    "after",
  );
  assert.equal((await criterionNamesInGroup(page, keyGroupId)).at(-1), "NFC Karte");
  await dragWithMouse(
    context,
    criterionDragHandle(page, "NFC Karte"),
    criterionRow(page, "Standardschlüssel"),
  );
  assert.equal((await criterionNamesInGroup(page, keyGroupId))[0], "NFC Karte");

  // Sorting another row must not close an open editor or lose its unsaved text.
  const draftRow = criterionRow(page, "UWB Smart Key");
  await draftRow.locator("[data-editor-open]").click();
  await draftRow.getByLabel("Bezeichnung").fill("Noch nicht gespeichert");
  await mutate(page, () =>
    page.getByRole("button", { name: "Kriterium NFC Karte nach unten", exact: true }).click(),
  );
  assert.equal(await draftRow.locator("[data-editor]").isVisible(), true);
  assert.equal(await draftRow.getByLabel("Bezeichnung").inputValue(), "Noch nicht gespeichert");
  await draftRow.getByRole("button", { name: "Abbrechen", exact: true }).click();
  await page.getByRole("button", { name: "Änderungen verwerfen", exact: true }).click();

  // Escape and dropping outside the list must not send a mutation.
  const versionBeforeCancelledDrags = await readWorkspaceVersion(page);
  const handle = criterionDragHandle(page, "NFC Karte");
  await handle.scrollIntoViewIfNeeded();
  const handleBox = await handle.boundingBox();

  await page.mouse.move(handleBox.x + 12, handleBox.y + 15);
  await page.mouse.down();
  await page.mouse.move(handleBox.x + 12, handleBox.y + 70, { steps: 4 });
  await page.keyboard.press("Escape");
  await page.mouse.up();
  assert.equal(await page.locator(".sorting-marker").count(), 0);
  assert.equal(await readWorkspaceVersion(page), versionBeforeCancelledDrags);

  await page.mouse.move(handleBox.x + 12, handleBox.y + 15);
  await page.mouse.down();
  await page.mouse.move(100, 300, { steps: 6 });
  await page.mouse.up();
  assert.equal(await page.locator(".sorting-marker").count(), 0);
  assert.equal(await readWorkspaceVersion(page), versionBeforeCancelledDrags);

  record(
    "Drag sorting in both directions preserves identity and open drafts; Escape/outside drops cancel",
  );

  return nfcId;
}

// ---------------------------------------------------------------------------
// Dragging groups, moving criteria between groups, the ungrouped section
// ---------------------------------------------------------------------------

async function checkGroupDraggingAndEmptyGroups(context, groupIds, nfcId) {
  const { page, mutate, record } = context;
  const [keyGroupId, everydayGroupId, comfortGroupId] = groupIds;
  const ungroupedId = "";

  // Drag a group across another group using its header, then move a criterion into the empty group.
  await groupSection(page, comfortGroupId).locator(".group-heading").scrollIntoViewIfNeeded();
  await page.evaluate(() => window.scrollBy(0, 200));
  await dragWithMouse(
    context,
    groupSection(page, comfortGroupId).locator(".group-heading [data-drag-handle]"),
    groupSection(page, everydayGroupId).locator(".group-heading"),
    "before",
  );
  assert.deepEqual(await groupIdsInDisplayOrder(page), [
    keyGroupId,
    comfortGroupId,
    everydayGroupId,
  ]);

  await page.evaluate(() => window.scrollTo(0, 220));
  await dragWithMouse(
    context,
    criterionDragHandle(page, "NFC Karte"),
    groupSection(page, comfortGroupId).locator(".criterion-list"),
  );
  assert.deepEqual(await criterionNamesInGroup(page, comfortGroupId), ["NFC Karte"]);
  assert.equal(await criterionRow(page, "NFC Karte").getAttribute("data-criterion-id"), nfcId);
  assert.equal(
    await criterionRow(page, "NFC Karte").getByLabel("Gruppe", { exact: true }).inputValue(),
    comfortGroupId,
  );

  await page.reload();
  assert.deepEqual(await criterionNamesInGroup(page, comfortGroupId), ["NFC Karte"]);
  assert.deepEqual(await groupIdsInDisplayOrder(page), [
    keyGroupId,
    comfortGroupId,
    everydayGroupId,
  ]);
  assert.deepEqual(await criterionNamesInGroup(page, keyGroupId), KEY_CRITERIA_WITHOUT_NFC);

  const groupUpArrow = page.getByRole("button", { name: "Gruppe Alltag nach oben", exact: true });
  await groupUpArrow.scrollIntoViewIfNeeded();
  const positionBeforeGroupMove = await listPositionInViewport(page);
  await mutate(page, () => groupUpArrow.click());
  await assertListStayedInPlace(page, positionBeforeGroupMove);
  await mutate(page, () =>
    page.getByRole("button", { name: "Gruppe Alltag nach unten", exact: true }).click(),
  );

  // The ungrouped section exists only while it contains a criterion; empty named groups stay visible.
  assert.equal(await groupSection(page, ungroupedId).count(), 0);
  assert.equal(await page.getByText("Keine Note für deine Entscheidung.").count(), 0);
  context.version = await readWorkspaceVersion(page);
  await seedChange(context, {
    action: "place-criterion",
    itemId: nfcId,
    groupId: "",
    position: "0",
  });
  await page.reload();
  assert.deepEqual(await criterionNamesInGroup(page, ungroupedId), ["NFC Karte"]);
  assert.deepEqual(await criterionNamesInGroup(page, comfortGroupId), []);
  assert.equal(await groupSection(page, comfortGroupId).locator(".group-empty").count(), 1);

  await dragWithMouse(
    context,
    criterionDragHandle(page, "Standardschlüssel"),
    groupSection(page, ungroupedId).locator(".criterion-list"),
  );
  assert.deepEqual((await criterionNamesInGroup(page, ungroupedId)).sort(), [
    "NFC Karte",
    "Standardschlüssel",
  ]);
  await dragWithMouse(
    context,
    criterionDragHandle(page, "Standardschlüssel"),
    criterionRow(page, "UWB Smart Key"),
  );
  assert.deepEqual(await criterionNamesInGroup(page, keyGroupId), KEY_CRITERIA_WITHOUT_NFC);

  await dragWithMouse(
    context,
    criterionDragHandle(page, "NFC Karte"),
    groupSection(page, comfortGroupId).locator(".criterion-list"),
  );
  assert.deepEqual(await criterionNamesInGroup(page, comfortGroupId), ["NFC Karte"]);
  assert.equal(await groupSection(page, ungroupedId).count(), 0);
  await page.reload();
  assert.equal(await groupSection(page, ungroupedId).count(), 0);

  record(
    "Group arrows preserve the viewport; drag sorting and empty groups persist; the ungrouped section appears only with criteria",
  );
}

// ---------------------------------------------------------------------------
// Separate sections for vehicle and offer criteria
// ---------------------------------------------------------------------------

async function checkScopeSections(context, groupIds) {
  const { page, output, mutate, record } = context;
  const keyGroupId = groupIds[0];

  // Vehicle and offer criteria are listed in separate sections; sorting stays within one kind.
  context.version = await readWorkspaceVersion(page);
  for (const name of OFFER_CRITERION_NAMES) {
    await seedChange(context, {
      action: "add-criterion",
      name,
      type: "Boolean",
      priority: "Preferred",
      scope: "Offer",
      groupId: keyGroupId,
    });
  }
  await page.reload();

  assert.deepEqual(await page.locator(".scope-heading h3").allTextContents(), [
    "Fahrzeugkriterien",
    "Offertenkriterien",
  ]);
  assert.deepEqual(
    await criterionNamesInScope(page, "Vehicle", keyGroupId),
    KEY_CRITERIA_WITHOUT_NFC,
  );
  assert.deepEqual(await criterionNamesInScope(page, "Offer", keyGroupId), [
    "Gratis Service",
    "Winterräder",
  ]);
  assert.equal(await scopeSection(page, "Offer").locator(".criterion-group").count(), 1);
  assert.equal(
    await page
      .locator(".criterion-copy p")
      .filter({ hasText: /Offerte|Fahrzeug/ })
      .count(),
    0,
  );

  // Element ids must stay unique across both sections.
  assert.equal(
    await page
      .locator("[id]")
      .evaluateAll((nodes) => nodes.length - new Set(nodes.map((node) => node.id)).size),
    0,
  );

  assert.equal(
    await page
      .getByRole("button", { name: "Kriterium Gratis Service nach oben", exact: true })
      .isDisabled(),
    true,
  );
  await mutate(page, () =>
    page.getByRole("button", { name: "Kriterium Winterräder nach oben", exact: true }).click(),
  );
  assert.deepEqual(await criterionNamesInScope(page, "Offer", keyGroupId), [
    "Winterräder",
    "Gratis Service",
  ]);
  assert.deepEqual(
    await criterionNamesInScope(page, "Vehicle", keyGroupId),
    KEY_CRITERIA_WITHOUT_NFC,
  );

  // A criterion cannot be dropped among criteria of the other kind.
  await criterionRow(page, "Notschlüssel").scrollIntoViewIfNeeded();
  const foreignHandleBox = await criterionDragHandle(page, "Notschlüssel").boundingBox();
  const foreignTargetBox = await criterionRow(page, "Winterräder").boundingBox();
  await page.mouse.move(
    foreignHandleBox.x + foreignHandleBox.width / 2,
    foreignHandleBox.y + foreignHandleBox.height / 2,
  );
  await page.mouse.down();
  await page.mouse.move(foreignTargetBox.x + 80, foreignTargetBox.y + 4, { steps: 8 });
  assert.equal(await page.locator(".sorting-marker").isHidden(), true);
  await page.mouse.up();
  assert.deepEqual(await criterionNamesInScope(page, "Offer", keyGroupId), [
    "Winterräder",
    "Gratis Service",
  ]);
  assert.deepEqual(
    await criterionNamesInScope(page, "Vehicle", keyGroupId),
    KEY_CRITERIA_WITHOUT_NFC,
  );
  await page.screenshot({ path: join(output, "criteria-sections.png"), fullPage: true });

  // Remove the offer criteria again so that later checks see vehicle criteria only.
  context.version = await readWorkspaceVersion(page);
  for (const name of OFFER_CRITERION_NAMES) {
    await seedChange(context, {
      action: "remove-criterion",
      itemId: await criterionRow(page, name).getAttribute("data-criterion-id"),
    });
  }

  record(
    "Vehicle and offer criteria form separate sections; arrows and dragging stay within one kind",
  );
}

// ---------------------------------------------------------------------------
// Touch dragging at phone width
// ---------------------------------------------------------------------------

async function checkTouchDragging({ owner, origin, output, mutate, record, path }) {
  // Exercise real touch input, not synthetic drag events.
  const touchContext = await owner.context.browser().newContext({
    viewport: MOBILE_VIEWPORT,
    hasTouch: true,
    isMobile: true,
    ignoreHTTPSErrors: origin.startsWith("https:"),
  });
  await touchContext.addCookies(await owner.context.cookies());
  const touchPage = await touchContext.newPage();
  touchPage.setDefaultTimeout(TOUCH_PAGE_TIMEOUT_MS);
  const pageErrors = [];
  touchPage.on("pageerror", (error) => pageErrors.push(error.message));

  await touchPage.goto(`${origin}${path}?section=criteria`);
  assert.deepEqual(await touchPage.locator(".scope-heading h3").allTextContents(), [
    "Fahrzeugkriterien",
  ]);
  assert.ok(await touchPage.evaluate(() => document.documentElement.scrollWidth <= innerWidth));

  const sourceHandle = touchPage.getByRole("button", {
    name: "Kriterium Standardschlüssel verschieben",
    exact: true,
  });
  await sourceHandle.evaluate((element) =>
    window.scrollBy(0, element.getBoundingClientRect().top - 250),
  );
  const sourceBounds = await sourceHandle.boundingBox();
  const targetBounds = await touchPage
    .locator(".criterion")
    .filter({ has: touchPage.getByRole("heading", { name: "UWB Smart Key", exact: true }) })
    .boundingBox();

  // Finger down on the handle, move to just above the lower edge of the next row, lift.
  const devTools = await touchContext.newCDPSession(touchPage);
  const touchStart = {
    x: sourceBounds.x + sourceBounds.width / 2,
    y: sourceBounds.y + sourceBounds.height / 2,
  };
  await devTools.send("Input.dispatchTouchEvent", {
    type: "touchStart",
    touchPoints: [touchStart],
  });
  await devTools.send("Input.dispatchTouchEvent", {
    type: "touchMove",
    touchPoints: [{ x: touchStart.x, y: targetBounds.y + targetBounds.height - 8 }],
  });
  await touchPage.locator(".sorting-marker").waitFor({ state: "visible" });
  await mutate(touchPage, () =>
    devTools.send("Input.dispatchTouchEvent", { type: "touchEnd", touchPoints: [] }),
  );
  assert.equal(await touchPage.locator(".criterion h4").first().textContent(), "UWB Smart Key");

  await touchPage
    .locator(".criteria-main")
    .evaluate((element) => window.scrollTo(0, element.getBoundingClientRect().top + scrollY - 20));
  await touchPage.screenshot({ path: join(output, "09-compact-mobile.png") });
  assert.deepEqual(pageErrors, []);
  await touchContext.close();

  record("390px layout and touch drag sorting work without horizontal overflow");
}

// ---------------------------------------------------------------------------
// Rejected placements, then removal of the evaluation
// ---------------------------------------------------------------------------

async function checkPlacementValidationAndRemoveEvaluation(context, groupIds, nfcId) {
  const { page, post, path } = context;
  const keyGroupId = groupIds[0];

  await page.reload();
  context.version = await readWorkspaceVersion(page);

  const outdatedPlacement = await post(page, path, {
    action: "place-criterion",
    itemId: nfcId,
    groupId: keyGroupId,
    position: "0",
    version: "1",
  });
  assert.equal(outdatedPlacement.status(), 409);

  const placementBeyondEnd = await post(page, path, {
    action: "place-criterion",
    itemId: nfcId,
    groupId: keyGroupId,
    position: "999",
    version: context.version,
  });
  assert.equal(placementBeyondEnd.status(), 422);

  await post(page, path, { action: "delete", version: context.version });
}
