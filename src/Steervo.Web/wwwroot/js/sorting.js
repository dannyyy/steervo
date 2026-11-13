(() => {
  if (!window.htmx || !window.PointerEvent) return;
  document.documentElement.classList.add('sorting-enabled');
  const requests = new WeakMap();
  let drag;

  // Replacing the workspace can make the browser anchor the viewport to the footer.
  // Preserve the list's viewport position and the user's open editors across sorting.
  document.addEventListener('htmx:beforeRequest', event => {
    const form = event.detail.elt;
    if (!form.matches('form[data-sort]')) return;
    requests.set(event.detail.xhr, { focus: form.dataset.focusId || document.activeElement?.id });
  });
  document.addEventListener('htmx:beforeSwap', event => {
    const state = requests.get(event.detail.xhr);
    if (!state || !event.detail.shouldSwap) return;
    const workspace = document.getElementById('evaluation-workspace');
    state.x = scrollX;
    state.top = workspace.querySelector('.criteria-main').getBoundingClientRect().top;
    state.open = [...workspace.querySelectorAll('details[open][id]')].map(details => details.id);
    state.fields = [...workspace.querySelectorAll('input[id], select[id], textarea[id]')].filter(field => {
      if (field.closest('form[data-guard-draft]')) return false;
      const original = field instanceof HTMLSelectElement
        ? [...field.options].find(option => option.defaultSelected)?.value ?? field.options[0]?.value
        : field.defaultValue;
      return field.value !== original;
    }).map(field => [field.id, field.value]);
    document.documentElement.classList.add('workspace-swapping');
  });
  function restoreViewport(state) {
    const main = document.querySelector('#evaluation-workspace .criteria-main');
    if (main) window.scrollTo({ left: state.x, top: scrollY + main.getBoundingClientRect().top - state.top, behavior: 'instant' });
  }
  document.addEventListener('htmx:afterSwap', event => {
    const state = requests.get(event.detail.xhr);
    if (!state?.open) return;
    // A stale response must show the current server state, not overwrite it with old fields.
    if (event.detail.xhr.status < 400) {
      for (const id of state.open) { const details = document.getElementById(id); if (details) details.open = true; }
      for (const [id, value] of state.fields) { const field = document.getElementById(id); if (field) field.value = value; }
      document.querySelectorAll('[data-criterion-type]').forEach(field => field.dispatchEvent(new Event('change')));
    }
    const control = document.getElementById(state.focus);
    if (control && !control.disabled) control.focus({ preventScroll: true });
    else control?.closest('[data-sort-kind]')?.querySelector('[data-drag-handle]')?.focus({ preventScroll: true });
    restoreViewport(state);
  });
  document.addEventListener('htmx:afterSettle', event => {
    const state = requests.get(event.detail.xhr);
    if (!state?.open) return;
    restoreViewport(state);
    requests.delete(event.detail.xhr);
    requestAnimationFrame(() => document.documentElement.classList.remove('workspace-swapping'));
  });

  function busy() { return !!document.querySelector('#evaluation-workspace.htmx-settling, #evaluation-workspace .htmx-request'); }
  function finish(cancelled = false) {
    if (!drag) return;
    const current = drag;
    drag = null;
    cancelAnimationFrame(current.frame);
    current.marker?.remove();
    current.item.classList.remove('sorting-source');
    document.documentElement.classList.remove('sorting-active');
    if (current.handle.hasPointerCapture(current.pointerId)) current.handle.releasePointerCapture(current.pointerId);
    if (cancelled || !current.active || !current.destination) return;
    const { groupId, position } = current.destination;
    if (groupId === current.groupId && position === current.position) return;
    const form = document.getElementById('reorder-form');
    form.elements.action.value = `place-${current.kind}`;
    form.elements.itemId.value = current.item.dataset.criterionId || current.item.dataset.groupId;
    form.elements.groupId.value = groupId;
    form.elements.position.value = position;
    form.dataset.focusId = current.handle.id;
    form.requestSubmit();
  }

  function locateDestination() {
    const { item, kind, x, y, marker } = drag;
    const hit = document.elementFromPoint(x, y);
    let container;
    let siblings;
    if (kind === 'criterion') {
      const group = hit?.closest('.criterion-group');
      container = group?.querySelector('.criterion-list');
      siblings = container ? [...container.children].filter(child => child.matches('.criterion') && child !== item) : [];
    } else {
      container = hit?.closest('.criteria-main');
      siblings = container ? [...container.querySelectorAll(':scope > [data-sort-kind="group"]')].filter(child => child !== item) : [];
    }
    marker.hidden = !container;
    drag.destination = null;
    if (!container) return;
    let position = siblings.findIndex(sibling => {
      const bounds = sibling.getBoundingClientRect();
      return y < bounds.top + bounds.height / 2;
    });
    if (position === -1) position = siblings.length;
    const next = siblings[position];
    const previous = siblings[position - 1];
    const bounds = container.getBoundingClientRect();
    const top = next ? next.getBoundingClientRect().top : previous ? previous.getBoundingClientRect().bottom : bounds.top + bounds.height / 2;
    Object.assign(marker.style, { left: `${bounds.left}px`, width: `${bounds.width}px`, top: `${top - 1}px` });
    drag.destination = { groupId: kind === 'criterion' ? container.closest('.criterion-group').dataset.groupId : '', position };
  }

  function autoScroll(time) {
    if (!drag?.active) return;
    const elapsed = Math.min(time - (drag.time || time), 32);
    drag.time = time;
    const edge = 64;
    const speed = drag.y < edge ? -Math.min(1, (edge - drag.y) / edge) : drag.y > innerHeight - edge ? Math.min(1, (drag.y - innerHeight + edge) / edge) : 0;
    if (speed) window.scrollBy({ top: speed * elapsed * .8, behavior: 'instant' });
    locateDestination();
    drag.frame = requestAnimationFrame(autoScroll);
  }

  document.addEventListener('pointerdown', event => {
    const handle = event.target.closest('[data-drag-handle]');
    if (!handle || event.button !== 0 || !event.isPrimary || busy() || drag) return;
    const item = handle.closest('[data-sort-kind]');
    const kind = item.dataset.sortKind;
    const siblings = kind === 'criterion' ? [...item.parentElement.querySelectorAll(':scope > .criterion')] : [...item.parentElement.querySelectorAll(':scope > [data-sort-kind="group"]')];
    drag = { handle, item, kind, pointerId: event.pointerId, startX: event.clientX, startY: event.clientY, x: event.clientX, y: event.clientY,
      groupId: kind === 'criterion' ? item.closest('.criterion-group').dataset.groupId : '', position: siblings.indexOf(item) };
    handle.focus({ preventScroll: true });
    handle.setPointerCapture(event.pointerId);
    event.preventDefault();
  });
  document.addEventListener('pointermove', event => {
    if (!drag || event.pointerId !== drag.pointerId) return;
    drag.x = event.clientX;
    drag.y = event.clientY;
    if (!drag.active && Math.hypot(drag.x - drag.startX, drag.y - drag.startY) >= 6) {
      drag.active = true;
      drag.marker = document.createElement('div');
      drag.marker.className = 'sorting-marker';
      drag.marker.setAttribute('aria-hidden', 'true');
      document.body.append(drag.marker);
      drag.item.classList.add('sorting-source');
      document.documentElement.classList.add('sorting-active');
      drag.frame = requestAnimationFrame(autoScroll);
    }
    if (drag.active) { event.preventDefault(); locateDestination(); }
  });
  document.addEventListener('pointerup', event => { if (drag && event.pointerId === drag.pointerId) finish(); });
  document.addEventListener('pointercancel', event => { if (drag && event.pointerId === drag.pointerId) finish(true); });
  document.addEventListener('lostpointercapture', event => { if (drag && event.pointerId === drag.pointerId) finish(true); });
  window.addEventListener('blur', () => finish(true));
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && drag) { event.preventDefault(); finish(true); return; }
    const handle = event.target.closest('[data-drag-handle]');
    if (!handle || !['ArrowUp', 'ArrowDown'].includes(event.key)) return;
    event.preventDefault();
    if (busy() || drag) return;
    const item = handle.closest('[data-sort-kind]');
    const direction = event.key === 'ArrowUp' ? '-1' : '1';
    const form = [...item.querySelectorAll('form[data-sort]')].find(candidate =>
      candidate.elements.action.value === `move-${item.dataset.sortKind}` && candidate.elements.direction.value === direction);
    if (form && !form.querySelector('button').disabled) { form.dataset.focusId = handle.id; form.requestSubmit(); }
  });
})();
