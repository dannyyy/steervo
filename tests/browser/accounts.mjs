import assert from "node:assert/strict";
import { join } from "node:path";

const DESKTOP_VIEWPORT = { width: 1440, height: 1050 };
const MOBILE_VIEWPORT = { width: 390, height: 844 };
const OVERFLOW_CHECK_WIDTHS = [320, 390, 768, 1024, 1440];
const OVERFLOW_CHECK_HEIGHT = 1000;

const LOGIN_REQUEST_PATTERN = "**/Auth/Login?*";
const INVITATIONS_REQUEST_PATTERN = "**/Account/Invitations";
const PASSKEY_OPTIONS_REQUEST_PATTERN = "**/Account?handler=Options";
const ACCOUNT_REQUEST_PATTERN = "**/Account";

const RETAINED_EMAIL = "retained@example.test";
const PASSKEY_DRAFT_NAME = "Persönlicher Sicherheitsschlüssel";
const EXPIRED_SESSION_DRAFT_NAME = "Draft after session expiry";

// ── Shared interaction helpers ──────────────────────────────────────────────

async function errorVisible(form) {
  await form.locator(".editor-error:not([hidden])").waitFor();
}

async function discard(page) {
  await page.keyboard.press("Escape");
  await page.locator("[data-confirm-accept]").click();
}

async function screenshot(page, output, name) {
  await page.screenshot({ path: join(output, `account-${name}.png`), fullPage: true });
}

function isFocused(locator) {
  return locator.evaluate((node) => document.activeElement === node);
}

/** Resizes through the supported widths and fails when the document scrolls horizontally. */
async function assertNoOverflowAtSupportedWidths(page) {
  for (const width of OVERFLOW_CHECK_WIDTHS) {
    await page.setViewportSize({ width, height: OVERFLOW_CHECK_HEIGHT });

    assert.ok(
      await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth),
      `Page overflow at ${width}px: ${page.url()}`,
    );
  }
}

/**
 * Checks all widths, then captures the desktop and the mobile screenshot.
 * The page is left at the mobile viewport.
 */
async function captureDesktopAndMobile(page, output, name) {
  await assertNoOverflowAtSupportedWidths(page);
  await screenshot(page, output, `${name}-desktop`);
  await page.setViewportSize(MOBILE_VIEWPORT);
  await screenshot(page, output, `${name}-mobile`);
}

function abortPostRequests(route) {
  return route.request().method() === "POST" ? route.abort() : route.continue();
}

// ── Installation setup ──────────────────────────────────────────────────────

export async function checkSetup({ page, token, output, record }) {
  await captureDesktopAndMobile(page, output, "setup");

  const form = page.locator("#setup-form");
  const tokenInput = page.getByLabel("Einrichtungsschlüssel");

  await tokenInput.fill("invalid-installation-token");
  await page.getByRole("button", { name: "E-Mail-Adresse bestätigen" }).click();
  await errorVisible(form);

  assert.equal(await page.getByLabel("Name", { exact: true }).inputValue(), "Test Owner");
  assert.equal(await tokenInput.inputValue(), "invalid-installation-token");
  assert.equal(await isFocused(form.locator(".editor-error")), true);

  await tokenInput.fill(token);
  await page.setViewportSize(DESKTOP_VIEWPORT);
  record(
    "Setup keeps account fields and focuses server validation; responsive entry at five widths",
  );
}

// ── Email code verification ─────────────────────────────────────────────────

export async function checkVerify({ page, code, output, record }) {
  const form = page.locator("#verify-form");
  const codeInput = page.getByLabel("Einmalcode");
  const signInButton = page.getByRole("button", { name: "Anmelden", exact: true });
  const invalid = code === "111111" ? "222222" : "111111";

  await codeInput.fill(invalid);
  await signInButton.click();
  await errorVisible(form);

  assert.equal(await codeInput.inputValue(), invalid);
  assert.equal(await signInButton.isEnabled(), true);

  await captureDesktopAndMobile(page, output, "verify-error");
  await page.setViewportSize(DESKTOP_VIEWPORT);
  record("Incorrect verification code preserves input, local error focus and retry");
}

// ── Login, system states and invitations ────────────────────────────────────

export async function checkAccess({ page, reader, newUser, origin, output, record }) {
  await checkLoginRetainsInput({ newUser, origin, output, record });
  await checkSystemStatesAndReturnUrl({ page, reader, origin, output, record });
  await checkInvitationEditor({ page, origin, output, record });
}

async function checkLoginRetainsInput({ newUser, origin, output, record }) {
  const anonymous = await newUser();
  const login = anonymous.page;

  await login.goto(`${origin}/Auth/Login?ReturnUrl=%2FAccount`);
  await captureDesktopAndMobile(login, output, "login");

  const form = login.locator("#login-code-form");
  const emailInput = login.getByLabel("E-Mail-Adresse", { exact: true });
  const sendCodeButton = login.getByRole("button", { name: "Anmeldecode senden" });

  // Offline: the request fails locally and the address stays in the field.
  await emailInput.fill(RETAINED_EMAIL);
  await anonymous.context.setOffline(true);
  await sendCodeButton.click();
  await errorVisible(form);
  assert.equal(await emailInput.inputValue(), RETAINED_EMAIL);
  await anonymous.context.setOffline(false);

  // Hold the next request open so the pending state can be observed, then answer with 429.
  let releaseDelayedResponse;
  const delayedResponse = new Promise((resolve) => {
    releaseDelayedResponse = resolve;
  });
  let loginRequests = 0;

  await login.route(LOGIN_REQUEST_PATTERN, async (route) => {
    if (route.request().method() !== "POST") {
      return route.continue();
    }

    loginRequests++;
    await delayedResponse;
    await route.fulfill({ status: 429, body: "" });
  });

  await sendCodeButton.click();
  await form.locator(".request-status:not([hidden])").waitFor();
  assert.equal(await form.getAttribute("aria-busy"), "true");
  assert.equal(await sendCodeButton.isDisabled(), true);

  await form.evaluate((node) => {
    node.requestSubmit();
    node.requestSubmit();
  });
  releaseDelayedResponse();

  await form.locator(".editor-error").filter({ hasText: "Zu viele Anfragen" }).waitFor();
  assert.equal(loginRequests, 1);
  assert.equal(await emailInput.inputValue(), RETAINED_EMAIL);
  assert.notEqual(await form.getAttribute("aria-busy"), "true");

  await login.unroute(LOGIN_REQUEST_PATTERN);
  record(
    "Login retains input offline and on rate limits; pending requests reject duplicate submission",
  );
  await anonymous.context.close();
}

async function checkSystemStatesAndReturnUrl({ page, reader, origin, output, record }) {
  const notFound = await page.goto(`${origin}/missing-browser-page`);
  assert.equal(notFound.status(), 404);
  await page.locator(".system-state").waitFor();
  assert.equal(await page.locator(".app-navigation [aria-current=page]").count(), 0);

  await captureDesktopAndMobile(page, output, "not-found");
  await page.setViewportSize(DESKTOP_VIEWPORT);

  assert.equal((await reader.page.goto(`${origin}/Account/Invitations`)).status(), 403);
  await reader.page.locator(".system-state").waitFor();

  for (const status of [429, 500]) {
    assert.equal((await page.goto(`${origin}/Error?statusCode=${status}`)).status(), status);
    await page.locator(".system-state").waitFor();
  }

  // An external return destination is ignored, a local one is followed.
  await page.goto(`${origin}/Auth/Login?ReturnUrl=https%3A%2F%2Fexample.invalid`);
  await page.waitForURL(origin + "/");
  await page.goto(`${origin}/Auth/Login?ReturnUrl=%2FAccount`);
  await page.waitForURL(origin + "/Account");

  record(
    "Full-page 403/404 and explicit 429/500 states preserve HTTP status; login only accepts local return destinations",
  );
}

async function checkInvitationEditor({ page, origin, output, record }) {
  await page.goto(`${origin}/Account/Invitations`);

  const inviteButton = page.locator("#invite-person");
  const editor = page.locator("#invitation-editor");
  const nameInput = editor.getByLabel("Name", { exact: true });
  const emailInput = editor.getByLabel("E-Mail-Adresse");
  const sendButton = page.locator("#send-invitation");
  const confirmCancel = page.locator("[data-confirm-cancel]");

  // Opening focuses the first field; an untouched editor closes back onto its opener.
  await inviteButton.click();
  assert.equal(await isFocused(nameInput), true);
  await page.keyboard.press("Escape");
  assert.equal(await isFocused(inviteButton), true);

  // Server validation keeps the draft.
  await inviteButton.click();
  await nameInput.fill("   ");
  const inviteEmail = `pending-${Date.now()}@example.test`;
  await emailInput.fill(inviteEmail);
  await sendButton.click();
  await errorVisible(editor);
  assert.equal(await emailInput.inputValue(), inviteEmail);

  // Escape on a changed draft asks first; cancelling keeps the draft.
  await nameInput.fill("Eine Person mit einem bewusst langen Namen für den gemeinsamen Vergleich");
  await page.keyboard.press("Escape");
  assert.equal(await isFocused(confirmCancel), true);
  await confirmCancel.click();
  assert.equal(await emailInput.inputValue(), inviteEmail);

  // A network failure keeps the draft as well.
  await page.route(INVITATIONS_REQUEST_PATTERN, abortPostRequests);
  await sendButton.click();
  await errorVisible(editor);
  await page.unroute(INVITATIONS_REQUEST_PATTERN);
  assert.equal(await emailInput.inputValue(), inviteEmail);
  await page.setViewportSize(MOBILE_VIEWPORT);
  await screenshot(page, output, "invitation-error-mobile");

  // The retry succeeds and lists the pending invitation.
  await sendButton.click();
  const row = page.locator(".invitation-row").filter({ hasText: inviteEmail });
  await row.waitFor();
  assert.ok((await row.innerText()).includes("Ausstehend"));
  assert.equal(await isFocused(page.locator("#invitations-title")), true);
  await screenshot(page, output, "invitations-mobile");
  await assertNoOverflowAtSupportedWidths(page);
  await screenshot(page, output, "invitations-desktop");

  await row.getByRole("button", { name: "Entziehen", exact: true }).click();
  await row.locator(".invitation-status.expired").waitFor();
  assert.equal(await row.getByRole("button").count(), 0);

  record(
    "Invitations use focused editors, safe discard, retained validation/network drafts, pending/accepted/revoked states",
  );
}

// ── Passkey enrollment ──────────────────────────────────────────────────────

export async function checkEnrollment({ page, origin, output, record }) {
  await page.goto(`${origin}/Account`);
  await assertNoOverflowAtSupportedWidths(page);
  await screenshot(page, output, "empty-desktop");

  const addButton = page.locator("#add-passkey");
  const editor = page.locator("#passkey-add-editor");
  const nameInput = page.locator("#passkey-name");
  const saveButton = page.locator("#save-passkey");

  // Opening focuses the name; an untouched editor closes back onto its opener.
  await addButton.click();
  assert.equal(await isFocused(nameInput), true);
  await page.keyboard.press("Escape");
  assert.equal(await isFocused(addButton), true);

  // Failing to load the passkey options keeps the name.
  await addButton.click();
  await nameInput.fill(PASSKEY_DRAFT_NAME);
  await page.route(PASSKEY_OPTIONS_REQUEST_PATTERN, (route) => route.abort());
  await saveButton.click();
  await errorVisible(editor);
  await page.unroute(PASSKEY_OPTIONS_REQUEST_PATTERN);
  assert.equal(await nameInput.inputValue(), PASSKEY_DRAFT_NAME);

  // Keep the authenticator prompt pending until the test cancels it.
  await page.evaluate(() => {
    window.originalCredentialsCreate = navigator.credentials.create;
    navigator.credentials.create = () =>
      new Promise((resolve, reject) => {
        window.cancelCredentials = () => reject(new DOMException("Cancelled", "NotAllowedError"));
      });
  });
  await saveButton.click();
  await page.waitForFunction(() => window.cancelCredentials);
  assert.equal(await saveButton.isDisabled(), true);

  // While the enrollment is pending, Escape neither closes the editor nor asks to discard.
  await page.keyboard.press("Escape");
  assert.equal(await editor.isVisible(), true);
  assert.equal(await page.locator("#action-confirmation").isVisible(), false);

  await page.evaluate(() => {
    window.cancelCredentials();
    navigator.credentials.create = window.originalCredentialsCreate;
  });
  await errorVisible(editor);
  assert.equal(await saveButton.isEnabled(), true);
  assert.equal(await nameInput.inputValue(), PASSKEY_DRAFT_NAME);

  await page.setViewportSize(MOBILE_VIEWPORT);
  await screenshot(page, output, "passkey-cancelled-mobile");
  await discard(page);
  assert.equal(await editor.isVisible(), false);
  await page.setViewportSize(DESKTOP_VIEWPORT);

  record(
    "Passkey options failures and authenticator cancellation preserve names; pending enrollment blocks closing",
  );
}

// ── Passkey rename ──────────────────────────────────────────────────────────

export async function checkRename({ page, owner, browser, origin, output, record }) {
  await checkRenameKeepsDrafts({ page, output, record });
  await checkRenameAfterSessionLoss({ owner, browser, origin, record });
}

async function checkRenameKeepsDrafts({ page, output, record }) {
  const row = page.locator(".passkey-row");
  const nameInput = row.getByLabel("Name", { exact: true });
  const saveButton = row.locator("[id^=save-passkey-]");
  const removeButton = row.getByRole("button", { name: "Passkey entfernen", exact: true });
  const confirmCancel = page.locator("[data-confirm-cancel]");

  // Server validation keeps the invalid draft.
  await row.locator("[data-editor-open]").click();
  await nameInput.fill("   ");
  await saveButton.click();
  await errorVisible(row);
  assert.equal(await nameInput.inputValue(), "   ");

  // A network failure keeps the draft as well.
  await nameInput.fill("Ein sehr langer, aber gültiger Passkey-Name für den mobilen Arbeitsalltag");
  await page.route(ACCOUNT_REQUEST_PATTERN, abortPostRequests);
  await saveButton.click();
  await errorVisible(row);
  await page.unroute(ACCOUNT_REQUEST_PATTERN);
  assert.ok((await nameInput.inputValue()).includes("mobilen Arbeitsalltag"));
  await page.setViewportSize(MOBILE_VIEWPORT);
  await screenshot(page, output, "rename-error-mobile");

  // The retry saves and returns focus to the row.
  await saveButton.click();
  await row.locator("[data-editor-open]").waitFor();
  await page.waitForFunction(() => document.activeElement?.matches(".passkey-row"));
  await assertNoOverflowAtSupportedWidths(page);
  await screenshot(page, output, "passkey-desktop");

  // Removal names the passkey, focuses the safe choice and returns focus when cancelled.
  await removeButton.click();
  assert.ok(
    (await page.locator("#confirmation-title").innerText()).includes("mobilen Arbeitsalltag"),
  );
  assert.equal(await isFocused(confirmCancel), true);
  await page.keyboard.press("Escape");
  assert.equal(await isFocused(removeButton), true);

  record(
    "Passkey rename preserves invalid/network drafts, restores row focus and names removal with safe cancel focus",
  );
}

async function checkRenameAfterSessionLoss({ owner, browser, origin, record }) {
  const isolated = await browser.newContext({
    storageState: await owner.context.storageState(),
    ignoreHTTPSErrors: origin.startsWith("https:"),
  });
  const expired = await isolated.newPage();
  const row = expired.locator(".passkey-row");
  const nameInput = row.getByLabel("Name", { exact: true });
  const saveButton = expired.locator(".passkey-row [id^=save-passkey-]");

  await expired.goto(`${origin}/Account`);
  await expired.locator(".passkey-row [data-editor-open]").click();
  await nameInput.fill(EXPIRED_SESSION_DRAFT_NAME);

  await isolated.clearCookies();
  await saveButton.click();
  await errorVisible(row);

  assert.equal(await nameInput.inputValue(), EXPIRED_SESSION_DRAFT_NAME);
  assert.equal(await saveButton.isDisabled(), true);
  assert.equal(new URL(expired.url()).pathname, "/Account");

  await isolated.close();
  record("Actual session loss retains passkey drafts and disables further writes");
}

// ── Request rate limit ──────────────────────────────────────────────────────

export async function checkRateLimit({ page, origin, record }) {
  await page.goto(`${origin}/Account`);

  // Exhaust the limiter from inside the page so the next navigation is rejected.
  await page.evaluate(async () => {
    for (let count = 0; count < 125; count++) {
      const response = await fetch("/health/live");
      if (response.status === 429) {
        return;
      }
    }

    throw new Error("Request limit was not reached");
  });

  const response = await page.goto(`${origin}/Account`);
  assert.equal(response.status(), 429);
  await page.locator(".system-state .system-code").filter({ hasText: "429" }).waitFor();

  record("Actual server rate limit renders the full 429 state without looping through the limiter");
}
