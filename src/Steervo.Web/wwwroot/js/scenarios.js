document.querySelectorAll('[data-scenario-form]').forEach(form => {
  const mode = form.querySelector('[data-scenario-mode]');
  function update() {
    form.querySelectorAll('[data-scenario-modes]').forEach(group => {
      group.hidden = group.dataset.scenarioModes !== 'all' && !group.dataset.scenarioModes.split(' ').includes(mode.value);
      group.querySelectorAll('input').forEach(input => { input.disabled = group.hidden; });
    });
    form.querySelectorAll('[data-scenario-group]').forEach(section => {
      section.hidden = [...section.querySelectorAll('[data-scenario-modes]')].every(group => group.hidden);
    });
  }
  mode.addEventListener('change', update);
  update();
});

const revision = document.querySelector('[data-scenario-revision]');
const source = document.querySelector('[data-scenario-source]');
revision?.addEventListener('change', () => {
  const url = new URL(source.href); url.searchParams.set('revision', revision.value); source.href = url.href;
});
const comparison = document.getElementById('scenario-comparison-scroll');
if (comparison?.dataset.focusScenario) {
  const column = [...comparison.querySelectorAll('[data-scenario-column]')].find(cell => cell.dataset.scenarioColumn === comparison.dataset.focusScenario);
  if (column) requestAnimationFrame(() => { comparison.scrollLeft = Math.max(0, column.offsetLeft - comparison.querySelector('th').offsetWidth); });
}
