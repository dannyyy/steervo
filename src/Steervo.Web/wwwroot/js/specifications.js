(() => {
  const kilowattsPerPs = 0.73549875;
  const format = new Intl.NumberFormat(document.documentElement.lang, { maximumFractionDigits: 2 });
  function update(container) {
    const inputs = Object.fromEntries([...container.querySelectorAll('[data-spec-field]')].map(input => [input.dataset.specField, input]));
    const number = key => inputs[key]?.value === '' ? null : Number.isFinite(inputs[key]?.valueAsNumber) ? inputs[key].valueAsNumber : null;
    const pair = (first, second, operation) => number(first) !== null && number(second) !== null ? operation(number(first), number(second)) : null;
    const calculated = {
      KerbWeightKg: pair('GrossWeightKg', 'PayloadKg', (a, b) => a - b),
      GrossWeightKg: pair('KerbWeightKg', 'PayloadKg', (a, b) => a + b),
      PayloadKg: pair('GrossWeightKg', 'KerbWeightKg', (a, b) => a - b),
      PowerKw: number('PowerPs') === null ? null : number('PowerPs') * kilowattsPerPs,
      PowerPs: number('PowerKw') === null ? null : number('PowerKw') / kilowattsPerPs
    };
    for (const output of container.querySelectorAll('[data-calculated]')) {
      const key = output.dataset.calculated;
      const value = number(key) === null ? calculated[key] : null;
      const valid = value !== null && (key === 'PayloadKg' ? value >= 0 : value > 0);
      output.textContent = valid ? container.dataset.calculatedLabel.replace('{0}', `${format.format(value)} ${inputs[key].dataset.unit}`) : '';
      inputs[key].placeholder = valid ? format.format(value) : '';
    }
  }
  function initialize() { document.querySelectorAll('[data-specifications]').forEach(update); }
  document.addEventListener('input', event => {
    const container = event.target.closest('[data-specifications]');
    if (container) update(container);
  });
  document.addEventListener('invalid', event => {
    if (!event.target.closest('[data-specifications]')) return;
    for (let parent = event.target.parentElement; parent; parent = parent.parentElement)
      if (parent.tagName === 'DETAILS') parent.open = true;
  }, true);
  document.addEventListener('htmx:afterSettle', initialize);
  initialize();
})();
