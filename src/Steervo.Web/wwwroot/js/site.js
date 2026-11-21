function initializeFields(root = document) {
  root.querySelectorAll('[data-criterion-type]').forEach(select => {
    if (select.dataset.initialized) return;
    select.dataset.initialized = 'true';
    const fields = select.closest('form').querySelector('[data-measurement-fields]');
    const update = () => {
      const measurement = select.value === 'Measurement';
      fields.hidden = !measurement;
      fields.querySelectorAll('input').forEach(input => { input.required = measurement; input.disabled = !measurement; });
    };
    select.addEventListener('change', update);
    update();
  });
}
initializeFields();
document.addEventListener('htmx:afterSwap', event => {
  initializeFields();
  if (!event.detail.requestConfig?.elt?.matches('[data-sort]') && !event.detail.target?.querySelector('[data-editor]'))
    document.querySelector('.notice[tabindex]')?.focus({ preventScroll: true });
});
document.addEventListener('htmx:beforeSwap', event => {
  if ([409, 422].includes(event.detail.xhr.status)) { event.detail.shouldSwap = true; event.detail.isError = false; }
});
const connection = document.getElementById('connection-error');
function showConnection(offline = false) {
  connection.querySelector('[data-connection-message]').textContent = offline ? connection.dataset.offline : connection.dataset.failure;
  connection.hidden = false;
}
function requestFailed(event) {
  if (!event.detail.elt?.closest('[data-editor], [data-request-form], [data-operation]')) showConnection(!navigator.onLine);
}
document.addEventListener('htmx:sendError', requestFailed);
document.addEventListener('htmx:responseError', requestFailed);
window.addEventListener('offline', () => showConnection(true));
window.addEventListener('online', () => { connection.hidden = true; });
connection.querySelector('[data-dismiss-connection]').addEventListener('click', () => { connection.hidden = true; });
function requestPending(form, pending) {
  if (!form) return;
  form.toggleAttribute('data-pending', pending);
  form.setAttribute('aria-busy', String(pending));
  if (form.hasAttribute('data-sort')) return;
  let status = form.querySelector('.request-status');
  if (!status && pending) {
    status = document.createElement('p'); status.className = 'request-status'; status.setAttribute('role', 'status'); form.append(status);
  }
  if (status) { status.textContent = pending ? document.body.dataset.requestPending : ''; status.hidden = !pending; }
}
document.addEventListener('htmx:beforeRequest', event => requestPending(event.detail.elt.closest('form'), true));
document.addEventListener('htmx:afterRequest', event => requestPending(event.detail.elt.closest('form'), false));
document.addEventListener('form:pending', event => requestPending(event.target.closest('form'), event.detail));
// Up to five cars can be marked in the overview and opened side by side.
const comparedCars = new Set();
function updateCompareSelection(restore = false) {
  const form = document.querySelector('[data-compare-form]');
  if (!form) return;
  const boxes = [...document.querySelectorAll('[data-compare-select]')];
  if (restore) boxes.forEach(box => { box.checked = comparedCars.has(box.value); });
  comparedCars.clear();
  boxes.filter(box => box.checked).forEach(box => comparedCars.add(box.value));
  const maximum = Number(form.dataset.compareMaximum);
  const full = comparedCars.size >= maximum;
  boxes.forEach(box => { box.disabled = full && !box.checked; });
  const button = form.querySelector('[data-compare-label]');
  button.querySelector('span').textContent = `${button.dataset.compareLabel} (${comparedCars.size}/${maximum})`;
}
document.addEventListener('change', event => { if (event.target.matches('[data-compare-select]')) updateCompareSelection(); });
document.addEventListener('htmx:afterSwap', () => updateCompareSelection(true));
window.addEventListener('pageshow', () => updateCompareSelection());
updateCompareSelection();
