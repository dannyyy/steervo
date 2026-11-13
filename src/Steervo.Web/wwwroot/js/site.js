function initializeFields(root = document) {
  root.querySelectorAll('[data-criterion-type]').forEach(select => {
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
document.addEventListener('click', event => {
  const opener = event.target.closest('[data-dialog-open]');
  if (opener) {
    const dialog = document.getElementById(opener.dataset.dialogOpen);
    if (dialog instanceof HTMLDialogElement && !dialog.hasAttribute("data-editor") && !dialog.open) dialog.showModal();
  }
  const closer = event.target.closest('[data-dialog-close]');
  if (closer && !closer.closest('[data-editor]')) closer.closest('dialog')?.close();
});
document.querySelectorAll('dialog[data-open-on-load="true"]:not([data-editor])').forEach(dialog => dialog.showModal());

const drafts = new WeakMap();
document.addEventListener('htmx:beforeRequest', event => {
  const form = event.detail.elt.closest('form');
  if (form && event.detail.xhr) drafts.set(event.detail.xhr, [...new FormData(form)].filter(([key]) => !['__RequestVerificationToken', 'version'].includes(key)));
});
document.addEventListener('htmx:afterSwap', event => {
  const submittedDraft = drafts.get(event.detail.xhr);
  if ([409, 422].includes(event.detail.xhr?.status) && submittedDraft) {
    const draft = new Map(submittedDraft);
    const form = [...document.querySelectorAll('#evaluation-workspace form, #candidate-workspace form, #offer-workspace form')].find(candidate =>
      ['action', 'itemId', 'candidateId', 'offerId', 'criterionId'].every(name => (candidate.elements.namedItem(name)?.value || '') === (draft.get(name) || '')));
    if (form) {
      for (const [name, value] of submittedDraft) {
        const field = form.elements.namedItem(name);
        if (field instanceof HTMLInputElement || field instanceof HTMLSelectElement || field instanceof HTMLTextAreaElement) field.value = value;
      }
      let parent = form.parentElement;
      while (parent) { if (parent instanceof HTMLDetailsElement) parent.open = true; parent = parent.parentElement; }
    }
  }
  drafts.delete(event.detail.xhr);
  initializeFields();
  if (!event.detail.requestConfig?.elt?.matches('[data-sort]') && !event.detail.target?.querySelector('[data-editor]')) document.querySelector('.notice[tabindex]')?.focus({ preventScroll: true });
});
document.addEventListener('htmx:beforeSwap', event => {
  if ([409, 422].includes(event.detail.xhr.status)) { event.detail.shouldSwap = true; event.detail.isError = false; }
});
document.addEventListener('htmx:sendError', () => { document.getElementById('connection-error').hidden = false; });
document.addEventListener('htmx:responseError', () => { document.getElementById('connection-error').hidden = false; });
window.addEventListener('offline', () => { document.getElementById('connection-error').hidden = false; });
window.addEventListener('online', () => { document.getElementById('connection-error').hidden = true; });
document.addEventListener('submit', event => {
  const form = event.target;
  if (form.dataset.confirm && !window.confirm(form.dataset.confirm)) {
    event.preventDefault(); event.stopImmediatePropagation();
  }
}, true);

async function readPasskeyResponse(response, form) {
  if (response.redirected || response.status === 401) throw new Error(form.dataset.sessionError);
  if (response.status === 429) throw new Error(form.dataset.rateError);
  const result = response.headers.get('content-type')?.includes('application/json') ? await response.json() : null;
  if (!response.ok || !result) throw new Error(result?.error || form.dataset.error);
  return result;
}

document.addEventListener('submit', async event => {
  const form = event.target;
  if (!form.dataset.passkey) return;
  event.preventDefault();
  const error = form.querySelector('[data-passkey-error]');
  const button = form.querySelector('button');
  error.textContent = ''; button.disabled = true;
  try {
    if (!window.isSecureContext) throw new Error(form.dataset.secureError);
    if (location.origin !== form.dataset.origin) throw new Error(form.dataset.originError);
    if (!window.PublicKeyCredential || !PublicKeyCredential.parseCreationOptionsFromJSON || !PublicKeyCredential.parseRequestOptionsFromJSON || !PublicKeyCredential.prototype.toJSON) throw new Error(form.dataset.browserError);
    const fields = new FormData(form);
    const optionsResponse = await fetch(form.dataset.options, { method: 'POST', body: fields, credentials: 'same-origin' });
    const options = await readPasskeyResponse(optionsResponse, form);
    const credential = form.dataset.passkey === 'create'
      ? await navigator.credentials.create({ publicKey: PublicKeyCredential.parseCreationOptionsFromJSON(options) })
      : await navigator.credentials.get({ publicKey: PublicKeyCredential.parseRequestOptionsFromJSON(options) });
    if (!credential) throw new Error(form.dataset.cancelledError);
    const credentialJson = credential.toJSON();
    // Some providers omit empty extension results; Identity requires the JSON member.
    const clientExtensionResults = credentialJson.clientExtensionResults ?? credential.getClientExtensionResults?.() ?? {};
    fields.set('credential', JSON.stringify({ ...credentialJson, clientExtensionResults }));
    const response = await fetch(form.action, { method: 'POST', body: fields, credentials: 'same-origin' });
    const result = await readPasskeyResponse(response, form);
    location.assign(result.redirect);
  } catch (failure) {
    const browserErrors = {
      NotAllowedError: form.dataset.cancelledError,
      InvalidStateError: form.dataset.duplicateError,
      SecurityError: form.dataset.securityError,
      NotSupportedError: form.dataset.browserError
    };
    error.textContent = failure.name === 'Error' ? failure.message : browserErrors[failure.name] || form.dataset.error;
  } finally { button.disabled = false; }
});
