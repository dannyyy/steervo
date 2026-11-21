import assert from 'node:assert/strict';
import { join } from 'node:path';

export async function checkEvaluations({ page, reader, readerEmail, origin, output, post, browserRequest, mutate, record }) {
  const returnUrl = page.url();
  await page.goto(origin);
  await page.locator('#add-evaluation').click();
  const creation = page.locator('#new-evaluation');
  assert.equal(await page.locator('#new-region-country').inputValue(), '');
  assert.equal(await page.locator('#new-region-currency').inputValue(), '');
  for (const fields of [{ Currency: 'EUR' }, { Country: 'CH' }, { Country: 'ZZ', Currency: 'CHF' }])
    assert.equal((await post(page, '/', { Name: 'Must not create', ...fields })).status(), 422);
  record('Creation requires an explicit valid country and currency on the server without defaults');
  const name = 'Eine gemeinsame Entscheidung mit Platz für längere Namen und eigene Anforderungen';
  await creation.getByLabel('Name der Evaluation').fill(name);
  await page.locator('#new-region-country').selectOption('CH');
  await page.locator('#new-region-currency').fill('EUR');
  await creation.getByRole('button', { name: 'Evaluation anlegen', exact: true }).click();
  await page.waitForURL(/\/evaluations\//);
  const path = new URL(page.url()).pathname;
  const version = () => page.locator('#evaluation-workspace').getAttribute('data-version');
  const submit = async (button, status) => {
    const response = page.waitForResponse(r => new URL(r.url()).pathname === path && r.request().method() === 'POST');
    await button.click(); assert.equal((await response).status(), status);
    await page.waitForFunction(() => !document.querySelector('.htmx-settling, .htmx-request'));
  };
  const exportCase = async () => {
    const response = await browserRequest(page, `${path}?handler=Export`);
    assert.equal(response.status(), 200, 'Export must be authorized and available');
    return JSON.parse(await response.text());
  };
  await page.locator('#add-group').click();
  const groupEditor = page.locator('#new-group-editor');
  await groupEditor.getByLabel('Gruppenname').fill('Alltag');
  await page.route(`**${path}`, route => route.request().method() === 'POST' ? route.abort() : route.continue());
  await groupEditor.getByRole('button', { name: 'Gruppe hinzufügen', exact: true }).click();
  await groupEditor.locator('.editor-error:not([hidden])').waitFor();
  assert.equal(await groupEditor.getByLabel('Gruppenname').inputValue(), 'Alltag');
  await page.unroute(`**${path}`);
  await mutate(page, () => groupEditor.getByRole('button', { name: 'Gruppe hinzufügen', exact: true }).click());
  assert.equal(await page.locator('#add-group').evaluate(x => x === document.activeElement), true);
  record('Group creation retains its draft after a network failure and restores opener focus after retry');

  const groupId = (await exportCase()).groups[0].id;
  for (const criterion of ['Lange Strecken ohne Ladestopp', 'Gute Sicht beim Einparken']) {
    await page.locator('#add-criterion').click();
    const editor = page.locator('#new-criterion-editor');
    await editor.getByLabel('Bezeichnung').fill(criterion);
    await editor.getByLabel('Gruppe', { exact: true }).selectOption(groupId);
    await mutate(page, () => editor.getByRole('button', { name: 'Kriterium hinzufügen', exact: true }).click());
  }
  const data = await exportCase();
  const id = data.criteria[0].id;
  const editor = page.locator(`#criterion-editor-${id}`);
  await page.locator(`#edit-criterion-${id}`).click();
  await editor.getByLabel('Bezeichnung').fill('Lokaler Entwurf bleibt erhalten');
  // Another user changes the group before the local sorting request.
  assert.equal((await post(page, path, { action: 'rename-group', itemId: groupId, name: 'Unterwegs', version: await version() })).status(), 200);
  await submit(page.getByRole('button', { name: `Kriterium ${data.criteria[1].name} nach oben`, exact: true }), 409);
  assert.equal(await editor.isVisible(), true);
  assert.equal(await editor.getByLabel('Bezeichnung').inputValue(), 'Lokaler Entwurf bleibt erhalten');
  await page.getByRole('heading', { name: 'Unterwegs', exact: true }).waitFor();
  await editor.getByRole('button', { name: 'Abbrechen', exact: true }).click();
  await page.getByRole('button', { name: 'Weiterbearbeiten', exact: true }).click();
  assert.equal(await editor.getByLabel('Bezeichnung').inputValue(), 'Lokaler Entwurf bleibt erhalten');
  await mutate(page, () => editor.getByRole('button', { name: 'Speichern', exact: true }).click());
  assert.equal((await exportCase()).criteria.find(c => c.id === id).name, 'Lokaler Entwurf bleibt erhalten');
  record('A stale sorting request shows the current server state while preserving the open criterion draft');

  await page.locator(`#edit-group-${groupId}`).click();
  const editGroup = page.locator(`#group-editor-${groupId}`);
  await editGroup.getByLabel('Gruppenname').fill('Mein Gruppenname');
  assert.equal((await post(page, path, { action: 'rename-group', itemId: groupId, name: 'Anderer Gruppenname', version: await version() })).status(), 200);
  await submit(editGroup.getByRole('button', { name: 'Speichern', exact: true }), 409);
  assert.equal(await editGroup.getByLabel('Gruppenname').inputValue(), 'Mein Gruppenname');
  assert.equal((await exportCase()).groups[0].name, 'Anderer Gruppenname');
  await editGroup.getByRole('button', { name: 'Aktuellen Stand ansehen', exact: true }).click();
  await page.locator(`#edit-group-${groupId}`).click();
  await mutate(page, () => editGroup.getByRole('button', { name: 'Speichern', exact: true }).click());
  assert.equal((await exportCase()).groups[0].name, 'Mein Gruppenname');
  record('Group conflicts retain the local name and require an explicit save after reviewing the current state');

  await page.locator(`#edit-group-${groupId}`).click();
  await editGroup.getByLabel('Gruppenname').fill('Entwurf vor dem Auflösen');
  assert.equal((await post(page, path, { action: 'rename-group', itemId: groupId, name: 'Aktueller Name', version: await version() })).status(), 200);
  await editGroup.getByRole('button', { name: 'Gruppe auflösen', exact: true }).click();
  await submit(page.locator('#action-confirmation [data-confirm-accept]'), 409);
  assert.equal(await editGroup.getByLabel('Gruppenname').inputValue(), 'Entwurf vor dem Auflösen');
  assert.equal((await exportCase()).groups.length, 1);
  await editGroup.getByRole('button', { name: 'Abbrechen', exact: true }).click();
  await page.getByRole('button', { name: 'Änderungen verwerfen', exact: true }).click();
  record('A failed group removal preserves the adjacent rename draft and does not remove criteria');

  await page.locator('.evaluation-tools > summary').click();
  assert.equal(await page.locator('.evaluation-tools').getByRole('link', { name: 'Evaluation bearbeiten', exact: true }).count(), 0);
  await page.locator('.evaluation-tools > summary').click();
  await page.locator('.workspace-subnav').getByRole('link', { name: 'Allgemein', exact: true }).click();
  assert.equal((await page.locator('.workspace-subnav [aria-current=page]').textContent()).trim(), 'Allgemein');
  assert.equal((await page.locator('.workflow-nav a.active').textContent()).trim(), 'Grundlagen');
  await page.locator('#edit-evaluation').click();
  await page.locator('#case-name').fill('   ');
  await page.locator('#case-purpose').fill('Eine erhaltene Notiz zur Entscheidung.');
  await page.locator('#case-region-country').selectOption('DE');
  await page.locator('#case-region-currency').fill('CHF');
  await submit(page.locator('#save-evaluation'), 422);
  assert.equal(await page.locator('#case-purpose').inputValue(), 'Eine erhaltene Notiz zur Entscheidung.');
  assert.equal(await page.locator('#case-region-country').inputValue(), 'DE');
  assert.equal(await page.locator('#case-region-currency').inputValue(), 'CHF');
  await page.locator('#case-name').fill(name);
  await mutate(page, () => page.locator('#save-evaluation').click());
  assert.match(await page.locator('.evaluation-facts').innerText(), /Eine erhaltene Notiz/);
  assert.equal((await exportCase()).country, 'DE');
  assert.equal((await exportCase()).currency, 'CHF');
  await page.locator('.evaluation-tools > summary').click(); await page.getByRole('link', { name: 'Freigaben', exact: true }).click();
  await page.locator('#add-share').click();
  const share = page.locator('#share-editor-new');
  await share.getByLabel('E-Mail-Adresse').fill('not-confirmed@example.test');
  await submit(share.getByRole('button', { name: 'Zugriff freigeben', exact: true }), 422);
  assert.equal(await share.getByLabel('E-Mail-Adresse').inputValue(), 'not-confirmed@example.test');
  await share.getByLabel('E-Mail-Adresse').fill(readerEmail);
  await mutate(page, () => share.getByRole('button', { name: 'Zugriff freigeben', exact: true }).click());
  await page.locator('.share-row [data-editor-open]').click();
  const roleEditor = page.locator('.share-entry [data-editor]');
  await roleEditor.getByLabel('Berechtigung').selectOption('Editor');
  await mutate(page, () => roleEditor.getByRole('button', { name: 'Speichern', exact: true }).click());
  assert.match(await page.locator('.share-row').innerText(), /Bearbeiter/);
  record('Settings and permissions use the shared editor, retain validation drafts and update the reading view');

  for (const width of [320, 390, 768, 1024, 1440]) {
    await page.setViewportSize({ width, height: width < 700 ? 844 : 1050 });
    for (const section of ['criteria', 'sharing', 'settings']) {
      await page.goto(`${origin}${path}?section=${section}`);
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${section} overflows at ${width}`);
      await page.locator('.evaluation-tools > summary').click();
      const menu = await page.locator('.evaluation-tools > div').boundingBox();
      assert.ok(menu.x >= 0 && menu.x + menu.width <= width, `management menu overflows at ${width}`);
      await page.locator('.evaluation-tools > summary').click();
      if ([390, 1440].includes(width)) await page.screenshot({ path: join(output, `phase2-${section}-${width}.png`), fullPage: true });
    }
  }
  await page.goto(`${origin}${path}#sharing`);
  await page.waitForURL(`**?section=sharing`);
  assert.equal((await page.locator('.evaluation-tools [aria-current=page]').textContent()).trim(), 'Freigaben');
  await reader.page.goto(`${origin}${path}`);
  await reader.page.locator('#add-group').click();
  await reader.page.locator('#new-group-editor').getByLabel('Gruppenname').fill('Nicht verlieren');
  await mutate(page, () => page.locator('.share-row').getByRole('button', { name: 'Entziehen', exact: true }).click());
  const denied = reader.page.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname === path);
  await reader.page.locator('#save-group-new').click(); assert.equal((await denied).status(), 403);
  await reader.page.locator('#new-group-editor .editor-error:not([hidden])').waitFor();
  assert.equal(await reader.page.locator('#group-name-new').inputValue(), 'Nicht verlieren');
  assert.equal(await reader.page.locator('#save-group-new').isDisabled(), true);
  assert.equal((await exportCase()).groups.length, 1);
  // Remove the stale page without interacting with any other user's data.
  await reader.page.goto(origin);
  record('Desktop/mobile sections fit the viewport; old links work and real permission revocation protects the draft');

  await page.getByRole('link', { name: 'Grundlagen', exact: true }).click();
  await page.locator(`#edit-criterion-${id}`).click();
  await editor.getByRole('button', { name: 'Kriterium löschen', exact: true }).click();
  assert.match(await page.locator('#confirmation-title').innerText(), /Lokaler Entwurf bleibt erhalten/);
  await page.keyboard.press('Escape');
  assert.equal((await exportCase()).criteria.length, 2);
  await editor.getByRole('button', { name: 'Kriterium löschen', exact: true }).click();
  await mutate(page, () => page.locator('#action-confirmation [data-confirm-accept]').click());
  assert.equal((await exportCase()).criteria.length, 1);
  assert.equal(await page.locator('#criteria-title').evaluate(x => x === document.activeElement), true);
  await page.locator(`#edit-group-${groupId}`).click();
  await editGroup.getByRole('button', { name: 'Gruppe auflösen', exact: true }).click();
  await mutate(page, () => page.locator('#action-confirmation [data-confirm-accept]').click());
  const ungrouped = await exportCase();
  assert.equal(ungrouped.groups.length, 0);
  assert.equal(ungrouped.criteria.length, 1);
  assert.equal(ungrouped.criteria[0].groupId, null);
  record('Criterion deletion targets only the confirmed criterion; dissolving a group retains its remaining criteria');

  await page.locator('.workspace-subnav').getByRole('link', { name: 'Allgemein', exact: true }).click();
  await page.getByRole('button', { name: 'Evaluation löschen', exact: true }).click();
  const confirmation = page.locator('#action-confirmation');
  assert.match(await confirmation.innerText(), new RegExp(name));
  assert.equal(await confirmation.locator('[data-confirm-cancel]').evaluate(x => x === document.activeElement), true);
  await page.keyboard.press('Escape');
  assert.equal((await exportCase()).name, name);
  await page.getByRole('button', { name: 'Evaluation löschen', exact: true }).click();
  await confirmation.locator('[data-confirm-accept]').click();
  await page.waitForURL(origin + '/');
  assert.equal((await browserRequest(page, path)).status(), 404);
  record('Evaluation deletion names its subject, safely cancels, and redirects only after confirmed removal');

  const legacy = { ...ungrouped, name: 'Imported legacy evaluation', version: 7 }; delete legacy.country;
  await page.locator('#import-evaluation').click();
  await page.locator('#upload').setInputFiles({ name: 'legacy.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(legacy)) });
  await page.locator('#save-import').click();
  await page.waitForURL(/\/evaluations\//);
  const importedPath = new URL(page.url()).pathname;
  await page.locator('[data-regional-settings-notice]').getByRole('link').click();
  assert.equal(await page.locator('#evaluation-editor').isVisible(), true);
  assert.equal(await page.locator('#case-region-country').inputValue(), '');
  assert.equal(await page.locator('#case-region-currency').inputValue(), 'CHF');
  await page.locator('#case-region-country').selectOption('LI');
  await mutate(page, () => page.locator('#save-evaluation').click());
  assert.equal(await page.locator('[data-regional-settings-notice]').count(), 0);
  const completed = JSON.parse(await (await browserRequest(page, `${importedPath}?handler=Export`)).text());
  assert.equal(completed.country, 'LI'); assert.equal(completed.currency, 'CHF'); assert.equal(completed.version, 8);
  record('Legacy imports keep their currency and unknown country, then allow explicit completion in properties');
  await page.goto(returnUrl);
}
