import { checkScenarioWorkflows } from './financial-workflows.mjs';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { join } from 'node:path';

export async function checkScenarios({ page, reader, readerEmail, origin, output, post, browserRequest, record, restart }) {
  const originalPath = new URL(page.url()).pathname;
  async function save(operation, target = page) { await Promise.all([target.waitForNavigation({ waitUntil: 'load' }), operation()]); }
  await page.goto(origin);
  await page.locator('[data-dialog-open="new-evaluation"]').first().click();
  const creation = page.getByRole('dialog', { name: 'Neue Evaluation', exact: true });
  await creation.getByLabel('Name der Evaluation').fill('Szenarienprüfung');
  await creation.getByRole('button', { name: 'Evaluation anlegen', exact: false }).click(); await page.waitForURL(/\/evaluations\//);
  const casePath = new URL(page.url()).pathname; const path = `${casePath}/scenarios`;
  async function version() { await page.goto(`${origin}${casePath}`); return page.locator('#evaluation-workspace').getAttribute('data-version'); }
  async function exportCase() { return JSON.parse(await (await browserRequest(page, `${casePath}?handler=Export`)).text()); }
  let v = await version();
  assert.equal((await post(page, `${casePath}/candidates`, { action: 'add-candidate', 'Input.Model': 'Rechenauto', version: v })).status(), 0);
  const car = (await exportCase()).candidates[0].id;
  await page.goto(`${origin}${casePath}/offers/edit?candidateId=${car}`); await page.locator('#offer-currency').fill('EUR');
  await page.locator('#offer-Name').fill('Rechenofferte'); await page.getByRole('button', { name: /Offerte anlegen/ }).click(); await page.locator('#offer-workspace').waitFor();
  const offer = (await exportCase()).offers[0].id;
  await page.goto(`${origin}${casePath}/costs`); await page.getByLabel('Beginn der Planung').fill('2027-10'); await page.getByLabel('Dauer in Monaten').fill('12');
  await save(() => page.getByRole('button', { name: 'Zeitraum speichern' }).click()); await page.locator('#cost-workspace').waitFor();
  await page.getByRole('link', { name: /Kostenposition hinzufügen/ }).click(); await page.getByLabel('Bezeichnung der Kostenposition').fill('Parkplatz');
  await page.getByLabel('Betrag je Zahlung', { exact: false }).fill('25'); await save(() => page.getByRole('button', { name: 'Speichern', exact: true }).click()); await page.locator('#cost-workspace').waitFor();
  const cost = (await exportCase()).costs[0].id;
  await page.getByRole('link', { name: 'Rechnen', exact: true }).click(); await page.getByLabel('Angenommene Anlagerendite pro Jahr (%)').fill('0');
  await save(() => page.getByRole('button', { name: 'Speichern', exact: true }).click()); await page.locator('#scenario-workspace').waitFor();
  async function create(name, mode, values) {
    await page.goto(`${origin}${path}`); await page.getByRole('link', { name: 'Szenario anlegen', exact: true }).click();
    await page.locator('.scenario-offer-option').first().click(); await page.getByLabel('Name des Szenarios').fill(name);
    await page.getByLabel('Beschaffungsweg').selectOption(mode);
    for (const [key, value] of Object.entries(values)) await page.locator(`#scenario-${key}`).fill(value);
    await page.getByLabel('Ich habe Zusatzkosten und Vergünstigungen für dieses Szenario geprüft.').check();
    await save(() => page.locator('#save-scenario').click()); await page.locator('#scenario-detail').waitFor();
    return new URL(page.url()).pathname.split('/').at(-1);
  }
  async function residual(scenario, month, low, expected, high) {
    await page.goto(`${origin}${path}/${scenario}?month=${month}`);
    await page.locator('#open-residual').click();
    await page.getByLabel('Pessimistisch (EUR)', { exact: true }).fill(low); await page.getByLabel('Erwartet (EUR)', { exact: true }).fill(expected); await page.getByLabel('Optimistisch (EUR)', { exact: true }).fill(high);
    await save(() => page.getByRole('button', { name: 'Restwert-Stichtag speichern' }).click()); await page.locator('#scenario-detail').waitFor();
  }
  function amount(target = page) { return target.locator('[data-effective-cost]').first().getAttribute('data-effective-cost'); }
  const cash = await create('Barkauf', 'Purchase', { PurchasePrice: '1000', InitialFees: '20', AnnualKilometres: '1000' });
  assert.equal(await page.locator('[data-result-complete]').getAttribute('data-result-complete'), 'false');
  await residual(cash, 12, '300', '400', '500'); assert.equal(await amount(), '920');
  await page.goto(`${origin}${path}/${cash}?month=6`); assert.equal(await amount(), ''); await page.getByText('Erwarteter Fahrzeugwert genau für Monat 6.', { exact: true }).waitFor();
  const lease = await create('Leasing', 'LeaseReturn', { PurchasePrice: '1000', InitialFees: '10', DownPayment: '40', MonthlyPayment: '50', TermMonths: '12', AnnualKilometres: '1100', IncludedKilometresPerYear: '1000', ExcessKilometrePrice: '0.2' });
  assert.equal(await amount(), '970.0');
  await page.locator('#open-adjustment').click();
  await page.getByLabel('Kostenposition', { exact: true }).selectOption(cost); await page.getByLabel('Abweichender Betrag je Zahlung (EUR)').fill('0');
  await page.getByLabel('Letzter Monat (einschliesslich)').fill('6'); await page.getByLabel('Grundlage / Vergünstigung').fill('Sechs Monate Parkplatz inklusive');
  await save(() => page.getByRole('button', { name: 'Kostenanpassung speichern' }).click()); await page.locator('#scenario-detail').waitFor();
  assert.equal(Number(await amount()), 820);
  const buyout = await create('Übernahme', 'LeaseBuyout', { PurchasePrice: '1000', InitialFees: '10', DownPayment: '40', MonthlyPayment: '50', TermMonths: '12', FinalPayment: '400' });
  await residual(buyout, 12, '350', '400', '450'); assert.equal(Number(await amount()), 950);
  const credit = await create('Kredit', 'Financing', { PurchasePrice: '1000', InitialFees: '0', LoanAmount: '1000', LoanInterestPercent: '12', MonthlyPayment: '500', TermMonths: '2', FinalPayment: '15.10' });
  await residual(credit, 2, '700', '750', '800'); assert.equal(Number(await amount()), 315.10);
  record('Cash, leasing return, buyout and credit scenarios calculate payments, exact residual checkpoints, mileage and temporary benefits');

  await page.goto(`${origin}${path}/compare?leftId=${cash}&rightId=${lease}&month=12`);
  assert.equal(Number(await page.locator('[data-cost-difference]').getAttribute('data-cost-difference')), 100);
  assert.match(await page.locator('.scenario-threshold').textContent(), /500,00 EUR/);
  await page.screenshot({ path: join(output, '24-scenario-comparison-desktop.png'), fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 }); assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  await page.screenshot({ path: join(output, '25-scenario-comparison-mobile.png'), fullPage: true });
  await page.goto(`${origin}${path}/edit/${cash}`); assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  await page.screenshot({ path: join(output, '26-scenario-edit-mobile.png'), fullPage: true });
  await page.locator('#scenario-PurchasePrice').fill('1000.001'); await page.locator('[data-scenario-form]').evaluate(form => { form.noValidate = true; });
  let response = page.waitForResponse(r => r.request().method() === 'POST' && r.url() === page.url()); await page.locator('#save-scenario').click(); assert.equal((await response).status(), 422);
  assert.equal(await page.locator('#scenario-PurchasePrice').inputValue(), '1000.001');
  await page.setViewportSize({ width: 1440, height: 1050 });
  await page.goto(`${origin}${path}/${cash}`); await page.getByRole('link', { name: 'Jahre', exact: true }).click(); assert.equal(await page.locator('[data-payment-month]').count(), 2);
  v = await version(); assert.equal((await post(page, `${casePath}/offers/edit/${offer}`, { Version: v, 'Input.Name': 'Neue Rechenofferte', 'Input.Kind': 'Purchase', 'Input.Commitment': 'NonBinding', 'Input.BasePrice': '9999' })).status(), 0);
  await page.goto(`${origin}${path}/${cash}`); await page.getByText('Offertversion 2 ist verfügbar.', { exact: false }).waitFor(); assert.equal(Number(await amount()), 920);
  await page.getByRole('link', { name: 'Szenario bearbeiten' }).click(); await page.getByLabel('Verwendete Offertversion').selectOption('2'); await save(() => page.locator('#save-scenario').click()); await page.locator('#scenario-detail').waitFor();
  assert.equal(Number(await amount()), 920); const pinned = await exportCase(); assert.equal(pinned.scenarios.find(s => s.id === cash).input.revisionNumber, 2); assert.equal(pinned.scenarios.find(s => s.id === lease).input.revisionNumber, 1);
  await restart(); await page.goto(`${origin}${path}/${lease}`); assert.equal(Number(await amount()), 820);
  record('Scenario comparison, residual threshold, calendar years, mobile screens, validation and explicit source-version updates survive restart');

  v = await version(); assert.equal((await post(page, casePath, { action: 'share', email: readerEmail, access: 'Reader', version: v })).status(), 200);
  await reader.page.goto(`${origin}${path}/${cash}`); assert.equal(Number(await amount(reader.page)), 920); assert.equal(await reader.page.getByRole('link', { name: 'Szenario bearbeiten' }).count(), 0);
  assert.equal((await browserRequest(reader.page, `${path}/edit/${cash}`)).status(), 403);
  v = await reader.page.locator('#scenario-detail').getAttribute('data-version');
  assert.equal((await post(reader.page, `${path}/${cash}`, { action: 'residual', Version: v, 'Residual.Month': '12', 'Residual.Expected': '1' })).status(), 403);
  assert.equal((await post(reader.page, path, { action: 'settings', Version: v, AnnualReturn: '50' })).status(), 403);
  v = await version(); assert.equal((await post(page, casePath, { action: 'share', email: readerEmail, access: 'Editor', version: v })).status(), 200);
  await page.goto(`${origin}${path}/edit/${cash}`); const stale = await page.locator('[name=Version]').inputValue(); await page.locator('#scenario-PurchasePrice').fill('1111');
  await reader.page.goto(`${origin}${path}/edit/${cash}`); await reader.page.getByLabel('Annahmen und Begründung', { exact: true }).fill('Reviewed by editor'); await save(() => reader.page.locator('#save-scenario').click(), reader.page); await reader.page.locator('#scenario-detail').waitFor();
  response = page.waitForResponse(r => r.request().method() === 'POST' && r.url() === page.url()); await page.locator('#save-scenario').click(); assert.equal((await response).status(), 409); assert.equal(await page.locator('#scenario-PurchasePrice').inputValue(), '1111'); assert.equal(await page.locator('[name=Version]').inputValue(), stale);
  assert.equal((await browserRequest(page, path, { action: 'settings', Version: stale, AnnualReturn: '20' })).status(), 400);
  v = await version(); assert.equal((await post(page, casePath, { action: 'revoke', userId: await page.locator('.share-row input[name=userId]').inputValue(), version: v })).status(), 200);
  assert.equal((await browserRequest(reader.page, `${path}/${cash}`)).status(), 403);
  record('Scenario reads and writes enforce reader/editor access, immediate revocation, CSRF and draft-preserving concurrency');

  await page.goto(`${origin}${path}/${lease}`); await page.locator('#open-scenario-copy').click();
  await page.getByLabel('Name der unabhängigen Kopie').fill('Leasingkopie'); await save(() => page.locator('#save-scenario-copy').click()); await page.waitForURL(url => url.pathname.startsWith(path + '/') && !url.pathname.endsWith(lease));
  const copiedScenario = new URL(page.url()).pathname.split('/').at(-1); assert.equal(Number(await amount()), 820);
  await page.goto(`${origin}${path}/${copiedScenario}?costId=${cost}`); await page.getByLabel('Abweichender Betrag je Zahlung (EUR)').fill('5'); await save(() => page.getByRole('button', { name: 'Kostenanpassung speichern' }).click()); await page.locator('#scenario-detail').waitFor(); assert.equal(Number(await amount()), 850);
  await page.goto(`${origin}${path}/${lease}`); assert.equal(Number(await amount()), 820);
  const json = await exportCase(); assert.equal(json.version, 7); assert.equal(json.scenarios.length, 5);
  await page.goto(origin); await page.getByRole('button', { name: 'Evaluation importieren', exact: true }).click();
  await page.getByLabel('Exportdatei (JSON oder ZIP)').setInputFiles({ name: 'scenarios.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(json)) }); await page.getByRole('button', { name: 'Als neue Evaluation importieren', exact: true }).click(); await page.waitForURL(/\/evaluations\//);
  const importedPath = new URL(page.url()).pathname; const imported = JSON.parse(await (await browserRequest(page, `${importedPath}?handler=Export`)).text()); assert.equal(imported.scenarios.length, 5);
  assert.ok(imported.scenarios.every(s => !json.scenarios.some(original => original.id === s.id)));
  assert.ok(imported.scenarios.every(s => imported.offers.some(o => o.id === s.input.offerId)));
  assert.ok(imported.scenarios.flatMap(s => s.costAdjustments).every(a => imported.costs.some(c => c.id === a.costId)));
  await page.goto(`${origin}${path}`); await page.locator(`[data-scenario-id="${copiedScenario}"]`).getByRole('button', { name: 'Szenario löschen' }).click(); await save(() => page.locator('[data-confirm-accept]').click()); await page.locator('#scenario-workspace').waitFor(); assert.equal(await page.locator('[data-scenario-id]').count(), 4);
  record('Scenario copies, JSON import and deletion preserve independent assumptions, residuals and mapped cost adjustments');

  await page.goto(`${origin}${casePath}/offers/${offer}`); await page.getByRole('link', { name: 'Dokument hinzufügen', exact: true }).click(); await page.getByLabel('Name des Dokuments').fill('Rechengrundlage');
  await page.getByLabel('Datei', { exact: true }).setInputFiles({ name: 'basis.txt', mimeType: 'text/plain', buffer: Buffer.from('Independent calculation attachment') }); await page.getByRole('button', { name: 'Datei hochladen' }).click(); await page.locator('[data-document-id]').waitFor();
  const downloadEvent = page.waitForEvent('download'); await page.getByRole('link', { name: 'Exportieren', exact: true }).click(); const downloaded = await downloadEvent;
  const zip = await readFile(await downloaded.path()); assert.equal(zip.subarray(0, 2).toString(), 'PK');
  await page.goto(origin); await page.getByRole('button', { name: 'Evaluation importieren', exact: true }).click();
  await page.getByLabel('Exportdatei (JSON oder ZIP)').setInputFiles({ name: 'scenarios.zip', mimeType: 'application/zip', buffer: zip }); await page.getByRole('button', { name: 'Als neue Evaluation importieren', exact: true }).click(); await page.waitForURL(/\/evaluations\//);
  const zippedCase = new URL(page.url()).pathname; await page.goto(`${origin}${zippedCase}/scenarios`); assert.equal(await page.locator('[data-scenario-id]').count(), 4);
  await page.getByRole('link', { name: 'Barkauf', exact: true }).click(); assert.equal(Number(await amount()), 920);
  record('ZIP export and import carry scenarios and calculations together with offer documents');
  await restart();
  await checkScenarioWorkflows({ page, reader, readerEmail, origin, casePath, cash, lease, cost, post, record });
  await page.goto(`${origin}${originalPath}`);
}
