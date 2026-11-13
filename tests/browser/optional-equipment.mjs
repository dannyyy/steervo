import assert from 'node:assert/strict';
import { join } from 'node:path';

export async function checkOptionalEquipment({ page, reader, readerEmail, origin, output, post, browserRequest, record, casePath, firstId, boolean, offerChecks }) {
  const exportCase = async path => JSON.parse(await (await browserRequest(page, `${path}?handler=Export`)).text());
  const original = await exportCase(casePath);
  const secondId = original.candidates.find(c => c.id !== firstId).id;
  const firstPath = `${casePath}/candidates/${firstId}`;
  const comparePath = `${casePath}/compare?first=${firstId}&second=${secondId}`;
  const field = candidate => `assessment-${candidate}-${boolean}`;
  const editor = candidate => page.locator(`#editor-${field(candidate)}`);
  const opener = candidate => page.locator(`#open-${field(candidate)}`);
  const value = candidate => editor(candidate).getByLabel('Deine Angabe', { exact: true });
  const note = candidate => editor(candidate).locator('textarea[name=notes]');
  const settle = () => page.waitForFunction(() => !document.querySelector('.htmx-request, .htmx-settling'));
  async function submit(candidate, status = 200) {
    const path = new URL(page.url()).pathname;
    const response = page.waitForResponse(r => new URL(r.url()).pathname === path && r.request().method() === 'POST');
    await editor(candidate).locator('.value-editor button[type=submit]').click();
    assert.equal((await response).status(), status); await settle();
  }
  await page.setViewportSize({ width: 1440, height: 1050 });
  await page.goto(origin + firstPath);
  await opener(firstId).click();
  await value(firstId).selectOption('optional');
  await note(firstId).fill('Separat beim Hersteller oder einem Drittanbieter erhältlich.');
  await editor(firstId).locator('select[name=source]').evaluate(el => { el.add(new Option('Invalid', 'Invalid')); el.value = 'Invalid'; });
  await submit(firstId, 422);
  assert.equal(await value(firstId).inputValue(), 'optional');
  assert.match(await note(firstId).inputValue(), /Drittanbieter/);
  await editor(firstId).locator('select[name=source]').selectOption('Research');
  await page.route(`**${firstPath}*`, route => route.request().method() === 'POST' ? route.abort('failed') : route.continue());
  await editor(firstId).locator('.value-editor button[type=submit]').click();
  await editor(firstId).locator('.editor-error').getByText(/nicht gespeichert/).waitFor();
  assert.equal(await value(firstId).inputValue(), 'optional');
  assert.match(await note(firstId).inputValue(), /Drittanbieter/);
  await page.unroute(`**${firstPath}*`);
  await submit(firstId);
  assert.equal(await opener(firstId).evaluate(el => el === document.activeElement), true);
  assert.equal(await opener(firstId).locator('.fulfillment').textContent(), 'Optional erhältlich');
  assert.equal(await page.locator(`article[data-assessment-criterion="${boolean}"]`).getAttribute('data-result'), 'Optional');
  await page.reload();
  await opener(firstId).click();
  assert.equal(await value(firstId).inputValue(), 'optional');
  await page.screenshot({ path: join(output, 'optional-equipment-editor.png'), fullPage: true });
  await page.keyboard.press('Escape');
  await page.screenshot({ path: join(output, 'optional-equipment-desktop.png'), fullPage: true });
  record('Optional equipment persists in the vehicle record and retains its value and note after validation and network failures');

  await page.goto(`${origin}${casePath}/candidates`);
  const card = page.locator(`[data-candidate-id="${firstId}"]`);
  if (original.criteria.find(c => c.id === boolean).priority === 'Required') {
    assert.match(await card.locator('.candidate-optional').textContent(), /optional erhältlich/);
  }
  for (const width of [320, 390, 768, 1024, 1440]) {
    await page.setViewportSize({ width, height: 1000 });
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  }
  await page.screenshot({ path: join(output, 'optional-equipment-list.png'), fullPage: true });
  await page.goto(origin + comparePath);
  await page.setViewportSize({ width: 390, height: 844 });
  const scroll = page.locator('#vehicle-comparison-scroll');
  await scroll.evaluate(el => { el.scrollLeft = el.scrollWidth; });
  const horizontal = await scroll.evaluate(el => el.scrollLeft);
  await opener(secondId).click();
  await value(secondId).selectOption('optional');
  await note(secondId).fill('Zusätzlicher physischer Schlüssel, separat erhältlich.');
  const version = await page.locator('#candidate-workspace').getAttribute('data-version');
  assert.equal((await post(page, `${casePath}/candidates/${secondId}`, { action: 'assess-candidate', criterionId: boolean, booleanValue: 'false', version })).status(), 200);
  await submit(secondId, 409);
  assert.equal(await value(secondId).inputValue(), 'optional');
  assert.match(await note(secondId).inputValue(), /physischer Schlüssel/);
  await editor(secondId).getByRole('button', { name: 'Aktuellen Stand ansehen' }).click();
  await opener(secondId).click();
  assert.equal(await value(secondId).inputValue(), 'optional');
  await submit(secondId);
  assert.equal(await opener(secondId).evaluate(el => el === document.activeElement), true);
  assert.ok(Math.abs(await scroll.evaluate(el => el.scrollLeft) - horizontal) <= 1);
  assert.equal(await opener(secondId).locator('.comparison-value').textContent(), 'Optional');
  await page.screenshot({ path: join(output, 'optional-equipment-mobile.png'), fullPage: true });
  for (const width of [320, 390, 768, 1024, 1440]) {
    await page.setViewportSize({ width, height: 1000 });
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  }
  record('Optional equipment in the mobile comparison preserves drafts, focus and horizontal position through a version conflict');

  await page.goto(origin + firstPath);
  let currentVersion = await page.locator('#candidate-workspace').getAttribute('data-version');
  for (const invalid of ['maybe', 'OptionalAvailable=true']) {
    assert.equal((await post(page, firstPath, { action: 'assess-candidate', criterionId: boolean, booleanValue: invalid, version: currentVersion })).status(), 422);
  }
  const measurement = original.criteria.find(c => c.type === 'Measurement');
  assert.equal((await post(page, firstPath, { action: 'assess-candidate', criterionId: measurement.id, booleanValue: 'optional', version: currentVersion })).status(), 422);
  assert.equal((await post(page, casePath, { action: 'share', email: readerEmail, access: 'Reader', version: currentVersion })).status(), 200);
  await reader.page.goto(origin + firstPath);
  assert.equal(await reader.page.locator('.value-editor').count(), 0);
  assert.match(await reader.page.locator(`article[data-assessment-criterion="${boolean}"]`).textContent(), /Optional erhältlich/);
  currentVersion = await reader.page.locator('#candidate-workspace').getAttribute('data-version');
  assert.equal((await post(reader.page, firstPath, { action: 'assess-candidate', criterionId: boolean, booleanValue: 'optional', version: currentVersion })).status(), 403);
  assert.equal((await post(reader.page, comparePath, { action: 'assess-candidate', candidateId: firstId, criterionId: boolean, booleanValue: 'optional', version: currentVersion })).status(), 403);
  await page.goto(origin + firstPath);
  currentVersion = await page.locator('#candidate-workspace').getAttribute('data-version');
  assert.equal((await post(page, casePath, { action: 'share', email: readerEmail, access: 'Editor', version: currentVersion })).status(), 200);
  const exported = await exportCase(casePath);
  for (const candidateId of [firstId, secondId]) {
    const answer = exported.assessments.find(a => a.candidateId === candidateId && a.criterionId === boolean);
    assert.equal(answer.optionalAvailable, true); assert.equal(answer.booleanValue, null);
  }
  record('Optional answers remain visible to readers, reject unauthorized or invalid writes, and export without becoming yes or unknown');

  // Restore shared fixtures so the existing restart checks keep their independent expectations.
  for (const candidateId of [firstId, secondId]) {
    const answer = original.assessments.find(a => a.candidateId === candidateId && a.criterionId === boolean);
    await page.goto(`${origin}${casePath}/candidates/${candidateId}`);
    currentVersion = await page.locator('#candidate-workspace').getAttribute('data-version');
    assert.equal((await post(page, `${casePath}/candidates/${candidateId}`, { action: answer ? 'assess-candidate' : 'clear-assessment', criterionId: boolean,
      booleanValue: answer?.booleanValue == null ? '' : String(answer.booleanValue), source: answer?.source || 'Unknown', notes: answer?.notes || '', version: currentVersion })).status(), 200);
  }

  const offerData = await exportCase(offerChecks.casePath);
  const offerCriterion = offerData.criteria.find(c => c.type === 'Boolean').id;
  const offerPath = `${offerChecks.casePath}/offers/${offerChecks.firstId}`;
  await page.goto(origin + offerPath);
  const row = page.locator(`[data-offer-assessment="${offerCriterion}"]`);
  await row.locator('[data-editor-open]').click();
  await row.getByLabel('Deine Angabe', { exact: true }).selectOption('optional');
  await row.locator('textarea[name=notes]').fill('Zubehör separat bestellbar.');
  const response = page.waitForResponse(r => new URL(r.url()).pathname === offerPath && r.request().method() === 'POST');
  await row.locator('.value-editor button[type=submit]').click();
  assert.equal((await response).status(), 200); await settle();
  assert.equal(await row.getAttribute('data-result'), 'Optional');
  await page.reload();
  assert.equal(await row.getByLabel('Deine Angabe', { exact: true }).inputValue(), 'optional');
  await page.getByRole('link', { name: 'Offerten vergleichen', exact: true }).click();
  const offerColumn = page.locator('tr').filter({ has: page.getByText(offerData.criteria.find(c => c.id === offerCriterion).name, { exact: true }) });
  assert.match(await offerColumn.textContent(), /Optional erhältlich/);
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 1000 });
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  }
  record('The shared optional answer also persists in offer assessments and appears in the offer comparison');
  await page.setViewportSize({ width: 1440, height: 1050 });
}
