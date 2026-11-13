import assert from 'node:assert/strict';
import { join } from 'node:path';

export async function checkCanvas({ page, origin, casePath, firstId, secondId, boolean, rating, output, record }) {
  const comparePath = `${casePath}/compare?first=${firstId}&second=${secondId}`;
  const firstPath = `${casePath}/candidates/${firstId}`;
  const editor = id => page.locator(`#editor-assessment-${secondId}-${id}`);
  const opener = id => page.locator(`#open-assessment-${secondId}-${id}`);
  const input = id => editor(id).getByLabel('Deine Angabe', { exact: true });
  const note = id => editor(id).locator('textarea[name=notes]');
  const save = id => editor(id).getByRole('button', { name: /speichern$/ });
  const confirmation = page.locator('#action-confirmation');
  async function settle() { await page.waitForFunction(() => !document.querySelector('.htmx-request, .htmx-settling')); }
  async function submit(id, status = 200) {
    const response = page.waitForResponse(response => response.request().method() === 'POST' && new URL(response.url()).pathname === `${casePath}/compare`);
    await save(id).click();
    assert.equal((await response).status(), status);
    await settle();
  }
  async function exportData() { return page.evaluate(async path => (await fetch(path + '?handler=Export')).json(), casePath); }
  await page.goto(origin + casePath + '/candidates');
  const count = await page.locator('.candidate-card').count();
  await page.locator('#add-candidate').click();
  const creation = page.getByRole('dialog', { name: 'Auto hinzufügen', exact: true });
  await creation.getByLabel('Modell / Bezeichnung').fill('Ungespeicherter Kandidat');
  await page.keyboard.press('Escape');
  await confirmation.getByRole('button', { name: 'Weiterbearbeiten' }).click();
  assert.equal(await creation.getByLabel('Modell / Bezeichnung').inputValue(), 'Ungespeicherter Kandidat');
  await page.mouse.click(20, 20);
  assert.equal(await creation.isVisible(), true);
  await creation.getByRole('button', { name: 'Abbrechen', exact: true }).click();
  await confirmation.getByRole('button', { name: 'Änderungen verwerfen', exact: true }).click();
  assert.equal(await page.locator('#add-candidate').evaluate(button => button === document.activeElement), true);
  await page.locator('#add-candidate').click();
  assert.equal(await creation.getByLabel('Modell / Bezeichnung').inputValue(), '');
  await page.keyboard.press('Escape');
  const card = page.locator('.candidate-card').first();
  await card.locator('.candidate-actions > summary').click();
  await card.getByRole('button', { name: 'Auto entfernen', exact: true }).click();
  assert.equal(await confirmation.locator('[data-confirm-cancel]').evaluate(button => button === document.activeElement), true);
  await page.keyboard.press('Escape');
  assert.equal(await page.locator('.candidate-card').count(), count);
  record('Vehicle creation protects drafts and restores focus; cancelling removal leaves the collection unchanged');
  await page.goto(origin + comparePath);
  const original = (await exportData()).assessments.find(value => value.candidateId === secondId && value.criterionId === boolean);

  // The pair remains explicit in links, including returning from the second vehicle.
  await page.locator('.comparison-candidate-link').nth(1).click();
  assert.equal(new URL(page.url()).searchParams.get('first'), firstId);
  assert.equal(new URL(page.url()).searchParams.get('second'), secondId);
  await page.locator('.section-nav').getByRole('link', { name: 'Vergleichen', exact: true }).click();
  assert.equal(await page.locator('#compare-first').inputValue(), firstId);
  assert.equal(await page.locator('#compare-second').inputValue(), secondId);

  // Unchanged Escape closes directly; changed Escape offers a safe continuation.
  await opener(boolean).click();
  assert.equal(await input(boolean).evaluate(field => field === document.activeElement), true);
  await page.keyboard.press('Escape');
  assert.equal(await editor(boolean).isVisible(), false);
  assert.equal(await opener(boolean).evaluate(button => button === document.activeElement), true);
  await opener(boolean).click();
  await note(boolean).fill('Dieser Entwurf bleibt bis zum bewussten Verwerfen.');
  await page.keyboard.press('Escape');
  assert.equal(await confirmation.locator('[data-confirm-cancel]').evaluate(button => button === document.activeElement), true);
  await confirmation.getByRole('button', { name: 'Weiterbearbeiten' }).click();
  assert.equal(await note(boolean).inputValue(), 'Dieser Entwurf bleibt bis zum bewussten Verwerfen.');
  await editor(boolean).getByRole('button', { name: 'Abbrechen', exact: true }).click();
  await confirmation.getByRole('button', { name: 'Änderungen verwerfen', exact: true }).click();
  await opener(boolean).click();
  assert.equal(await note(boolean).inputValue(), original?.notes || '');
  await page.keyboard.press('Escape');
  record('Canvas capture/comparison retains the pair; Escape, discard and focus follow the same editor rules');

  // Keyboard-accessible horizontal comparison with a full-width form below it.
  await page.setViewportSize({ width: 390, height: 844 });
  const scroll = page.locator('#vehicle-comparison-scroll');
  await scroll.evaluate(element => { element.scrollLeft = element.scrollWidth; });
  const horizontal = await scroll.evaluate(element => element.scrollLeft);
  assert.ok(horizontal > 0);
  const pinned = await page.locator(`[data-comparison-criterion="${boolean}"] th`).boundingBox();
  assert.ok(pinned.x >= 0 && pinned.x < 50);
  await opener(boolean).click();
  const bounds = await editor(boolean).boundingBox();
  assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= 390);
  assert.equal(await editor(boolean).locator('form').first().evaluate(form => !!form.closest('.comparison-scroll')), false);
  await note(boolean).fill('Auf dem Handy geprüft.');
  await submit(boolean);
  assert.equal(await editor(boolean).isVisible(), false);
  assert.equal(await opener(boolean).evaluate(button => button === document.activeElement), true);
  assert.ok(Math.abs(await scroll.evaluate(element => element.scrollLeft) - horizontal) <= 1);
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  for (const heading of await page.locator('.comparison-group-label').all()) {
    const bounds = await heading.boundingBox();
    assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= 390, 'Group labels remain visible in the second comparison column');
  }
  await page.screenshot({ path: join(output, 'canvas-comparison-mobile.png'), fullPage: true });
  record('Mobile second-column editing preserves pinned criteria, viewport, horizontal position and return focus');

  // A failed request cannot look like a successful save or erase the draft.
  await opener(boolean).click();
  await note(boolean).fill('Bei einem Verbindungsfehler bleibt diese Notiz.');
  const rejectPost = route => route.request().method() === 'POST' ? route.abort('failed') : route.continue();
  await page.route(`**${casePath}/compare?*`, rejectPost);
  await save(boolean).click();
  await editor(boolean).getByRole('alert').filter({ hasText: 'nicht gespeichert' }).waitFor();
  assert.equal(await note(boolean).inputValue(), 'Bei einem Verbindungsfehler bleibt diese Notiz.');
  await page.unroute(`**${casePath}/compare?*`, rejectPost);
  await submit(boolean);

  await opener(rating).click();
  await input(rating).evaluate(select => { select.add(new Option('9 / 5', '9')); select.value = '9'; select.dispatchEvent(new Event('change', { bubbles: true })); });
  await note(rating).fill('Auch eine ungültige Angabe bleibt zur Korrektur erhalten.');
  await submit(rating, 422);
  assert.equal(await input(rating).inputValue(), '9');
  assert.equal(await note(rating).inputValue(), 'Auch eine ungültige Angabe bleibt zur Korrektur erhalten.');
  assert.equal(await editor(rating).isVisible(), true);
  await input(rating).selectOption('4');
  await submit(rating);
  record('Network failure and validation keep values and notes in the open editor; retry saves successfully');

  await opener(boolean).click();
  await input(boolean).selectOption('true');
  await note(boolean).fill('Lokaler Entwurf während einer gleichzeitigen Änderung.');
  const remote = await page.evaluate(async ({ path, candidateId, criterionId }) => {
    const token = document.querySelector('[name=__RequestVerificationToken]').value;
    const version = document.querySelector('#candidate-workspace').dataset.version;
    return (await fetch(`${path}/candidates/${candidateId}`, { method: 'POST', body: new URLSearchParams({
      __RequestVerificationToken: token, version, action: 'assess-candidate', criterionId, booleanValue: 'false', source: 'Inspection', notes: 'In einem anderen Fenster geändert.'
    }) })).status;
  }, { path: casePath, candidateId: secondId, criterionId: boolean });
  assert.equal(remote, 200);
  await submit(boolean, 409);
  assert.equal(await input(boolean).inputValue(), 'true');
  assert.equal(await note(boolean).inputValue(), 'Lokaler Entwurf während einer gleichzeitigen Änderung.');
  assert.equal((await exportData()).assessments.find(value => value.candidateId === secondId && value.criterionId === boolean).booleanValue, false);
  await editor(boolean).getByRole('button', { name: 'Aktuellen Stand ansehen' }).click();
  await page.locator('.section-nav').getByRole('link', { name: 'Autos', exact: true }).click();
  await confirmation.getByRole('button', { name: 'Weiterbearbeiten' }).click();
  assert.equal(new URL(page.url()).pathname, `${casePath}/compare`);
  await opener(boolean).click();
  assert.equal(await note(boolean).inputValue(), 'Lokaler Entwurf während einer gleichzeitigen Änderung.');
  await submit(boolean);
  assert.equal((await exportData()).assessments.find(value => value.candidateId === secondId && value.criterionId === boolean).booleanValue, true);
  record('Conflict shows the newer server value and retains the local draft even while reviewing or attempting navigation');

  // Restore the shared fixture so the remaining domain assertions stay independent.
  await opener(boolean).click();
  await input(boolean).selectOption(original?.booleanValue == null ? '' : String(original.booleanValue));
  await editor(boolean).getByLabel('Grundlage', { exact: true }).selectOption(original?.source || 'Unknown');
  await note(boolean).fill(original?.notes || '');
  await submit(boolean);
  for (const width of [320, 768, 1024, 1440]) {
    await page.setViewportSize({ width, height: 1000 });
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `comparison overflow at ${width}`);
    await page.goto(origin + firstPath);
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `record overflow at ${width}`);
    await page.getByRole('tab', { name: 'Technische Daten', exact: true }).click();
    assert.equal(await page.locator('#record-specifications').isVisible(), true);
    await page.getByRole('tab', { name: 'Anforderungen', exact: true }).click();
    await page.goto(origin + comparePath);
  }
  await page.screenshot({ path: join(output, 'canvas-comparison-desktop.png'), fullPage: true });
  record('Canvas record, tabs and comparison fit at 320/768/1024/1440px without document overflow');

  // A real expired session redirects to login; that response must never replace the editor.
  const expired = await page.context().browser().newContext({ storageState: await page.context().storageState(), ignoreHTTPSErrors: origin.startsWith('https:') });
  try {
    const stalePage = await expired.newPage();
    stalePage.setDefaultTimeout(10000);
    await stalePage.goto(origin + comparePath);
    await stalePage.locator(`#open-assessment-${secondId}-${boolean}`).click();
    const staleEditor = stalePage.locator(`#editor-assessment-${secondId}-${boolean}`);
    await staleEditor.locator('textarea').fill('Entwurf mit inzwischen abgelaufener Anmeldung.');
    await expired.clearCookies();
    await staleEditor.getByRole('button', { name: /speichern$/ }).click();
    await staleEditor.getByRole('alert').filter({ hasText: 'nicht mehr bearbeiten' }).waitFor();
    assert.equal(await staleEditor.locator('textarea').inputValue(), 'Entwurf mit inzwischen abgelaufener Anmeldung.');
    assert.equal(await staleEditor.getByRole('button', { name: /speichern$/ }).isDisabled(), true);
    assert.equal(new URL(stalePage.url()).pathname, `${casePath}/compare`);
  } finally { await expired.close(); }
  record('Expired authentication retains the draft and blocks further saves instead of swapping login into the workspace');
}
