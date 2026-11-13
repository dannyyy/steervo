import assert from 'node:assert/strict';

async function save(page, operation) {
  await Promise.all([page.waitForNavigation({ waitUntil: 'load' }), operation()]);
}
async function response(page, status, operation) {
  const path = new URL(page.url()).pathname;
  const pending = page.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname === path);
  await operation(); assert.equal((await pending).status(), status);
  await page.waitForFunction(() => !document.querySelector('.htmx-request'));
}
async function focus(page, id) { await page.waitForFunction(value => document.activeElement?.id === value, id); }
async function share({ page, post, origin, casePath, readerEmail }, access) {
  await page.goto(origin + casePath);
  const version = await page.locator('#evaluation-workspace').getAttribute('data-version');
  assert.equal((await post(page, casePath, { action: 'share', email: readerEmail, access, version })).status(), 200);
}
async function revoke({ page, post, origin, casePath }) {
  await page.goto(origin + casePath);
  const version = await page.locator('#evaluation-workspace').getAttribute('data-version');
  const userId = await page.locator('.share-row input[name=userId]').inputValue();
  assert.equal((await post(page, casePath, { action: 'revoke', userId, version })).status(), 200);
}
async function concurrentRemoval(page, other, url, selector, label, change) {
  await page.goto(url); await other.goto(url);
  await change(other);
  await page.locator(selector).getByRole('button', { name: label, exact: true }).click();
  await response(page, 409, () => page.locator('[data-confirm-accept]').click());
  const error = page.locator(selector + ' .editor-error');
  await error.getByText(/Die Aktion wurde nicht ausgeführt/).waitFor();
  await error.getByRole('button', { name: 'Stand geprüft – erneut bestätigen', exact: true }).click();
  await page.locator('#action-confirmation[open]').waitFor();
  assert.equal(await page.locator('[data-confirm-cancel]').evaluate(el => el === document.activeElement), true);
  await page.keyboard.press('Escape');
  assert.equal(await error.isVisible(), true);
  await other.locator(selector).getByRole('button', { name: label, exact: true }).click();
  await save(other, () => other.locator('[data-confirm-accept]').click());
  await error.getByRole('button', { name: 'Stand geprüft – erneut bestätigen', exact: true }).click();
  await response(page, 404, () => page.locator('[data-confirm-accept]').click());
  await error.getByText(/nicht mehr verfügbar/).waitFor();
  assert.equal(await page.locator(selector + ' button[type=submit]').isDisabled(), true);
  await page.reload(); assert.equal(await page.locator(selector).count(), 0);
}

export async function checkCostWorkflows(options) {
  const { page, reader, origin, casePath, candidateId, costId, record } = options;
  const path = casePath + '/costs';
  await page.goto(`${origin}${path}?candidateId=${candidateId}`);
  await page.locator('#open-cost-plan').click(); await page.locator('#Plan_Months').fill('48');
  await save(page, () => page.locator('#save-cost-plan').click());
  await focus(page, 'cost-plan-title');
  await page.goto(`${origin}${path}?candidateId=${candidateId}&period=month&pageNumber=2`);
  assert.equal(await page.locator('[data-cost-period]').first().getAttribute('data-cost-period'), '25');
  await page.locator(`#cost-${costId}`).getByRole('link', { name: 'Bearbeiten', exact: true }).click();
  await page.locator('#cost-page-editor[data-initialized=true]').waitFor();
  await page.keyboard.press('Escape'); await page.locator('#cost-workspace').waitFor();
  assert.equal(new URL(page.url()).searchParams.get('pageNumber'), '2');
  await page.locator(`#cost-${costId}`).getByRole('link', { name: 'Bearbeiten', exact: true }).click();
  await page.locator('#Input_Notes').fill('Entwurf mit Zeitraum und Fahrzeugkontext');
  await page.route('**/costs/edit/**', route => route.request().method() === 'POST' ? route.abort('failed') : route.continue());
  await page.locator('#save-cost').click(); await page.locator('.editor-error').getByText(/nicht gespeichert/).waitFor();
  await page.unroute('**/costs/edit/**');
  await page.getByRole('link', { name: 'Abbrechen', exact: true }).click();
  await page.locator('[data-confirm-cancel]').click();
  assert.equal(await page.locator('#Input_Notes').inputValue(), 'Entwurf mit Zeitraum und Fahrzeugkontext');
  await save(page, () => page.locator('#save-cost').click());
  await focus(page, 'cost-' + costId);
  assert.equal(new URL(page.url()).searchParams.get('candidateId'), candidateId);
  assert.equal(new URL(page.url()).searchParams.get('pageNumber'), '2');
  assert.equal(await page.locator('[data-cost-period]').first().getAttribute('data-cost-period'), '25');
  record('Cost edits retain the vehicle, timeline page, notes and return focus through cancellation and network failure');

  await share(options, 'Editor');
  await page.goto(`${origin}${path}?candidateId=${candidateId}`);
  await page.locator('#open-cost-plan').click(); await page.locator('#Plan_Months').fill('60');
  const stale = await page.locator('#cost-plan-editor [name=Version]').inputValue();
  await reader.page.goto(origin + path); await reader.page.locator('#open-cost-plan').click(); await reader.page.locator('#Plan_Start').fill('2028-01');
  await save(reader.page, () => reader.page.locator('#save-cost-plan').click());
  await response(page, 409, () => page.locator('#save-cost-plan').click());
  assert.equal(await page.locator('#Plan_Months').inputValue(), '60');
  assert.equal(await page.locator('#cost-plan-editor [name=Version]').inputValue(), stale);
  assert.match(await page.locator('#cost-plan-editor .editor-error').textContent(), /01.2028/);
  await save(page, () => page.getByRole('button', { name: 'Stand geprüft – erneut speichern', exact: true }).click());
  assert.match(await page.locator('.cost-plan-heading').textContent(), /60 Monate/);
  await page.locator('#open-cost-plan').click(); await page.locator('#Plan_Months').fill('48');
  await save(page, () => page.locator('#save-cost-plan').click());
  assert.match(await page.locator('.cost-plan-heading').textContent(), /48 Monate/);
  assert.equal(await page.locator('#cost-plan-editor').isVisible(), false);
  await focus(page, 'cost-plan-title');
  record('Planning conflicts retain the draft until explicit retry; repeated saves refresh the same page and restore focus');

  await reader.page.goto(`${origin}${path}/edit/${costId}`); await reader.page.locator('#Input_Notes').fill('Entwurf ohne Schreibrecht');
  await revoke(options);
  await reader.page.locator('#save-cost').click(); await reader.page.locator('.editor-error').getByText(/nicht mehr bearbeiten/).waitFor();
  assert.equal(await reader.page.locator('#Input_Notes').inputValue(), 'Entwurf ohne Schreibrecht');
  assert.equal(await reader.page.locator('#save-cost').isDisabled(), true);
  await page.goto(`${origin}${path}?candidateId=${candidateId}`);
  const removable = page.locator('[data-cost-id]').filter({ hasNot: page.locator(`[id="remove-cost-${costId}"]`) }).first();
  const removedId = await removable.getAttribute('data-cost-id');
  const name = await removable.locator('h4').textContent();
  await page.locator('#open-cost-plan').click(); await page.locator('#Plan_Months').fill('72');
  await removable.getByRole('button', { name: 'Kostenposition löschen', exact: true }).click();
  assert.ok((await page.locator('#confirmation-title').textContent()).includes(name));
  await page.locator('[data-confirm-accept]').click();
  await page.getByRole('heading', { name: 'Ungespeicherte Änderungen verwerfen?' }).waitFor();
  await page.locator('[data-confirm-cancel]').click();
  assert.equal(await page.locator('#Plan_Months').inputValue(), '72');
  assert.equal(await page.locator('#cost-' + removedId).count(), 1);
  await page.locator('#cost-plan-editor [data-editor-close]').first().click(); await page.locator('[data-confirm-accept]').click();
  await page.route('**/costs?**', route => route.request().method() === 'POST' ? route.abort('failed') : route.continue());
  await removable.getByRole('button', { name: 'Kostenposition löschen', exact: true }).click(); await page.locator('[data-confirm-accept]').click();
  await page.locator('#remove-cost-' + removedId).getByText(/nicht gespeichert/).waitFor();
  assert.equal(await page.locator('#cost-' + removedId).count(), 1);
  await page.unroute('**/costs?**');
  await removable.getByRole('button', { name: 'Kostenposition löschen', exact: true }).click();
  await save(page, () => page.locator('[data-confirm-accept]').click());
  assert.equal(await page.locator('#cost-' + removedId).count(), 0); await focus(page, 'cost-items');
  record('Cost permissions and named deletion protect adjacent drafts, recover from network failure and return focus');

  await share(options, 'Editor');
  await concurrentRemoval(page, reader.page, `${origin}${path}?candidateId=${candidateId}`, '#remove-cost-' + costId, 'Kostenposition löschen', async other => {
    await other.locator('#open-cost-plan').click(); await other.locator('#Plan_Start').fill('2029-01');
    await save(other, () => other.locator('#save-cost-plan').click());
  });
  record('Cost deletion conflicts require explicit confirmation; an already removed item reports its absence and stops retrying');
}

export async function checkScenarioWorkflows(options) {
  const { page, reader, origin, casePath, cash, lease, cost, record } = options;
  const path = casePath + '/scenarios';
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(`${origin}${path}/compare?leftId=${cash}&rightId=${lease}&month=12`);
  await page.locator(`[data-scenario-column="${lease}"] .comparison-candidate-link`).click();
  await page.getByRole('link', { name: 'Jahre', exact: true }).click();
  await page.getByRole('link', { name: 'Szenario bearbeiten', exact: true }).click();
  await page.locator('#Input_RevisionNumber').selectOption('2');
  assert.equal(await page.locator('#scenario-MonthlyPayment').inputValue(), '50');
  assert.equal(new URL(await page.locator('[data-scenario-source]').getAttribute('href'), origin).searchParams.get('revision'), '2');
  await save(page, () => page.locator('#save-scenario').click()); await focus(page, 'scenario-title');
  assert.equal(new URL(page.url()).searchParams.get('period'), 'year');
  assert.equal(new URL(page.url()).searchParams.get('month'), '12');
  assert.equal(Number(await page.locator('[data-effective-cost]').getAttribute('data-effective-cost')), 820);
  await page.getByRole('link', { name: 'Szenarien vergleichen', exact: true }).click();
  assert.equal(await page.locator('#scenario-left').inputValue(), cash); assert.equal(await page.locator('#scenario-right').inputValue(), lease);
  await page.waitForFunction(() => document.querySelector('#scenario-comparison-scroll').scrollLeft > 50);
  assert.equal(await page.locator('.scenario-table thead th').first().evaluate(el => getComputedStyle(el).position), 'sticky');
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  record('Scenario editing preserves the pair, checkpoint and yearly view; source links follow the selected revision without replacing assumptions');

  await page.setViewportSize({ width: 1440, height: 1050 });
  await page.goto(`${origin}${path}/${cash}?month=6`); await page.locator('#open-residual').click();
  await page.locator('#Residual_Low').fill('600'); await page.locator('#Residual_Expected').fill('400'); await page.locator('#Residual_High').fill('300');
  await page.locator('#Residual_Notes').fill('Entwurf für den genauen Stichtag');
  await response(page, 422, () => page.locator('#save-residual').click());
  assert.equal(await page.locator('#Residual_Low').inputValue(), '600'); assert.equal(await page.locator('#Residual_Notes').inputValue(), 'Entwurf für den genauen Stichtag');
  await page.keyboard.press('Escape'); await page.locator('[data-confirm-cancel]').click();
  await page.locator('#Residual_Low').fill('300'); await page.locator('#Residual_High').fill('500');
  await save(page, () => page.locator('#save-residual').click()); await focus(page, 'scenario-residuals');
  assert.equal(Number(await page.locator('[data-effective-cost]').getAttribute('data-effective-cost')), 770);
  await share(options, 'Editor');
  await page.goto(`${origin}${path}/${cash}?month=6&residualMonth=6`); await page.locator('#Residual_Expected').fill('420');
  const stale = await page.locator('#residual-form [name=Version]').inputValue();
  await reader.page.goto(`${origin}${path}/${cash}?month=6&residualMonth=6`); await reader.page.locator('#Residual_Expected').fill('450');
  await save(reader.page, () => reader.page.locator('#save-residual').click());
  await response(page, 409, () => page.locator('#save-residual').click());
  assert.equal(await page.locator('#Residual_Expected').inputValue(), '420'); assert.equal(await page.locator('#residual-form [name=Version]').inputValue(), stale);
  await save(page, () => page.getByRole('button', { name: 'Stand geprüft – erneut speichern', exact: true }).click());
  assert.equal(Number(await page.locator('[data-effective-cost]').getAttribute('data-effective-cost')), 750);
  record('Residual validation and conflicts retain exact checkpoint assumptions and notes; explicit retry updates the calculated result');

  await page.goto(`${origin}${path}/${lease}?costId=${cost}`); await page.locator('#Adjustment_Amount').fill('7'); await page.locator('#Adjustment_Notes').fill('Lokale Anpassung');
  await page.route('**/scenarios/**', route => route.request().method() === 'POST' ? route.abort('failed') : route.continue());
  await page.locator('#save-adjustment').click(); await page.locator('#adjustment-editor .editor-error').getByText(/nicht gespeichert/).waitFor();
  assert.equal(await page.locator('#Adjustment_Amount').inputValue(), '7'); assert.equal(await page.locator('#Adjustment_Notes').inputValue(), 'Lokale Anpassung');
  await page.unroute('**/scenarios/**');
  await save(page, () => page.locator('#save-adjustment').click()); await focus(page, 'scenario-adjustments');
  assert.equal(Number(await page.locator('[data-effective-cost]').getAttribute('data-effective-cost')), 862);
  await page.locator('#open-scenario-copy').click(); await page.locator('#CopyName').fill('   ');
  await response(page, 422, () => page.locator('#save-scenario-copy').click());
  await page.locator('#CopyName').fill('Geschützte Kopie'); await page.keyboard.press('Escape'); await page.locator('[data-confirm-cancel]').click();
  assert.equal(await page.locator('#CopyName').inputValue(), 'Geschützte Kopie');
  await save(page, () => page.locator('#save-scenario-copy').click());
  const copyId = new URL(page.url()).pathname.split('/').at(-1);
  assert.notEqual(new URL(page.url()).pathname, path + '/' + lease);
  assert.equal(Number(await page.locator('[data-effective-cost]').getAttribute('data-effective-cost')), 862);
  record('Cost adjustments and scenario copies use the same guarded editors, retain failed drafts and keep independent calculations');

  await page.goto(origin + path); await page.locator('#open-scenario-settings').click(); await page.locator('#AnnualReturn').fill('2');
  await reader.page.goto(origin + path); await reader.page.locator('#open-scenario-settings').click(); await reader.page.locator('#AnnualReturn').fill('1');
  await save(reader.page, () => reader.page.locator('#save-scenario-settings').click());
  await response(page, 409, () => page.locator('#save-scenario-settings').click());
  assert.equal(await page.locator('#AnnualReturn').inputValue(), '2');
  await save(page, () => page.getByRole('button', { name: 'Stand geprüft – erneut speichern', exact: true }).click()); await focus(page, 'scenario-settings-title');
  await page.locator('#open-scenario-settings').click(); assert.equal(await page.locator('#AnnualReturn').inputValue(), '2');
  await page.keyboard.press('Escape'); await focus(page, 'open-scenario-settings');
  const expiredContext = await page.context().browser().newContext({ storageState: await page.context().storageState() });
  const expired = await expiredContext.newPage(); expired.setDefaultTimeout(10_000);
  await expired.goto(`${origin}${path}/edit/${cash}`); await expired.locator('#Input_Notes').fill('Erhaltene Rechenannahmen ohne Sitzung');
  await expiredContext.clearCookies(); await expired.locator('#save-scenario').click();
  await expired.locator('.editor-error').getByText(/nicht mehr bearbeiten/).waitFor();
  assert.equal(await expired.locator('#Input_Notes').inputValue(), 'Erhaltene Rechenannahmen ohne Sitzung'); assert.equal(await expired.locator('#save-scenario').isDisabled(), true);
  await expiredContext.close();
  record('Shared return assumptions require explicit conflict retry, and expired sessions retain full scenario drafts');

  await page.goto(`${origin}${path}/${cash}?month=6`);
  const point = page.locator('[data-residual-month="6"]'); await point.getByRole('button', { name: 'Stichtag löschen', exact: true }).click();
  assert.match(await page.locator('#confirmation-title').textContent(), /Monat 6/); await page.keyboard.press('Escape'); assert.equal(await point.count(), 1);
  await point.getByRole('button', { name: 'Stichtag löschen', exact: true }).click(); await save(page, () => page.locator('[data-confirm-accept]').click());
  assert.equal(await page.locator('[data-residual-month="6"]').count(), 0); assert.equal(await page.locator('[data-residual-month="12"]').count(), 1);
  assert.equal(await page.locator('[data-effective-cost]').getAttribute('data-effective-cost'), ''); await focus(page, 'scenario-residuals');
  record('Named residual deletion preserves other checkpoints and makes the missing result explicitly unknown again');

  await concurrentRemoval(page, reader.page, origin + path, '#remove-scenario-' + copyId, 'Szenario löschen', async other => {
    await other.locator('#open-scenario-settings').click(); await other.locator('#AnnualReturn').fill('3');
    await save(other, () => other.locator('#save-scenario-settings').click());
  });
  assert.equal(await page.locator('#remove-scenario-' + cash).count(), 1);
  assert.equal(await page.locator('#remove-scenario-' + lease).count(), 1);
  record('Concurrent scenario deletion keeps the other scenarios intact and requires a new explicit confirmation');
}
