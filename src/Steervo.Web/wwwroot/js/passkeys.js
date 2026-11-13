async function readPasskeyResponse(response, form) {
  if (response.redirected || response.status === 401) throw Object.assign(new Error(form.dataset.sessionError), { stopRetry: true });
  if (response.status === 429) throw new Error(form.dataset.rateError);
  const result = response.headers.get('content-type')?.includes('application/json') ? await response.json() : null;
  if (!response.ok || !result) throw Object.assign(new Error(result?.error || form.dataset.error), { stopRetry: response.status === 403 });
  return result;
}

document.addEventListener('submit', async event => {
  const form = event.target;
  if (!form.dataset.passkey) return;
  event.preventDefault();
  if (form.hasAttribute('data-pending')) return;
  const error = form.querySelector('[data-passkey-error]');
  const button = form.querySelector('button[type=submit]');
  let blocked = false;
  error.textContent = ''; error.hidden = true; button.disabled = true;
  form.dispatchEvent(new CustomEvent('form:pending', { bubbles: true, detail: true }));
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
    document.dispatchEvent(new Event('form:navigate'));
    const target = new URL(result.redirect, location.href);
    if (target.pathname === location.pathname && target.search === location.search && target.origin === location.origin) {
      history.replaceState(null, '', target.href);
      location.reload();
    } else location.assign(target.href);
  } catch (failure) {
    const browserErrors = {
      NotAllowedError: form.dataset.cancelledError,
      InvalidStateError: form.dataset.duplicateError,
      SecurityError: form.dataset.securityError,
      NotSupportedError: form.dataset.browserError
    };
    blocked = failure.stopRetry === true;
    error.hidden = false;
    error.textContent = failure.name === 'Error' ? failure.message : browserErrors[failure.name] || form.dataset.error;
    error.focus({ preventScroll: true }); error.scrollIntoView({ block: 'nearest' });
  } finally {
    button.disabled = blocked;
    form.dispatchEvent(new CustomEvent('form:pending', { bubbles: true, detail: false }));
  }
});
