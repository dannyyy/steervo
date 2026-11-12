document.querySelectorAll('[data-cost-form]').forEach(form => {
  const frequency = form.querySelector('[data-cost-frequency]');
  const amount = form.querySelector('[data-cost-amount]');
  const end = form.querySelector('[data-cost-end]');
  const direction = form.querySelector('[data-cost-direction]');
  function update() {
    const included = frequency.value === 'Included';
    amount.hidden = included;
    amount.querySelector('input').disabled = included;
    direction.disabled = included;
    if (included) direction.value = 'Expense';
    end.hidden = frequency.value === 'Once';
    end.querySelector('input').disabled = end.hidden;
  }
  frequency.addEventListener('change', update);
  update();
});
