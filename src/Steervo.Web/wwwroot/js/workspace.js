(() => {
  const requests = new WeakMap();
  document.addEventListener('htmx:beforeRequest', event => {
    const form = event.detail.elt.closest('form');
    const workspace = form?.closest('[data-preserve-viewport]');
    if (workspace) requests.set(event.detail.xhr, { form, workspace });
  });
  document.addEventListener('htmx:beforeSwap', event => {
    const state = requests.get(event.detail.xhr);
    if (!state || !event.detail.shouldSwap) return;
    const { form, workspace } = state;
    const anchor = form.closest('article[id]') || workspace;
    const submitted = form.elements.action?.value === 'clear-assessment' ? form.closest('article') : form;
    state.anchorId = anchor.id;
    state.top = anchor.getBoundingClientRect().top;
    state.x = scrollX; state.y = scrollY;
    state.focusId = document.activeElement?.id;
    state.open = [...workspace.querySelectorAll('details[open][id]')].map(element => element.id);
    state.fields = [...workspace.querySelectorAll('input[id], select[id], textarea[id]')].filter(field => {
      if (submitted?.contains(field)) return false;
      const original = field instanceof HTMLSelectElement
        ? [...field.options].find(option => option.defaultSelected)?.value ?? field.options[0]?.value
        : field.defaultValue;
      return field.value !== original;
    }).map(field => [field.id, field.value]);
    document.documentElement.classList.add('workspace-swapping');
  });
  function restoreViewport(state) {
    const anchor = document.getElementById(state.anchorId);
    window.scrollTo({ left: state.x, top: anchor ? scrollY + anchor.getBoundingClientRect().top - state.top : state.y, behavior: 'instant' });
  }
  document.addEventListener('htmx:afterSwap', event => {
    const state = requests.get(event.detail.xhr);
    if (!state?.open) return;
    if (event.detail.xhr.status !== 409) {
      for (const id of state.open) { const details = document.getElementById(id); if (details) details.open = true; }
      for (const [id, value] of state.fields) { const field = document.getElementById(id); if (field) field.value = value; }
      const focused = document.getElementById(state.focusId);
      if (focused && !focused.disabled) focused.focus({ preventScroll: true });
      else focused?.closest('article')?.querySelector('a')?.focus({ preventScroll: true });
    }
    restoreViewport(state);
  });
  document.addEventListener('htmx:afterSettle', event => {
    const state = requests.get(event.detail.xhr);
    if (!state?.open) return;
    restoreViewport(state);
    requests.delete(event.detail.xhr);
    requestAnimationFrame(() => document.documentElement.classList.remove('workspace-swapping'));
  });
})();
