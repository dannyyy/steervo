// Passkey forms (data-passkey="create" or "get"): fetch the WebAuthn options from the server,
// let the browser talk to the authenticator and post the resulting credential back.

// --- Server responses ---

// Reads the JSON result of a passkey request. Failures become errors that carry the message to
// show; stopRetry marks failures that submitting the same form again cannot resolve.
async function readPasskeyResponse(response, form) {
  // A redirect means the server sent the browser to the sign-in page.
  if (response.redirected || response.status === 401) {
    throw Object.assign(new Error(form.dataset.sessionError), { stopRetry: true });
  }

  if (response.status === 429) {
    throw new Error(form.dataset.rateError);
  }

  const isJson = response.headers.get("content-type")?.includes("application/json");
  const result = isJson ? await response.json() : null;
  if (!response.ok || !result) {
    throw Object.assign(new Error(result?.error || form.dataset.error), {
      stopRetry: response.status === 403,
    });
  }

  return result;
}

function postPasskeyFields(url, fields) {
  return fetch(url, {
    method: "POST",
    body: fields,
    credentials: "same-origin",
  });
}

// --- Browser and authenticator ---

function assertPasskeysAvailable(form) {
  if (!window.isSecureContext) {
    throw new Error(form.dataset.secureError);
  }

  if (location.origin !== form.dataset.origin) {
    throw new Error(form.dataset.originError);
  }

  const supportsJsonCredentials =
    window.PublicKeyCredential &&
    PublicKeyCredential.parseCreationOptionsFromJSON &&
    PublicKeyCredential.parseRequestOptionsFromJSON &&
    PublicKeyCredential.prototype.toJSON;
  if (!supportsJsonCredentials) {
    throw new Error(form.dataset.browserError);
  }
}

function requestPasskeyCredential(ceremony, options) {
  if (ceremony === "create") {
    return navigator.credentials.create({
      publicKey: PublicKeyCredential.parseCreationOptionsFromJSON(options),
    });
  }

  return navigator.credentials.get({
    publicKey: PublicKeyCredential.parseRequestOptionsFromJSON(options),
  });
}

function serializePasskeyCredential(credential) {
  const credentialJson = credential.toJSON();

  // Some providers omit empty extension results; Identity requires the JSON member.
  const clientExtensionResults =
    credentialJson.clientExtensionResults ?? credential.getClientExtensionResults?.() ?? {};

  return JSON.stringify({ ...credentialJson, clientExtensionResults });
}

// --- After the server accepted the credential ---

function followPasskeyRedirect(redirect) {
  const target = new URL(redirect, location.href);
  const isSamePage =
    target.pathname === location.pathname &&
    target.search === location.search &&
    target.origin === location.origin;

  // Assigning a URL that differs only in its fragment would not load the page again.
  if (isSamePage) {
    history.replaceState(null, "", target.href);
    location.reload();
  } else {
    location.assign(target.href);
  }
}

// --- Failures ---

function passkeyFailureMessage(form, failure) {
  // Plain errors are raised by this script and already carry the text to show.
  if (failure.name === "Error") {
    return failure.message;
  }

  const browserErrors = {
    NotAllowedError: form.dataset.cancelledError,
    InvalidStateError: form.dataset.duplicateError,
    SecurityError: form.dataset.securityError,
    NotSupportedError: form.dataset.browserError,
  };
  return browserErrors[failure.name] || form.dataset.error;
}

function showPasskeyFailure(form, errorMessage, failure) {
  errorMessage.hidden = false;
  errorMessage.textContent = passkeyFailureMessage(form, failure);
  errorMessage.focus({ preventScroll: true });
  errorMessage.scrollIntoView({ block: "nearest" });
}

// --- Form submission ---

document.addEventListener("submit", async (event) => {
  const form = event.target;
  if (!form.dataset.passkey) {
    return;
  }

  event.preventDefault();
  if (form.hasAttribute("data-pending")) {
    return;
  }

  const errorMessage = form.querySelector("[data-passkey-error]");
  const submitButton = form.querySelector("button[type=submit]");
  let retryBlocked = false;

  errorMessage.textContent = "";
  errorMessage.hidden = true;
  submitButton.disabled = true;
  form.dispatchEvent(new CustomEvent("form:pending", { bubbles: true, detail: true }));

  try {
    assertPasskeysAvailable(form);

    const fields = new FormData(form);
    const optionsResponse = await postPasskeyFields(form.dataset.options, fields);
    const options = await readPasskeyResponse(optionsResponse, form);

    const credential = await requestPasskeyCredential(form.dataset.passkey, options);
    if (!credential) {
      throw new Error(form.dataset.cancelledError);
    }

    fields.set("credential", serializePasskeyCredential(credential));
    const response = await postPasskeyFields(form.action, fields);
    const result = await readPasskeyResponse(response, form);

    document.dispatchEvent(new Event("form:navigate"));
    followPasskeyRedirect(result.redirect);
  } catch (failure) {
    retryBlocked = failure.stopRetry === true;
    showPasskeyFailure(form, errorMessage, failure);
  } finally {
    submitButton.disabled = retryBlocked;
    form.dispatchEvent(new CustomEvent("form:pending", { bubbles: true, detail: false }));
  }
});
