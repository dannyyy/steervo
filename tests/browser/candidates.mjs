import assert from 'node:assert/strict';
import { checkCanvas } from './canvas.mjs';
import { join } from 'node:path';
import { fillSpecifications, checkSpecificationValidation, editCopiedSpecifications, checkSpecificationsComparison } from './specifications.mjs';

export async function checkCandidates({ page, reader, origin, output, post, browserRequest, record }) {
  const casePath = new URL(page.url()).pathname;
  const data = JSON.parse(await (await browserRequest(page, `${casePath}?handler=Export`)).text());
  const boolean = data.criteria.find(criterion => criterion.type === 'Boolean').id;
  const measurement = data.criteria.find(criterion => criterion.type === 'Measurement').id;
  const rating = data.criteria.find(criterion => criterion.type === 'Rating').id;
  async function change(target, operation) {
    const version = await target.locator('#candidate-workspace').getAttribute('data-version');
    await operation();
    await target.waitForFunction(previous => {
      const workspace = document.querySelector('#candidate-workspace');
      return workspace && workspace.dataset.version !== previous;
    }, version);
    await target.waitForFunction(() => !document.querySelector('.htmx-settling, .htmx-request'));
  }
  const assessment = (target, id) => target.locator(`article[data-assessment-criterion="${id}"]`);
  const values = (target, id) => assessment(target, id).getByLabel('Deine Angabe', { exact: true });
  const open = async (target, id) => {
    if (!await assessment(target, id).locator('[data-editor]').isVisible()) await assessment(target, id).locator('[data-editor-open]').click();
  };
  const classify = async target => { if (!await target.locator('#candidate-classification').isVisible()) await target.locator('#edit-classification').click(); };
  const save = (target, id) => assessment(target, id).getByRole('button', { name: /speichern$/ });
  await page.getByRole('link', { name: 'Autos', exact: true }).click();
  assert.equal(await page.locator('.candidate-card').count(), 0);
  await page.getByRole('button', { name: 'Auto hinzufügen', exact: false }).click();
  const dialog = page.getByRole('dialog', { name: 'Auto hinzufügen', exact: true });
  await dialog.getByLabel('Modell / Bezeichnung').fill('Nord');
  await dialog.getByLabel('Marke', { exact: false }).fill('Beispiel');
  await fillSpecifications(dialog);
  await dialog.getByLabel('Quellen und Links').fill('javascript:alert(1)');
  await dialog.getByRole('button', { name: 'Auto anlegen', exact: false }).click();
  await dialog.locator('.editor-error').waitFor();
  assert.equal(await dialog.getByLabel('Modell / Bezeichnung').inputValue(), 'Nord');
  assert.equal(await dialog.locator('#spec-AccelerationSeconds').inputValue(), '4.5');
  await dialog.getByLabel('Quellen und Links').fill('https://example.test/specification');
  await dialog.getByLabel('Notizen', { exact: false }).fill('Eine frei erfasste Beobachtung.');
  await dialog.getByRole('button', { name: 'Auto anlegen', exact: false }).click();
  await page.getByRole('heading', { name: 'Beispiel Nord', exact: true }).waitFor();
  const firstPath = new URL(page.url()).pathname;
  const firstId = firstPath.split('/').at(-1);
  const details = page.locator('#candidate-edit');
  await page.locator('#edit-candidate').click();
  await checkSpecificationValidation(page, details);
  await details.getByLabel('Notizen', { exact: false }).fill('Aktualisierte Beobachtung.');
  await change(page, () => details.getByRole('button', { name: 'Speichern', exact: true }).click());
  assert.equal(await page.locator('.candidate-notes').textContent(), 'Aktualisierte Beobachtung.');
  assert.equal(await details.isVisible(), false);
  assert.equal(await assessment(page, boolean).getAttribute('data-result'), 'Unknown');
  assert.equal(await values(page, boolean).inputValue(), '');
  await open(page, boolean);
  await values(page, boolean).selectOption('false');
  await assessment(page, boolean).getByLabel('Grundlage', { exact: true }).selectOption('Inspection');

  await assessment(page, boolean).getByLabel('Was hast du festgestellt?').fill('Bei der Besichtigung nicht vorhanden.');
  await change(page, () => save(page, boolean).click());
  assert.equal(await assessment(page, boolean).getAttribute('data-result'), 'Unmet');
  await page.locator('.candidate-warning').filter({ hasText: 'Gute Sicht' }).waitFor();
  assert.equal(await page.getByLabel('Einstufung', { exact: true }).inputValue(), 'Watching');
  await open(page, measurement);
  await values(page, measurement).fill('0');
  await change(page, () => save(page, measurement).click());
  assert.equal(await values(page, measurement).inputValue(), '0');
  await open(page, rating);
  await values(page, rating).selectOption('1');
  await change(page, () => save(page, rating).click());
  assert.equal(await assessment(page, rating).getAttribute('data-result'), 'Rated');
  record('Candidates accept incomplete details and safe links; unknown, false, zero and subjective ratings stay distinct');

  // Switching editors must never silently discard an unfinished observation.
  await open(page, measurement);
  await assessment(page, measurement).getByLabel('Was hast du festgestellt?').fill('Noch nicht gespeichert');
  await assessment(page, boolean).locator('[data-editor-open]').click();
  const discard = page.getByRole('dialog', { name: 'Ungespeicherte Änderungen verwerfen?' });
  await discard.getByRole('button', { name: 'Weiterbearbeiten' }).click();
  assert.equal(await assessment(page, measurement).getByLabel('Was hast du festgestellt?').inputValue(), 'Noch nicht gespeichert');
  assert.equal(await assessment(page, measurement).locator('[data-editor]').isVisible(), true);
  await change(page, () => save(page, measurement).click());
  await open(page, boolean);
  await values(page, boolean).selectOption('true');
  await change(page, () => save(page, boolean).click());
  await classify(page);
  await page.getByLabel('Einstufung', { exact: true }).selectOption('Excluded');
  const failed = page.waitForResponse(response => response.request().method() === 'POST' && new URL(response.url()).pathname === firstPath);
  await page.locator('#candidate-classification').getByRole('button', { name: 'Speichern', exact: true }).click();
  assert.equal((await failed).status(), 422);
  await page.getByText('Bitte begründe, warum du dieses Auto zurückstellst oder ausscheiden lässt.', { exact: true }).waitFor();
  assert.equal(await page.getByLabel('Einstufung', { exact: true }).inputValue(), 'Excluded');
  await page.getByLabel('Begründung', { exact: true }).fill('Passt nicht zu meinen Anforderungen.');
  await change(page, () => page.locator('#candidate-classification').getByRole('button', { name: 'Speichern', exact: true }).click());
  await page.reload();
  assert.equal(await page.getByLabel('Einstufung', { exact: true }).inputValue(), 'Excluded');
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: join(output, '10-candidate-desktop.png'), fullPage: true });
  record('Editor switching protects unsaved notes; exclusion requires a reason and persists');

  await page.getByRole('link', { name: /Alle Autos/ }).click();
  await page.locator('.candidate-card').locator('summary').click();
  await page.getByRole('button', { name: 'Auto kopieren', exact: true }).click();
  await page.getByLabel('Bezeichnung der Kopie').fill('Süd');
  await page.getByRole('dialog', { name: 'Auto kopieren', exact: true }).getByRole('button', { name: 'Auto kopieren', exact: true }).click();
  await page.getByRole('heading', { name: 'Beispiel Süd', exact: true }).waitFor();
  const secondId = new URL(page.url()).pathname.split('/').at(-1);
  await editCopiedSpecifications(page, change);
  assert.equal(await page.getByLabel('Einstufung', { exact: true }).inputValue(), 'Watching');
  assert.equal(await values(page, boolean).inputValue(), 'true');
  await open(page, measurement);
  await change(page, async () => {
    await assessment(page, measurement).getByRole('button', { name: /zurücksetzen$/ }).click();
    await page.locator('#action-confirmation [data-confirm-accept]').click();
  });
  assert.equal(await assessment(page, measurement).getAttribute('data-result'), 'Unknown');
  assert.equal(await values(page, measurement).inputValue(), '');
  assert.equal(await assessment(page, measurement).getByLabel('Was hast du festgestellt?').inputValue(), '');
  await open(page, measurement);
  await values(page, measurement).fill('0');
  await change(page, () => save(page, measurement).click());
  await open(page, boolean);
  await values(page, boolean).selectOption('false');
  await change(page, () => save(page, boolean).click());
  await page.goto(`${origin}${firstPath}`);
  assert.equal(await values(page, boolean).inputValue(), 'true');
  await page.goto(`${origin}${casePath}/compare?first=${firstId}&second=${secondId}`);
  await checkSpecificationsComparison(page, firstId, secondId);
  record('Optional specifications preserve decimal inputs and calculation sources; validation, independent copies and neutral comparison work');
  const comparison = page.locator(`[data-comparison-criterion="${boolean}"]`);
  const firstCell = comparison.locator(`[data-comparison-candidate="${firstId}"]`);
  const secondCell = comparison.locator(`[data-comparison-candidate="${secondId}"]`);
  assert.equal(await firstCell.locator('.comparison-value').textContent(), 'Ja');
  assert.equal(await secondCell.locator('.comparison-value').textContent(), 'Nein');
  await secondCell.locator('[data-editor-open]').click();
  const comparisonEditor = page.locator(`#editor-assessment-${secondId}-${boolean}`);
  await comparisonEditor.getByLabel('Deine Angabe', { exact: true }).selectOption('true');
  await change(page, () => comparisonEditor.getByRole('button', { name: /speichern$/ }).click());
  assert.equal(await secondCell.locator('.comparison-value').textContent(), 'Ja');
  assert.equal(await page.getByLabel('Erstes Auto', { exact: true }).inputValue(), firstId);
  assert.equal(await page.getByLabel('Zweites Auto', { exact: true }).inputValue(), secondId);
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: join(output, '11-comparison-desktop.png'), fullPage: true });
  record('Candidate copies have independent assessments; comparison edits preserve the selected pair');

  await checkCanvas({ page, origin, casePath, firstId, secondId, boolean, rating, output, record });

  // Eight candidates remain selectable on a phone without widening the document.
  await page.goto(`${origin}${casePath}/candidates`);
  let version = await page.locator('#candidate-workspace').getAttribute('data-version');
  for (let index = 3; index <= 8; index++) {
    const response = await post(page, `${casePath}/candidates`, { action: 'add-candidate', 'Input.Model': `Kandidat ${index}`, version });
    assert.equal(response.status(), 0); // fetch's manual redirect represents a successful create as an opaque redirect.
    await page.reload();
    version = await page.locator('#candidate-workspace').getAttribute('data-version');
  }
  assert.equal(await page.locator('.candidate-card').count(), 8);
  const moving = page.locator(`[data-candidate-id="${secondId}"]`);
  await change(page, () => moving.getByRole('button', { name: /nach oben$/ }).click());
  assert.equal(await page.locator('.candidate-card').first().getAttribute('data-candidate-id'), secondId);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(`${origin}${casePath}/compare?first=${firstId}&second=${secondId}`);
  assert.equal(await page.locator('#compare-first option').count(), 8);
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: join(output, '12-comparison-mobile.png'), fullPage: true });
  await page.goto(`${origin}${firstPath}`);
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  await open(page, rating);
  await assessment(page, rating).getByLabel('Deine Angabe', { exact: true }).selectOption('4');
  await change(page, () => save(page, rating).click());
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: join(output, '13-candidate-mobile.png'), fullPage: true });
  const mobileDetails = page.locator('#candidate-edit');
  await page.locator('#edit-candidate').click();
  await mobileDetails.locator('#spec-section-Electric > summary').click();
  await mobileDetails.locator('#spec-Charging20To80Minutes').fill('23.5');
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  await page.screenshot({ path: join(output, '14-specifications-mobile-edit.png'), fullPage: true });
  await change(page, () => mobileDetails.getByRole('button', { name: 'Speichern', exact: true }).click());
  assert.match(await page.locator('#candidate-specifications [data-specification="Charging20To80Minutes"]').textContent(), /23,5 min/);
  await page.setViewportSize({ width: 1440, height: 1050 });
  record('Eight candidates, manual ordering, mobile assessment and two-car comparison work at 390px');

  await page.goto(`${origin}${casePath}`);
  let mainVersion = await page.locator('#evaluation-workspace').getAttribute('data-version');
  const readerEmail = await page.locator('.share-row').locator('span').first().textContent();
  assert.equal((await post(page, casePath, { action: 'share', email: readerEmail, access: 'Reader', version: mainVersion })).status(), 200);
  await reader.page.goto(`${origin}${firstPath}`);
  assert.equal(await reader.page.locator('.value-editor').count(), 0);
  assert.match(await reader.page.locator('#candidate-specifications [data-specification="PayloadKg"]').textContent(), /400 kg/);
  version = await reader.page.locator('#candidate-workspace').getAttribute('data-version');
  for (const action of ['edit-candidate', 'classify-candidate', 'assess-candidate', 'clear-assessment']) {
    assert.equal((await post(reader.page, firstPath, { action, criterionId: boolean, booleanValue: 'true', version })).status(), 403);
  }
  for (const action of ['add-candidate', 'copy-candidate', 'move-candidate', 'remove-candidate']) {
    assert.equal((await post(reader.page, `${casePath}/candidates`, { action, itemId: firstId, 'Input.Model': 'Blocked', version })).status(), 403);
  }
  await reader.page.goto(`${origin}${casePath}/compare?first=${firstId}&second=${secondId}`);
  assert.equal(await reader.page.locator('[data-editor-open]').count(), 0);
  assert.equal((await post(reader.page, `${casePath}/compare?first=${firstId}&second=${secondId}`, { action: 'assess-candidate', candidateId: firstId, criterionId: boolean, booleanValue: 'true', version })).status(), 403);
  await page.reload();
  mainVersion = await page.locator('#evaluation-workspace').getAttribute('data-version');
  assert.equal((await post(page, casePath, { action: 'share', email: readerEmail, access: 'Editor', version: mainVersion })).status(), 200);
  await reader.page.goto(`${origin}${firstPath}`);
  await open(reader.page, rating);
  await values(reader.page, rating).selectOption('2');
  await change(reader.page, () => save(reader.page, rating).click());
  await page.goto(`${origin}${firstPath}`);
  version = await page.locator('#candidate-workspace').getAttribute('data-version');
  assert.equal(await values(page, rating).inputValue(), '2');
  assert.equal((await post(page, firstPath, { action: 'assess-candidate', criterionId: rating, ratingValue: '9', version })).status(), 422);
  assert.equal((await post(page, firstPath, { action: 'assess-candidate', criterionId: boolean, booleanValue: 'false', version: '1' })).status(), 409);
  const exported = JSON.parse(await (await browserRequest(page, `${casePath}?handler=Export`)).text());
  assert.equal(exported.version, 6);
  assert.equal(exported.candidates.length, 8);
  const specifications = exported.candidates.find(candidate => candidate.id === firstId).specifications;
  assert.equal(specifications.kerbWeightKg, 2100);
  assert.equal(specifications.payloadKg, null);
  assert.equal(specifications.accelerationSeconds, 4.5);
  assert.equal(specifications.powerKw, null);
  assert.equal(specifications.powerPs, 400);
  assert.equal(specifications.charging20To80Minutes, 23.5);
  assert.ok(exported.assessments.length >= 6);
  assert.equal(exported.assessments.find(value => value.candidateId === firstId && value.criterionId === boolean).booleanValue, true);
  record('Candidate and comparison writes enforce reader/editor roles, validation and conflicts; exports contain all observations');
  await page.goto(`${origin}${casePath}`);
  return { firstId, boolean, rating };
}
