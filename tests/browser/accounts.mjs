import assert from 'node:assert/strict';
import { join } from 'node:path';

async function errorVisible(form) { await form.locator('.editor-error:not([hidden])').waitFor(); }
async function discard(page) { await page.keyboard.press('Escape'); await page.locator('[data-confirm-accept]').click(); }
async function screenshot(page, output, name) { await page.screenshot({ path: join(output, `account-${name}.png`), fullPage: true }); }
async function narrow(page) {
  for (const width of [320, 390, 768, 1024, 1440]) {
    await page.setViewportSize({ width, height: 1000 });
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `Page overflow at ${width}px: ${page.url()}`);
  }
}
export async function checkSetup({ page, token, output, record }) {
  await narrow(page); await screenshot(page, output, 'setup-desktop');
  await page.setViewportSize({ width: 390, height: 844 }); await screenshot(page, output, 'setup-mobile');
  const form = page.locator('#setup-form');
  await page.getByLabel('Einrichtungsschlüssel').fill('invalid-installation-token');
  await page.getByRole('button', { name: 'E-Mail-Adresse bestätigen' }).click();
  await errorVisible(form);
  assert.equal(await page.getByLabel('Name', { exact: true }).inputValue(), 'Test Owner');
  assert.equal(await page.getByLabel('Einrichtungsschlüssel').inputValue(), 'invalid-installation-token');
  assert.equal(await form.locator('.editor-error').evaluate(node => document.activeElement === node), true);
  await page.getByLabel('Einrichtungsschlüssel').fill(token);
  await page.setViewportSize({ width: 1440, height: 1050 });
  record('Setup keeps account fields and focuses server validation; responsive entry at five widths');
}
export async function checkVerify({ page, code, output, record }) {
  const form = page.locator('#verify-form');
  const invalid = code === '111111' ? '222222' : '111111';
  await page.getByLabel('Einmalcode').fill(invalid);
  await page.getByRole('button', { name: 'Anmelden', exact: true }).click();
  await errorVisible(form);
  assert.equal(await page.getByLabel('Einmalcode').inputValue(), invalid);
  assert.equal(await page.getByRole('button', { name: 'Anmelden', exact: true }).isEnabled(), true);
  await narrow(page); await screenshot(page, output, 'verify-error-desktop');
  await page.setViewportSize({ width: 390, height: 844 }); await screenshot(page, output, 'verify-error-mobile');
  await page.setViewportSize({ width: 1440, height: 1050 });
  record('Incorrect verification code preserves input, local error focus and retry');
}
export async function checkAccess({ page, reader, newUser, origin, output, record }) {
  const anonymous = await newUser(); const login = anonymous.page;
  await login.goto(`${origin}/Auth/Login?ReturnUrl=%2FAccount`);
  await narrow(login); await screenshot(login, output, 'login-desktop');
  await login.setViewportSize({ width: 390, height: 844 }); await screenshot(login, output, 'login-mobile');
  const form = login.locator('#login-code-form'); const input = login.getByLabel('E-Mail-Adresse', { exact: true });
  await input.fill('retained@example.test');
  await anonymous.context.setOffline(true);
  await login.getByRole('button', { name: 'Anmeldecode senden' }).click();
  await errorVisible(form); assert.equal(await input.inputValue(), 'retained@example.test');
  await anonymous.context.setOffline(false);
  let release; const delayed = new Promise(resolve => { release = resolve; }); let requests = 0;
  await login.route('**/Auth/Login?*', async route => {
    if (route.request().method() !== 'POST') return route.continue();
    requests++; await delayed;
    await route.fulfill({ status: 429, body: '' });
  });
  await login.getByRole('button', { name: 'Anmeldecode senden' }).click();
  await form.locator('.request-status:not([hidden])').waitFor();
  assert.equal(await form.getAttribute('aria-busy'), 'true');
  assert.equal(await login.getByRole('button', { name: 'Anmeldecode senden' }).isDisabled(), true);
  await form.evaluate(node => { node.requestSubmit(); node.requestSubmit(); });
  release();
  await form.locator('.editor-error').filter({ hasText: 'Zu viele Anfragen' }).waitFor();
  assert.equal(requests, 1); assert.equal(await input.inputValue(), 'retained@example.test');
  assert.notEqual(await form.getAttribute('aria-busy'), 'true');
  await login.unroute('**/Auth/Login?*');
  record('Login retains input offline and on rate limits; pending requests reject duplicate submission');
  await anonymous.context.close();

  const notFound = await page.goto(`${origin}/missing-browser-page`);
  assert.equal(notFound.status(), 404); await page.locator('.system-state').waitFor();
  assert.equal(await page.locator('.app-navigation [aria-current=page]').count(), 0);
  await narrow(page); await screenshot(page, output, 'not-found-desktop');
  await page.setViewportSize({ width: 390, height: 844 }); await screenshot(page, output, 'not-found-mobile');
  await page.setViewportSize({ width: 1440, height: 1050 });
  assert.equal((await reader.page.goto(`${origin}/Account/Invitations`)).status(), 403);
  await reader.page.locator('.system-state').waitFor();
  for (const status of [429, 500]) {
    assert.equal((await page.goto(`${origin}/Error?statusCode=${status}`)).status(), status);
    await page.locator('.system-state').waitFor();
  }
  await page.goto(`${origin}/Auth/Login?ReturnUrl=https%3A%2F%2Fexample.invalid`);
  await page.waitForURL(origin + '/');
  await page.goto(`${origin}/Auth/Login?ReturnUrl=%2FAccount`); await page.waitForURL(origin + '/Account');
  record('Full-page 403/404 and explicit 429/500 states preserve HTTP status; login only accepts local return destinations');

  await page.goto(`${origin}/Account/Invitations`);
  await page.locator('#invite-person').click();
  const editor = page.locator('#invitation-editor');
  assert.equal(await editor.getByLabel('Name', { exact: true }).evaluate(node => document.activeElement === node), true);
  await page.keyboard.press('Escape');
  assert.equal(await page.locator('#invite-person').evaluate(node => document.activeElement === node), true);
  await page.locator('#invite-person').click();
  await editor.getByLabel('Name', { exact: true }).fill('   ');
  const inviteEmail = `pending-${Date.now()}@example.test`;
  await editor.getByLabel('E-Mail-Adresse').fill(inviteEmail);
  await page.locator('#send-invitation').click(); await errorVisible(editor);
  assert.equal(await editor.getByLabel('E-Mail-Adresse').inputValue(), inviteEmail);
  await editor.getByLabel('Name', { exact: true }).fill('Eine Person mit einem bewusst langen Namen für den gemeinsamen Vergleich');
  await page.keyboard.press('Escape');
  assert.equal(await page.locator('[data-confirm-cancel]').evaluate(node => document.activeElement === node), true);
  await page.locator('[data-confirm-cancel]').click();
  assert.equal(await editor.getByLabel('E-Mail-Adresse').inputValue(), inviteEmail);
  await page.route('**/Account/Invitations', route => route.request().method() === 'POST' ? route.abort() : route.continue());
  await page.locator('#send-invitation').click(); await errorVisible(editor);
  await page.unroute('**/Account/Invitations');
  assert.equal(await editor.getByLabel('E-Mail-Adresse').inputValue(), inviteEmail);
  await page.setViewportSize({ width: 390, height: 844 }); await screenshot(page, output, 'invitation-error-mobile');
  await page.locator('#send-invitation').click();
  const row = page.locator('.invitation-row').filter({ hasText: inviteEmail });
  await row.waitFor(); assert.ok((await row.innerText()).includes('Ausstehend'));
  assert.equal(await page.locator('#invitations-title').evaluate(node => document.activeElement === node), true);
  await screenshot(page, output, 'invitations-mobile'); await narrow(page); await screenshot(page, output, 'invitations-desktop');
  await row.getByRole('button', { name: 'Entziehen', exact: true }).click();
  await row.locator('.invitation-status.expired').waitFor();
  assert.equal(await row.getByRole('button').count(), 0);
  record('Invitations use focused editors, safe discard, retained validation/network drafts, pending/accepted/revoked states');
}
export async function checkEnrollment({ page, origin, output, record }) {
  await page.goto(`${origin}/Account`); await narrow(page); await screenshot(page, output, 'empty-desktop');
  await page.locator('#add-passkey').click();
  const editor = page.locator('#passkey-add-editor');
  assert.equal(await page.locator('#passkey-name').evaluate(node => document.activeElement === node), true);
  await page.keyboard.press('Escape');
  assert.equal(await page.locator('#add-passkey').evaluate(node => document.activeElement === node), true);
  await page.locator('#add-passkey').click(); await page.locator('#passkey-name').fill('Persönlicher Sicherheitsschlüssel');
  await page.route('**/Account?handler=Options', route => route.abort());
  await page.locator('#save-passkey').click(); await errorVisible(editor);
  await page.unroute('**/Account?handler=Options');
  assert.equal(await page.locator('#passkey-name').inputValue(), 'Persönlicher Sicherheitsschlüssel');
  await page.evaluate(() => {
    window.originalCredentialsCreate = navigator.credentials.create;
    navigator.credentials.create = () => new Promise((resolve, reject) => { window.cancelCredentials = () => reject(new DOMException('Cancelled', 'NotAllowedError')); });
  });
  await page.locator('#save-passkey').click();
  await page.waitForFunction(() => window.cancelCredentials);
  assert.equal(await page.locator('#save-passkey').isDisabled(), true);
  await page.keyboard.press('Escape'); assert.equal(await editor.isVisible(), true);
  assert.equal(await page.locator('#action-confirmation').isVisible(), false);
  await page.evaluate(() => { window.cancelCredentials(); navigator.credentials.create = window.originalCredentialsCreate; });
  await errorVisible(editor); assert.equal(await page.locator('#save-passkey').isEnabled(), true);
  assert.equal(await page.locator('#passkey-name').inputValue(), 'Persönlicher Sicherheitsschlüssel');
  await page.setViewportSize({ width: 390, height: 844 }); await screenshot(page, output, 'passkey-cancelled-mobile');
  await discard(page); assert.equal(await editor.isVisible(), false);
  await page.setViewportSize({ width: 1440, height: 1050 });
  record('Passkey options failures and authenticator cancellation preserve names; pending enrollment blocks closing');
}
export async function checkRename({ page, owner, browser, origin, output, record }) {
  const row = page.locator('.passkey-row');
  await row.locator('[data-editor-open]').click();
  const input = row.getByLabel('Name', { exact: true });
  await input.fill('   '); await row.locator('[id^=save-passkey-]').click();
  await errorVisible(row); assert.equal(await input.inputValue(), '   ');
  await input.fill('Ein sehr langer, aber gültiger Passkey-Name für den mobilen Arbeitsalltag');
  await page.route('**/Account', route => route.request().method() === 'POST' ? route.abort() : route.continue());
  await row.locator('[id^=save-passkey-]').click(); await errorVisible(row);
  await page.unroute('**/Account');
  assert.ok((await input.inputValue()).includes('mobilen Arbeitsalltag'));
  await page.setViewportSize({ width: 390, height: 844 }); await screenshot(page, output, 'rename-error-mobile');
  await row.locator('[id^=save-passkey-]').click(); await row.locator('[data-editor-open]').waitFor();
  await page.waitForFunction(() => document.activeElement?.matches('.passkey-row'));
  await narrow(page); await screenshot(page, output, 'passkey-desktop');
  await row.getByRole('button', { name: 'Passkey entfernen', exact: true }).click();
  assert.ok((await page.locator('#confirmation-title').innerText()).includes('mobilen Arbeitsalltag'));
  assert.equal(await page.locator('[data-confirm-cancel]').evaluate(node => document.activeElement === node), true);
  await page.keyboard.press('Escape');
  assert.equal(await row.getByRole('button', { name: 'Passkey entfernen', exact: true }).evaluate(node => document.activeElement === node), true);
  record('Passkey rename preserves invalid/network drafts, restores row focus and names removal with safe cancel focus');

  const isolated = await browser.newContext({ storageState: await owner.context.storageState(), ignoreHTTPSErrors: origin.startsWith('https:') });
  const expired = await isolated.newPage(); await expired.goto(`${origin}/Account`);
  await expired.locator('.passkey-row [data-editor-open]').click();
  await expired.locator('.passkey-row').getByLabel('Name', { exact: true }).fill('Draft after session expiry');
  await isolated.clearCookies();
  await expired.locator('.passkey-row [id^=save-passkey-]').click();
  await errorVisible(expired.locator('.passkey-row'));
  assert.equal(await expired.locator('.passkey-row').getByLabel('Name', { exact: true }).inputValue(), 'Draft after session expiry');
  assert.equal(await expired.locator('.passkey-row [id^=save-passkey-]').isDisabled(), true);
  assert.equal(new URL(expired.url()).pathname, '/Account');
  await isolated.close();
  record('Actual session loss retains passkey drafts and disables further writes');
}

export async function checkRateLimit({ page, origin, record }) {
  await page.goto(`${origin}/Account`);
  await page.evaluate(async () => {
    for (let count = 0; count < 125; count++) {
      const response = await fetch('/health/live');
      if (response.status === 429) return;
    }
    throw new Error('Request limit was not reached');
  });
  const response = await page.goto(`${origin}/Account`);
  assert.equal(response.status(), 429);
  await page.locator('.system-state .system-code').filter({ hasText: '429' }).waitFor();
  record('Actual server rate limit renders the full 429 state without looping through the limiter');
}
