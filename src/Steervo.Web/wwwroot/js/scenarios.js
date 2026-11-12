document.querySelectorAll('[data-scenario-form]').forEach(form => {
  const mode = form.querySelector('[data-scenario-mode]');
  function update() {
    form.querySelectorAll('[data-scenario-modes]').forEach(group => {
      group.hidden = group.dataset.scenarioModes !== 'all' && !group.dataset.scenarioModes.split(' ').includes(mode.value);
      group.querySelectorAll('input').forEach(input => { input.disabled = group.hidden; });
    });
  }
  mode.addEventListener('change', update);
  update();
});
