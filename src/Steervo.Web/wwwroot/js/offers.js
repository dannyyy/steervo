(() => {
  const comparison = document.getElementById('offer-comparison-scroll');
  if (comparison?.dataset.focusOffer) {
    const column = [...comparison.querySelectorAll('[data-offer-column]')].find(item => item.dataset.offerColumn === comparison.dataset.focusOffer);
    if (column) requestAnimationFrame(() => { comparison.scrollLeft = Math.max(0, column.offsetLeft - comparison.querySelector('th').offsetWidth); });
  }
  function updateLine(line) {
    const benefit = line.querySelector('[data-line-kind]').value === 'Benefit';
    const amount = line.querySelector('[data-line-price]').value === 'Amount';
    const saving = line.querySelector('[data-line-effect]').value === 'CostSaving';
    const included = line.querySelector('[data-line-price]').value === 'Included';
    const contract = ['Leasing', 'Financing'].includes(line.closest('form').querySelector('#offer-kind').value);
    // The checkbox keeps its state while hidden, so switching the offer kind back restores it.
    line.querySelector('[data-line-financed]').hidden = !(contract && amount);
    for (const [selector, visible] of [['[data-line-amount]', amount], ['[data-line-value]', included], ['[data-line-benefit]', benefit], ['[data-line-category]', benefit && saving]]) {
      for (const container of line.querySelectorAll(selector)) {
        container.hidden = !visible;
        container.querySelectorAll('input, select').forEach(input => { input.disabled = !visible; });
      }
    }
    line.querySelector('[data-line-amount] input').required = amount;
  }
  function updateTotals(form) {
    const totals = form.querySelector('[data-offer-totals]');
    if (!totals) return;
    const kind = form.querySelector('#offer-kind').value;
    const contract = kind === 'Leasing' || kind === 'Financing';
    let before = 0, discounts = 0, benefit = 0, financed = 0, priced = 0;
    for (const line of form.querySelectorAll('[data-offer-line]')) {
      const price = line.querySelector('[data-line-price]').value;
      if (price === 'Included') { benefit += Number(line.querySelector('[data-line-value] input').value) || 0; continue; }
      const amount = Number(line.querySelector('[data-line-amount-input]').value);
      if (price !== 'Amount' || !Number.isFinite(amount) || line.querySelector('[data-line-amount-input]').value === '') continue;
      const discount = line.querySelector('[data-line-kind]').value === 'Discount';
      priced++;
      if (discount) discounts += amount; else before += amount;
      if (line.querySelector('[data-line-financed-input]').checked) financed += discount ? -amount : amount;
    }
    const currency = form.querySelector('#offer-currency').value.trim().toUpperCase();
    const money = value => (value < 0 ? '−' : '') + new Intl.NumberFormat(document.documentElement.lang || 'de', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(Math.abs(value)) + ' ' + currency;
    const total = before - discounts, concession = discounts + benefit;
    const set = (name, text) => { totals.querySelector(`[data-total="${name}"]`).textContent = text; };
    totals.hidden = priced === 0 && concession === 0;
    set('total', priced ? money(total) : '');
    set('financed', money(financed)); set('handover', money(total - financed));
    set('concession', money(concession) + (before > 0 && concession > 0 ? ' · ' + (Math.round(concession * 1000 / before) / 10) + ' %' : ''));
    totals.querySelectorAll('[data-total-contract]').forEach(row => { row.hidden = !contract || !priced; });
    totals.querySelector('[data-total-concession]').hidden = concession <= 0;
    form.querySelectorAll('[data-financed-label]').forEach(label => { label.textContent = kind === 'Financing' ? totals.dataset.labelFinancing : totals.dataset.labelLeasing; });
  }
  function updateCurrency(form) {
    const currency = form.querySelector('#offer-currency').value.trim().toUpperCase();
    form.querySelectorAll('[data-offer-currency-label]').forEach(label => { label.textContent = currency; });
  }
  for (const form of document.querySelectorAll('[data-offer-form]')) {
    form.querySelectorAll('[data-offer-line]').forEach(updateLine);
    updateCurrency(form); updateTotals(form);
    form.addEventListener('input', event => {
      if (event.target.id === 'offer-currency') updateCurrency(form);
      updateTotals(form);
    });
    form.addEventListener('change', event => {
      const line = event.target.closest('[data-offer-line]');
      if (line) updateLine(line);
      if (event.target.id === 'offer-kind') form.querySelectorAll('[data-offer-line]').forEach(updateLine);
      updateTotals(form);
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
        updateLine(line); updateCurrency(form); updateTotals(form); line.querySelector('input:not([type=hidden])').focus();
        return;
      }
      const line = button.closest('[data-offer-line]');
      if (!line) return;
      if (button.hasAttribute('data-offer-line-remove')) { line.remove(); form.querySelector('[data-offer-line-add]').focus({ preventScroll: true }); }
      if (button.hasAttribute('data-offer-line-up') && line.previousElementSibling) { line.previousElementSibling.before(line); button.focus({ preventScroll: true }); }
      if (button.hasAttribute('data-offer-line-down') && line.nextElementSibling) { line.nextElementSibling.after(line); button.focus({ preventScroll: true }); }
      updateTotals(form);
    });
  }
})();
