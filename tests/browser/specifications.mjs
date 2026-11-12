import assert from 'node:assert/strict';

export async function fillSpecifications(dialog) {
  for (const summary of await dialog.locator('.specification-section > summary').all()) await summary.click();
  await dialog.locator('#spec-Propulsion').selectOption('Electric');
  await dialog.locator('#spec-Drive').selectOption('All');
  const values = {
    LengthMm: '4700', WidthMm: '1900', WidthWithMirrorsMm: '2100', HeightMm: '1600', WheelbaseMm: '2850',
    Seats: '5', BootVolumeLitres: '500', BootVolumeFoldedLitres: '1600', FrunkVolumeLitres: '0',
    KerbWeightKg: '2000', GrossWeightKg: '2500', PowerPs: '400', AccelerationSeconds: '4.5', TopSpeedKmh: '220',
    SummerConsumptionKwhPer100Km: '16.5', WinterConsumptionKwhPer100Km: '22.4', BatteryUsableKwh: '75', BatteryGrossKwh: '80',
    AcChargingPowerKw: '11', DcChargingPowerKw: '250', Charging20To80Minutes: '22.5'
  };
  for (const [name, value] of Object.entries(values)) await dialog.locator(`#spec-${name}`).fill(value);
  assert.match(await dialog.locator('#calculated-PayloadKg').textContent(), /500 kg/);
  assert.match(await dialog.locator('#calculated-PowerKw').textContent(), /294,2 kW/);
  assert.equal(await dialog.locator('#spec-PayloadKg').inputValue(), '');
  assert.equal(await dialog.locator('#spec-PowerKw').inputValue(), '');
}

export async function checkSpecificationValidation(page, details) {
  await details.locator('#spec-section-Weight > summary').click();
  await details.locator('#spec-GrossWeightKg').fill('1900');
  const response = page.waitForResponse(response => response.request().method() === 'POST' && response.url() === page.url());
  await details.getByRole('button', { name: 'Stammdaten speichern' }).click();
  assert.equal((await response).status(), 422);
  await page.getByText(/Die Gewichte passen nicht zusammen/).waitFor();
  assert.equal(await details.locator('#spec-GrossWeightKg').inputValue(), '1900');
  assert.equal(await details.locator('#spec-AccelerationSeconds').inputValue(), '4.5');
  await details.locator('#spec-GrossWeightKg').fill('2500');
  await details.locator('#spec-KerbWeightKg').fill('2100');
  assert.match(await details.locator('#calculated-PayloadKg').textContent(), /400 kg/);
}

export async function editCopiedSpecifications(page, change) {
  const details = page.locator('#candidate-edit');
  await details.locator(':scope > summary').click();
  for (const group of ['Weight', 'Performance', 'Space', 'Electric', 'Combustion'])
    await details.locator(`#spec-section-${group} > summary`).click();
  await details.locator('#spec-KerbWeightKg').fill('2200');
  await details.locator('#spec-Propulsion').selectOption('Hybrid');
  await details.locator('#spec-Drive').selectOption('Rear');
  await details.locator('#spec-FrunkVolumeLitres').fill('');
  await details.locator('#spec-SummerConsumptionKwhPer100Km').fill('');
  await details.locator('#spec-TankCapacityLitres').fill('50');
  await details.locator('#spec-RangeKm').fill('800');
  await details.locator('#spec-FuelConsumptionLitresPer100Km').fill('6.25');
  await change(page, () => details.getByRole('button', { name: 'Stammdaten speichern' }).click());
  assert.match(await page.locator('#candidate-specifications [data-specification="PayloadKg"]').textContent(), /300 kg/);
  await details.locator(':scope > summary').click();
}

export async function checkSpecificationsComparison(page, firstId, secondId) {
  const cells = name => page.locator(`.specification-comparison [data-specification="${name}"]`);
  assert.match(await cells('PayloadKg').locator(`[data-specification-candidate="${firstId}"]`).textContent(), /400/);
  assert.match(await cells('PayloadKg').locator(`[data-specification-candidate="${secondId}"]`).textContent(), /300/);
  assert.equal((await cells('FrunkVolumeLitres').locator(`[data-specification-candidate="${firstId}"]`).textContent()).trim(), '0');
  assert.match(await cells('FrunkVolumeLitres').locator(`[data-specification-candidate="${secondId}"]`).textContent(), /Nicht angegeben/);
  assert.match(await cells('TankCapacityLitres').locator(`[data-specification-candidate="${firstId}"]`).textContent(), /Nicht angegeben/);
  assert.equal((await cells('TankCapacityLitres').locator(`[data-specification-candidate="${secondId}"]`).textContent()).trim(), '50');
  assert.equal(await page.locator('.specification-comparison .fulfillment, .specification-comparison .priority').count(), 0);
}
