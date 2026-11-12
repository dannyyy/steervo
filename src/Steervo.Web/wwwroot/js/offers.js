(() => {
  function updateLine(line) {
    const benefit = line.querySelector('[data-line-kind]').value === 'Benefit';
    const amount = line.querySelector('[data-line-price]').value === 'Amount';
    const saving = line.querySelector('[data-line-effect]').value === 'CostSaving';
    for (const [selector, visible] of [['[data-line-amount]', amount], ['[data-line-benefit]', benefit], ['[data-line-category]', benefit && saving]]) {
      for (const container of line.querySelectorAll(selector)) {
        container.hidden = !visible;
        container.querySelectorAll('input, select').forEach(input => { input.disabled = !visible; });
      }
    }
    line.querySelector('[data-line-amount] input').required = amount;
  }
  function updateCurrency(form) {
    const currency = form.querySelector('#offer-currency').value.trim().toUpperCase();
    form.querySelectorAll('[data-offer-currency-label]').forEach(label => { label.textContent = currency; });
  }
  for (const form of document.querySelectorAll('[data-offer-form]')) {
    form.querySelectorAll('[data-offer-line]').forEach(updateLine);
    updateCurrency(form);
    form.addEventListener('input', event => {
      if (event.target.id === 'offer-currency') updateCurrency(form);
    });
    form.addEventListener('change', event => {
      const line = event.target.closest('[data-offer-line]');
      if (line) updateLine(line);
      if (event.target.matches('[data-offer-candidate]')) {
        const description = form.querySelector('#offer-VehicleDescription');
        if (description.value === '') description.value = event.target.selectedOptions[0]?.dataset.description || '';
      }
    });
    form.addEventListener('click', event => {
      const button = event.target.closest('button');
      if (!button) return;
      if (button.hasAttribute('data-offer-line-add')) {
        const template = form.querySelector('[data-offer-line-template]');
        const container = document.createElement('template');
        container.innerHTML = template.innerHTML.replaceAll('__index__', crypto.randomUUID());
        const line = container.content.firstElementChild;
        form.querySelector('[data-offer-lines]').append(line);
        updateLine(line); updateCurrency(form); line.querySelector('input:not([type=hidden])').focus();
        return;
      }
      const line = button.closest('[data-offer-line]');
      if (!line) return;
      if (button.hasAttribute('data-offer-line-remove')) { line.remove(); form.querySelector('[data-offer-line-add]').focus({ preventScroll: true }); }
      if (button.hasAttribute('data-offer-line-up') && line.previousElementSibling) line.previousElementSibling.before(line);
      if (button.hasAttribute('data-offer-line-down') && line.nextElementSibling) line.nextElementSibling.after(line);
    });
  }
})();
