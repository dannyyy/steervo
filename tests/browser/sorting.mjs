import assert from 'node:assert/strict';
import { join } from 'node:path';

export async function checkSorting({ page, owner, origin, output, post, mutate, record }) {
  const returnUrl = page.url();
  await page.goto(origin);
  await page.getByRole('button', { name: /Neue Evaluation/ }).click();
  await page.getByRole('dialog', { name: 'Neue Evaluation', exact: true }).getByLabel('Name der Evaluation').fill('Anforderungen übersichtlich ordnen');
  await page.getByRole('dialog', { name: 'Neue Evaluation', exact: true }).getByRole('button', { name: 'Evaluation anlegen' }).click();
  await page.waitForURL(/\/evaluations\//);
  const path = new URL(page.url()).pathname;
  let version = await page.locator('#evaluation-workspace').getAttribute('data-version');
  async function seed(values) {
    const response = await post(page, path, { ...values, version });
    assert.equal(response.status(), 200);
    version = (await response.text()).match(/data-version="(\d+)"/)[1];
  }
  for (const name of ['Schlüssel', 'Alltag', 'Komfort']) await seed({ action: 'add-group', name });
  await page.reload();
  const groupIds = await page.locator('.criterion-group[data-sort-kind="group"]').evaluateAll(groups => groups.map(group => group.dataset.groupId));
  const names = ['NFC Karte', 'Standardschlüssel', 'UWB Smart Key', 'App Key (Bluetooth)', 'Notschlüssel'];
  for (let index = 0; index < 23; index++) await seed({ action: 'add-criterion', name: names[index] || `Anforderung ${index + 1}`, type: 'Boolean', priority: index % 2 ? 'Required' : 'Preferred', scope: 'Vehicle', groupId: groupIds[index < 5 ? 0 : 1] });
  await page.reload();
  const group = id => page.locator(`.criterion-group[data-group-id="${id}"]`);
  const row = name => page.locator('.criterion').filter({ has: page.getByRole('heading', { name, exact: true }) });
  const order = id => group(id).locator('.criterion h4').allTextContents();
  const snapshot = () => page.evaluate(() => ({ y: scrollY, top: document.querySelector('.criteria-main').getBoundingClientRect().top }));
  const assertViewport = async before => {
    await page.waitForTimeout(100);
    const after = await snapshot();
    assert.ok(Math.abs(before.top - after.top) < 2, `List moved in viewport: ${JSON.stringify({ before, after })}`);
  };
  await page.locator('.criteria-main').scrollIntoViewIfNeeded();
  await page.screenshot({ path: join(output, '08-compact-desktop.png') });
  const height = await row('NFC Karte').evaluate(element => element.getBoundingClientRect().height);
  assert.ok(height <= 90, `Desktop row too tall: ${height}`);

  const arrow = page.getByRole('button', { name: 'Kriterium Anforderung 14 nach oben', exact: true });
  await arrow.scrollIntoViewIfNeeded();
  const before = await snapshot();
  await mutate(page, () => arrow.click());
  await assertViewport(before);
  assert.equal(await arrow.evaluate(element => element === document.activeElement), true);
  await mutate(page, () => page.keyboard.press('Enter'));
  const keyboardHandle = row('Anforderung 14').locator('[data-drag-handle]');
  await keyboardHandle.focus();
  await mutate(page, () => page.keyboard.press('ArrowDown'));
  assert.equal(await keyboardHandle.evaluate(element => element === document.activeElement), true);
  record('Compact rows; sorting arrows preserve viewport and keyboard focus on a long page');

  async function drag(source, target, edge = 'before') {
    await source.scrollIntoViewIfNeeded();
    const start = await source.boundingBox();
    await page.mouse.move(start.x + start.width / 2, start.y + start.height / 2);
    await page.mouse.down();
    let end = await target.boundingBox();
    for (let step = 0; step < 160 && (end.y < 90 || end.y + end.height > 950); step++) {
      await page.mouse.move(start.x + start.width / 2, end.y < 90 ? 10 : 1040, { steps: 2 });
      await page.waitForTimeout(50);
      end = await target.boundingBox();
    }
    assert.ok(end.y >= 80 && end.y + end.height <= 960, 'Auto-scroll must bring the drop target into view');
    await page.mouse.move(end.x + end.width / 2, edge === 'before' ? end.y + 4 : end.y + end.height - 4, { steps: 12 });
    await page.locator('.sorting-marker').waitFor({ state: 'visible' });
    await mutate(page, () => page.mouse.up());
  }
  const nfcId = await row('NFC Karte').getAttribute('data-criterion-id');
  await page.evaluate(() => window.scrollTo(0, 200));
  await drag(row('NFC Karte').locator('[data-drag-handle]'), row('Notschlüssel'), 'after');
  assert.equal((await order(groupIds[0])).at(-1), 'NFC Karte');
  await drag(row('NFC Karte').locator('[data-drag-handle]'), row('Standardschlüssel'));
  assert.equal((await order(groupIds[0]))[0], 'NFC Karte');
  await row('UWB Smart Key').locator('[data-editor-open]').click();
  await row('UWB Smart Key').getByLabel('Bezeichnung').fill('Noch nicht gespeichert');
  await mutate(page, () => page.getByRole('button', { name: 'Kriterium NFC Karte nach unten', exact: true }).click());
  assert.equal(await row('UWB Smart Key').locator('[data-editor]').isVisible(), true);
  assert.equal(await row('UWB Smart Key').getByLabel('Bezeichnung').inputValue(), 'Noch nicht gespeichert');
  await row('UWB Smart Key').getByRole('button', { name: 'Abbrechen', exact: true }).click();
  await page.getByRole('button', { name: 'Änderungen verwerfen', exact: true }).click();

  // Escape and dropping outside the list must not send a mutation.
  const oldVersion = await page.locator('#evaluation-workspace').getAttribute('data-version');
  const handle = row('NFC Karte').locator('[data-drag-handle]');
  await handle.scrollIntoViewIfNeeded();
  const handleBox = await handle.boundingBox();
  await page.mouse.move(handleBox.x + 12, handleBox.y + 15); await page.mouse.down();
  await page.mouse.move(handleBox.x + 12, handleBox.y + 70, { steps: 4 });
  await page.keyboard.press('Escape'); await page.mouse.up();
  assert.equal(await page.locator('.sorting-marker').count(), 0);
  assert.equal(await page.locator('#evaluation-workspace').getAttribute('data-version'), oldVersion);
  await page.mouse.move(handleBox.x + 12, handleBox.y + 15); await page.mouse.down();
  await page.mouse.move(100, 300, { steps: 6 }); await page.mouse.up();
  assert.equal(await page.locator('.sorting-marker').count(), 0);
  assert.equal(await page.locator('#evaluation-workspace').getAttribute('data-version'), oldVersion);
  record('Drag sorting in both directions preserves identity and open drafts; Escape/outside drops cancel');

  // Drag a group across another group using its header, then move a criterion into the empty group.
  await group(groupIds[2]).locator('.group-heading').scrollIntoViewIfNeeded();
  await page.evaluate(() => window.scrollBy(0, 200));
  await drag(group(groupIds[2]).locator('.group-heading [data-drag-handle]'), group(groupIds[1]).locator('.group-heading'), 'before');
  assert.deepEqual(await page.locator('.criterion-group[data-sort-kind="group"]').evaluateAll(groups => groups.map(element => element.dataset.groupId)), [groupIds[0], groupIds[2], groupIds[1]]);
  await page.evaluate(() => window.scrollTo(0, 220));
  await drag(row('NFC Karte').locator('[data-drag-handle]'), group(groupIds[2]).locator('.criterion-list'));
  assert.deepEqual(await order(groupIds[2]), ['NFC Karte']);
  assert.equal(await row('NFC Karte').getAttribute('data-criterion-id'), nfcId);
  assert.equal(await row('NFC Karte').getByLabel('Gruppe', { exact: true }).inputValue(), groupIds[2]);
  await page.reload();
  assert.deepEqual(await order(groupIds[2]), ['NFC Karte']);
  assert.deepEqual(await page.locator('.criterion-group[data-sort-kind="group"]').evaluateAll(groups => groups.map(element => element.dataset.groupId)), [groupIds[0], groupIds[2], groupIds[1]]);
  assert.deepEqual(await order(groupIds[0]), ['Standardschlüssel', 'UWB Smart Key', 'App Key (Bluetooth)', 'Notschlüssel']);
  const groupUp = page.getByRole('button', { name: 'Gruppe Alltag nach oben', exact: true });
  await groupUp.scrollIntoViewIfNeeded();
  const groupBefore = await snapshot();
  await mutate(page, () => groupUp.click());
  await assertViewport(groupBefore);
  await mutate(page, () => page.getByRole('button', { name: 'Gruppe Alltag nach unten', exact: true }).click());
  await drag(row('NFC Karte').locator('[data-drag-handle]'), group('').locator('.criterion-list'));
  assert.deepEqual(await order(''), ['NFC Karte']);
  await drag(row('NFC Karte').locator('[data-drag-handle]'), group(groupIds[2]).locator('.criterion-list'));
  assert.deepEqual(await order(groupIds[2]), ['NFC Karte']);
  record('Group arrows preserve the viewport; drag sorting, empty groups and ungrouped drops persist');

  // Exercise real touch input, not synthetic drag events.
  const touchContext = await owner.context.browser().newContext({ viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true, ignoreHTTPSErrors: origin.startsWith('https:') });
  await touchContext.addCookies(await owner.context.cookies());
  const touch = await touchContext.newPage();
  touch.setDefaultTimeout(10_000);
  const touchErrors = [];
  touch.on('pageerror', error => touchErrors.push(error.message));
  await touch.goto(`${origin}${path}`);
  assert.ok(await touch.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  const touchSource = touch.getByRole('button', { name: 'Kriterium Standardschlüssel verschieben', exact: true });
  await touchSource.evaluate(element => window.scrollBy(0, element.getBoundingClientRect().top - 250));
  const firstBounds = await touchSource.boundingBox();
  const secondBounds = await touch.locator('.criterion').filter({ has: touch.getByRole('heading', { name: 'UWB Smart Key', exact: true }) }).boundingBox();
  const cdp = await touchContext.newCDPSession(touch);
  const point = { x: firstBounds.x + firstBounds.width / 2, y: firstBounds.y + firstBounds.height / 2 };
  await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [point] });
  await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: point.x, y: secondBounds.y + secondBounds.height - 8 }] });
  await touch.locator('.sorting-marker').waitFor({ state: 'visible' });
  await mutate(touch, () => cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] }));
  assert.equal(await touch.locator('.criterion h4').first().textContent(), 'UWB Smart Key');
  await touch.locator('.criteria-main').evaluate(element => window.scrollTo(0, element.getBoundingClientRect().top + scrollY - 20));
  await touch.screenshot({ path: join(output, '09-compact-mobile.png') });
  assert.deepEqual(touchErrors, []);
  await touchContext.close();
  record('390px layout and touch drag sorting work without horizontal overflow');

  await page.reload();
  version = await page.locator('#evaluation-workspace').getAttribute('data-version');
  assert.equal((await post(page, path, { action: 'place-criterion', itemId: nfcId, groupId: groupIds[0], position: '0', version: '1' })).status(), 409);
  assert.equal((await post(page, path, { action: 'place-criterion', itemId: nfcId, groupId: groupIds[0], position: '999', version })).status(), 422);
  await post(page, path, { action: 'delete', version });
  await page.goto(returnUrl);
}
