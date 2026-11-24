// Editors are the places where a record is changed: inline panels, side sheets (dialogs) and
// whole pages, all marked with data-editor. This script opens and closes them, guards unsaved
// drafts (form[data-guard-draft]) against being lost, asks for confirmation before destructive
// actions, and keeps drafts, focus and error messages intact across HTMX requests.
(() => {
  // --- Constants ---

  const STATUS_CONFLICT = 409;

  // The server rejects a submitted form with these statuses (conflict, validation).
  const REJECTED_STATUSES = [409, 422];

  // Request fields that belong to the request itself, not to the user's draft.
  const TOKEN_FIELD = "__RequestVerificationToken";
  const VERSION_FIELD = "version";

  const ASSESSMENTS_TAB = "record-assessments";

  // How long a success notice stays visible, in milliseconds.
  const NOTICE_LIFETIME = 3200;

  // An opener closer than this (in pixels) to the window edge counts as out of view.
  const VIEWPORT_MARGIN = 12;

  // Largest distance (in pixels) kept above an opener that is scrolled back into view.
  const LARGEST_REVEAL_OFFSET = 180;

  // --- State ---

  // The signature of each guarded form as it was last loaded or saved.
  const initialSignatures = new WeakMap();

  // Forms that count as changed regardless of their signature, for example after a failed save.
  const formsForcedDirty = new WeakSet();

  // What has to survive each running HTMX request, keyed by its XMLHttpRequest.
  const requestStates = new WeakMap();

  // Editors that are waiting for the response to their own request.
  const busyEditors = new WeakSet();

  // The shared confirmation dialog and the resolver of the question it currently asks.
  const confirmation = document.getElementById("action-confirmation");
  let resolvePendingConfirmation;

  // Set once a navigation is intended, so the unsaved-changes warning stays quiet.
  let isLeavingPage = false;

  let noticeTimer;

  // --- Drafts: form values and change detection ---

  // The forms spell the version field both as "version" and as "Version".
  function isRequestField(name) {
    return name === TOKEN_FIELD || name.toLowerCase() === VERSION_FIELD;
  }

  function draftValues(form) {
    return [...new FormData(form)].filter(([name]) => !isRequestField(name));
  }

  function restoreField(field, value) {
    if (!field || !("value" in field)) {
      return;
    }

    // Browsers do not allow scripts to fill in a file selection.
    if (field.type === "file") {
      return;
    }

    const restoredValue = value ?? "";

    // The newly rendered select may no longer offer the value the user had chosen.
    const isMissingOption =
      field instanceof HTMLSelectElement &&
      restoredValue !== "" &&
      ![...field.options].some((option) => option.value === restoredValue);
    if (isMissingOption) {
      field.add(new Option(restoredValue, restoredValue));
    }

    field.value = restoredValue;
  }

  function restoreDraft(form, values) {
    for (const [name, value] of values) {
      restoreField(form.elements.namedItem(name), value);
    }
  }

  function fileSignature(file) {
    return file.name ? [file.name, file.size, file.lastModified] : null;
  }

  function formSignature(form) {
    return JSON.stringify(
      draftValues(form).map(([name, value]) => [
        name,
        value instanceof File ? fileSignature(value) : value,
      ]),
    );
  }

  function guardedForms(root) {
    return [...root.querySelectorAll("form[data-guard-draft]")];
  }

  // A form without a recorded signature has nothing to compare against and counts as unchanged.
  function hasUnsavedChanges(form) {
    const hasChanged =
      initialSignatures.has(form) && initialSignatures.get(form) !== formSignature(form);
    return formsForcedDirty.has(form) || hasChanged;
  }

  function isDirty(root) {
    return guardedForms(root).some(hasUnsavedChanges);
  }

  function markUnchanged(form) {
    formsForcedDirty.delete(form);
    initialSignatures.set(form, formSignature(form));
  }

  // Fields that depend on the criterion type follow its change event.
  function refreshDependentFields(form) {
    form
      .querySelectorAll("[data-criterion-type]")
      .forEach((typeSelect) => typeSelect.dispatchEvent(new Event("change")));
  }

  // --- Editors: lookup ---

  function allEditors() {
    return [...document.querySelectorAll("[data-editor]")];
  }

  function isDialog(editor) {
    return editor instanceof HTMLDialogElement;
  }

  function activeEditors() {
    return allEditors().filter((editor) => (isDialog(editor) ? editor.open : !editor.hidden));
  }

  // Editors that are open or still hold an unsaved draft.
  function pendingEditors() {
    return allEditors().filter((editor) => activeEditors().includes(editor) || isDirty(editor));
  }

  function currentOpener(editor) {
    return document.getElementById(editor.dataset.opener);
  }

  // --- Confirmation dialog ---

  // Asks the question and resolves to true when the user accepts. A second question while one
  // is open is declined right away.
  function confirmAction(title, description, accept, cancel) {
    if (resolvePendingConfirmation) {
      return Promise.resolve(false);
    }

    confirmation.querySelector("#confirmation-title").textContent = title;
    confirmation.querySelector("#confirmation-description").textContent = description;
    confirmation.querySelector("[data-confirm-accept]").textContent = accept;
    confirmation.querySelector("[data-confirm-cancel]").textContent = cancel;
    confirmation.showModal();
    confirmation.querySelector("[data-confirm-cancel]").focus();

    return new Promise((resolve) => {
      resolvePendingConfirmation = resolve;
    });
  }

  function resolveConfirmation(accepted) {
    const resolve = resolvePendingConfirmation;
    resolvePendingConfirmation = null;
    confirmation.close();
    resolve?.(accepted);
  }

  function confirmDiscard() {
    return confirmAction(
      confirmation.dataset.discardTitle,
      confirmation.dataset.discardDescription,
      confirmation.dataset.discardAction,
      confirmation.dataset.continueAction,
    );
  }

  confirmation.addEventListener("cancel", (event) => {
    event.preventDefault();
    resolveConfirmation(false);
  });
  confirmation
    .querySelector("[data-confirm-cancel]")
    .addEventListener("click", () => resolveConfirmation(false));
  confirmation
    .querySelector("[data-confirm-accept]")
    .addEventListener("click", () => resolveConfirmation(true));

  // --- Editors: opening and closing ---

  // Returns the focus to the control that opened an editor. With reveal, the page scrolls
  // when that control is not comfortably inside the window.
  function restoreFocus(opener, reveal = false) {
    if (!opener) {
      return;
    }

    opener.focus({ preventScroll: true });
    if (!reveal) {
      return;
    }

    const bounds = opener.getBoundingClientRect();
    const isOutOfView =
      bounds.top < VIEWPORT_MARGIN || bounds.bottom > innerHeight - VIEWPORT_MARGIN;
    if (isOutOfView) {
      window.scrollTo({
        top: scrollY + bounds.top - Math.min(LARGEST_REVEAL_OFFSET, innerHeight / 3),
        behavior: "instant",
      });
    }
  }

  function hideEditor(editor) {
    if (isDialog(editor)) {
      editor.close();
    } else {
      editor.hidden = true;
    }
  }

  // The form for a new object starts out empty rather than with its rendered default values.
  function clearNewObjectFields(form) {
    for (const field of form.elements) {
      if (!field.name?.startsWith("Input.")) {
        continue;
      }

      field.value = field.name === "Input.Condition" ? "Unknown" : "";
    }
  }

  function discardDraft(form) {
    form.reset();
    if (form.hasAttribute("data-new-object")) {
      clearNewObjectFields(form);
    }

    formsForcedDirty.delete(form);
    refreshDependentFields(form);
    initialSignatures.set(form, formSignature(form));
  }

  function closeEditor(editor, returnFocus = true) {
    // A page editor is left by navigating away; its values only stop counting as unsaved.
    if (editor.hasAttribute("data-page-editor")) {
      guardedForms(editor).forEach(markUnchanged);
      return;
    }

    guardedForms(editor).forEach(discardDraft);

    const error = editor.querySelector(".editor-error");
    if (error) {
      error.hidden = true;
    }

    hideEditor(editor);

    const opener = currentOpener(editor);
    opener?.setAttribute("aria-expanded", "false");
    if (returnFocus) {
      restoreFocus(opener, true);
    }
  }

  // Editors can be closed when none of them is waiting for a response and the user agrees to
  // discard any unsaved changes.
  async function canClose(editors) {
    const isWaiting = editors.some(
      (editor) => busyEditors.has(editor) || editor.querySelector("form[data-pending]"),
    );
    if (isWaiting) {
      return false;
    }

    if (!editors.some(isDirty)) {
      return true;
    }

    return confirmDiscard();
  }

  async function requestClose(editor) {
    if (editor && (await canClose([editor]))) {
      closeEditor(editor);
      if (editor.hasAttribute("data-page-editor")) {
        isLeavingPage = true;
        location.assign(editor.dataset.returnUrl);
      }
    }
  }

  function findFocusField(editor, focusId) {
    return (
      (focusId && document.getElementById(focusId)) ||
      editor.querySelector("[data-editor-focus]") ||
      editor.querySelector("input:not([type=hidden]):not([readonly]), select, textarea")
    );
  }

  // A field inside a collapsed section of the editor cannot receive the focus.
  function openEnclosingDetails(field, editor) {
    for (
      let parent = field?.parentElement;
      parent && parent !== editor;
      parent = parent.parentElement
    ) {
      if (parent instanceof HTMLDetailsElement) {
        parent.open = true;
      }
    }
  }

  function showEditor(editor, opener, focusId, moveFocus = true) {
    if (opener?.id) {
      editor.dataset.opener = opener.id;
    }

    if (isDialog(editor)) {
      if (!editor.open) {
        editor.showModal();
      }
    } else {
      editor.hidden = false;
    }

    currentOpener(editor)?.setAttribute("aria-expanded", "true");
    if (!moveFocus) {
      return;
    }

    const field = findFocusField(editor, focusId);
    openEnclosingDetails(field, editor);
    field?.focus({ preventScroll: true });

    // An inline editor is brought into view as a whole; a dialog only scrolls to a field that
    // was asked for explicitly.
    if (!isDialog(editor)) {
      editor.scrollIntoView({ block: "nearest", inline: "nearest" });
    } else if (focusId) {
      field?.scrollIntoView({ block: "center" });
    }
  }

  // Only one editor is in use at a time: the others are closed first, with the user's consent.
  async function openEditor(editor, opener) {
    if (!editor) {
      return;
    }

    const otherEditors = pendingEditors().filter((other) => other !== editor);
    if (!(await canClose(otherEditors))) {
      return;
    }

    otherEditors.forEach((other) => closeEditor(other, false));
    showEditor(editor, opener, opener?.dataset.focusId);
  }

  // --- Record tabs ---

  function selectTab(tabId, updateUrl = false) {
    document.querySelectorAll("[data-record-tab]").forEach((tab) => {
      const isSelected = tab.dataset.recordTab === tabId;
      tab.setAttribute("aria-selected", String(isSelected));
      tab.tabIndex = isSelected ? 0 : -1;

      const panel = document.getElementById(tab.dataset.recordTab);
      if (panel) {
        panel.hidden = !isSelected;
      }
    });

    if (updateUrl) {
      const fragment = tabId === ASSESSMENTS_TAB ? "#requirements" : "";
      history.replaceState(null, "", `${location.pathname}${location.search}${fragment}`);
    }
  }

  // The arrow keys wrap around; Home and End jump to the first and last tab.
  function tabIndexForKey(key, tabs, currentTab) {
    if (key === "Home") {
      return 0;
    }

    if (key === "End") {
      return tabs.length - 1;
    }

    const step = key === "ArrowRight" ? 1 : -1;
    return (tabs.indexOf(currentTab) + step + tabs.length) % tabs.length;
  }

  // --- Initialization of newly rendered forms and editors ---

  // After a failed submit the server renders the stored record and passes the submitted
  // values along, so the user's input is put back and counts as unsaved.
  function restoreSubmittedValues(form) {
    const submittedValues = Object.entries(JSON.parse(form.dataset.submittedValues));
    for (const [name, value] of submittedValues) {
      if (isRequestField(name)) {
        continue;
      }

      const field = form.elements.namedItem(name);
      restoreField(field, value);
    }

    formsForcedDirty.add(form);
    refreshDependentFields(form);
  }

  function initializeForm(form) {
    if (initialSignatures.has(form)) {
      return;
    }

    initialSignatures.set(form, formSignature(form));
    if (form.dataset.submittedValues) {
      restoreSubmittedValues(form);
    }
  }

  function initializeEditor(editor) {
    if (editor.dataset.initialized) {
      return;
    }

    editor.dataset.initialized = "true";
    const opensOnLoad = editor.dataset.openOnLoad === "true";

    if (isDialog(editor)) {
      // Escape goes through the same unsaved-changes check as the close button.
      editor.addEventListener("cancel", (event) => {
        event.preventDefault();
        requestClose(editor);
      });

      if (opensOnLoad) {
        showEditor(editor, currentOpener(editor), null, false);
      }
    }

    if (!isDialog(editor) && opensOnLoad) {
      showEditor(editor, currentOpener(editor), null);
    }

    // An editor rendered with an error holds input that has not been saved.
    const error = editor.querySelector(
      ".editor-error:not([hidden]), .notice.failure:not([hidden])",
    );
    if (error) {
      guardedForms(editor).forEach((form) => formsForcedDirty.add(form));
    }
  }

  function scheduleNoticeRemoval() {
    clearTimeout(noticeTimer);
    const notice = document.querySelector(".workspace-notice.success");
    if (notice) {
      noticeTimer = setTimeout(() => notice.remove(), NOTICE_LIFETIME);
    }
  }

  function initialize() {
    guardedForms(document).forEach(initializeForm);
    document.querySelectorAll("[data-editor]").forEach(initializeEditor);
    scheduleNoticeRemoval();
  }

  // --- Clicks: openers, close buttons, tabs and links ---

  function editorOpenedBy(opener) {
    const editor = document.getElementById(opener.dataset.editorOpen || opener.dataset.dialogOpen);
    return editor?.matches("[data-editor]") ? editor : null;
  }

  // A link that replaces the current page, as opposed to a download, a new tab or a new window.
  function navigatesInPlace(link, event) {
    if (!link || link.target === "_blank" || link.hasAttribute("download")) {
      return false;
    }

    const hasModifierKey = event.ctrlKey || event.metaKey || event.shiftKey || event.altKey;
    return !hasModifierKey;
  }

  document.addEventListener("click", async (event) => {
    const opener = event.target.closest("[data-editor-open], [data-dialog-open]");
    const openedEditor = opener ? editorOpenedBy(opener) : null;
    if (openedEditor) {
      event.preventDefault();
      await openEditor(openedEditor, opener);
      return;
    }

    const closer = event.target.closest("[data-editor-close]");
    if (closer) {
      event.preventDefault();
      await requestClose(closer.closest("[data-editor]"));
      return;
    }

    const tab = event.target.closest("[data-record-tab]");
    if (tab) {
      const editors = pendingEditors();
      if (!(await canClose(editors))) {
        return;
      }

      editors.forEach((editor) => closeEditor(editor, false));
      selectTab(tab.dataset.recordTab, true);
      return;
    }

    const link = event.target.closest("a[href]");
    if (!navigatesInPlace(link, event)) {
      return;
    }

    if (!pendingEditors().some(isDirty)) {
      return;
    }

    // The navigation waits for the user's decision about the unsaved changes.
    event.preventDefault();
    if (await canClose(pendingEditors())) {
      pendingEditors().forEach((editor) => closeEditor(editor, false));
      isLeavingPage = true;
      location.assign(link.href);
    }
  });

  // --- Keyboard: tab navigation and Escape ---

  document.addEventListener("keydown", (event) => {
    const tab = event.target.closest("[data-record-tab]");
    if (tab && ["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) {
      event.preventDefault();
      const tabs = [...document.querySelectorAll("[data-record-tab]")];
      const targetTab = tabs[tabIndexForKey(event.key, tabs, tab)];
      targetTab.focus();
      targetTab.click();
      return;
    }

    // Dialogs handle Escape through their cancel event; inline editors need it here.
    if (event.key === "Escape" && !confirmation.open) {
      const inlineEditor = activeEditors().find((editor) => !isDialog(editor));
      if (inlineEditor) {
        event.preventDefault();
        requestClose(inlineEditor);
      }
    }
  });

  // --- Submitting: confirmation and drafts in other editors ---

  // Stops the submission until the user has answered; submitting again marks the form as
  // confirmed so that the second pass goes through.
  function holdSubmission(event) {
    event.preventDefault();
    event.stopImmediatePropagation();
  }

  function resubmitConfirmed(form, submitter) {
    form.dataset.confirmed = "true";
    form.requestSubmit(submitter);
  }

  // Registered for the capture phase so that it runs before HTMX sends the form.
  document.addEventListener(
    "submit",
    async (event) => {
      const form = event.target;

      // Sorting preserves open drafts through the fragment exchange.
      if (form.hasAttribute("data-sort")) {
        return;
      }

      const isConfirmed = form.dataset.confirmed === "true";
      delete form.dataset.confirmed;

      if (!isConfirmed && form.dataset.confirmAction) {
        holdSubmission(event);
        const accepted = await confirmAction(
          form.dataset.confirmAction,
          form.dataset.confirmDescription,
          event.submitter?.textContent?.trim() || form.dataset.confirmAction,
          confirmation.dataset.cancelAction,
        );
        if (accepted) {
          resubmitConfirmed(form, event.submitter);
        }

        return;
      }

      const otherEditors = pendingEditors().filter((editor) => !editor.contains(form));
      if (otherEditors.some(isDirty)) {
        holdSubmission(event);
        if (await canClose(otherEditors)) {
          otherEditors.forEach((editor) => closeEditor(editor, false));
          resubmitConfirmed(form, event.submitter);
        }
      }

      // A plain form submission that was not held back loads a new page.
      const isPlainSubmission = !form.hasAttribute("hx-post") && !form.hasAttribute("data-passkey");
      if (isPlainSubmission && !event.defaultPrevented) {
        isLeavingPage = true;
      }
    },
    true,
  );

  // --- Leaving the page ---

  document.addEventListener("form:navigate", () => {
    isLeavingPage = true;
  });

  window.addEventListener("beforeunload", (event) => {
    if (!isLeavingPage && pendingEditors().some(isDirty)) {
      event.preventDefault();
      event.returnValue = "";
    }
  });

  // --- Error messages inside an editor ---

  function createErrorBox(editor) {
    const error = document.createElement("div");
    error.className = "editor-error";
    error.setAttribute("role", "alert");
    error.tabIndex = -1;
    editor.prepend(error);
    return error;
  }

  // Shows the message at the top of the editor. With blockSubmitting the submit buttons are
  // disabled, because trying again cannot succeed.
  function editorError(editor, message, blockSubmitting = false) {
    if (!editor) {
      return;
    }

    const error = editor.querySelector(".editor-error") || createErrorBox(editor);
    error.textContent = message;
    error.hidden = false;

    if (blockSubmitting) {
      editor.querySelectorAll("button[type=submit]").forEach((button) => {
        button.disabled = true;
      });
    }

    error.focus({ preventScroll: true });
    error.scrollIntoView({ block: "nearest" });

    // The editor reports the failure itself; the page-wide warning would only repeat it.
    document.getElementById("connection-error").hidden = true;
  }

  function appendParagraph(container, text) {
    const paragraph = document.createElement("p");
    paragraph.textContent = text;
    container.append(paragraph);
  }

  function readJsonResponse(xhr) {
    const isJson = xhr.getResponseHeader("Content-Type")?.includes("application/json");
    return isJson ? JSON.parse(xhr.responseText) : null;
  }

  function isSamePage(target) {
    return (
      target.origin === location.origin &&
      target.pathname === location.pathname &&
      target.search === location.search
    );
  }

  // --- HTMX: redirects ---

  document.addEventListener("htmx:beforeOnLoad", (event) => {
    const redirect = event.detail.xhr.getResponseHeader("HX-Redirect");
    if (event.detail.xhr.status < 400 && redirect) {
      isLeavingPage = true;
      const target = new URL(redirect, location.href);

      // A fragment-only navigation would leave the old values and disabled form in place.
      if (isSamePage(target)) {
        event.preventDefault();
        history.replaceState(null, "", target.href);
        location.reload();
      }
    }
  });

  // --- HTMX: remembering what a request must not lose ---

  // Sorting replaces the whole workspace, including every editor in it.
  function captureEditorsForSorting() {
    return pendingEditors().map((editor) => ({
      id: editor.id,
      opener: editor.dataset.opener,
      open: activeEditors().includes(editor),
      forms: guardedForms(editor).map((form) => ({
        values: draftValues(form),
        dirty: hasUnsavedChanges(form),
      })),
    }));
  }

  function captureEditorRequest(editor, form) {
    return {
      editorId: editor.id,
      openerId: editor.dataset.opener,
      fallbackId: editor.dataset.focusFallback,
      retainOnError: form.hasAttribute("data-retain-on-error"),
      tab: document.querySelector("[data-record-tab][aria-selected=true]")?.dataset.recordTab,
      draft: draftValues(form),
      editorBeforeSwap: editor,
      relatedDrafts: guardedForms(editor)
        .filter((other) => other !== form)
        .map((other) => draftValues(other)),
    };
  }

  document.addEventListener("htmx:beforeRequest", (event) => {
    const form = event.detail.elt.closest("form");

    // Operation and request forms act as their own editor for errors and the busy state.
    const editor =
      form?.closest("[data-editor]") ||
      (form?.matches("[data-operation], [data-request-form]") ? form : null);

    if (form?.hasAttribute("data-sort")) {
      requestStates.set(event.detail.xhr, { sorting: captureEditorsForSorting() });
      return;
    }

    if (!editor) {
      if (form?.dataset.focusFallback) {
        requestStates.set(event.detail.xhr, { fallbackId: form.dataset.focusFallback });
      }

      return;
    }

    busyEditors.add(editor);
    editor.setAttribute("aria-busy", "true");
    requestStates.set(event.detail.xhr, captureEditorRequest(editor, form));
  });

  // --- HTMX: rejected requests that keep the editor as it is ---

  function createReviewLink(reviewUrl) {
    const link = document.createElement("a");
    link.href = reviewUrl;
    link.target = "_blank";
    link.rel = "noopener";
    link.textContent = document.body.dataset.editorReview;
    return link;
  }

  // Submits the kept draft again, now based on the version the user has reviewed.
  function retryWithVersion(editor, version) {
    if (busyEditors.has(editor)) {
      return;
    }

    const form = editor.matches("form") ? editor : editor.querySelector("form[data-guard-draft]");
    form.elements.namedItem("Version").value = version;
    form.requestSubmit(form.querySelector("button[type=submit]"));
  }

  function createRetryButton(editor, isOperation, version) {
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = isOperation
      ? document.body.dataset.editorOperationRetry
      : document.body.dataset.editorRetry;
    button.addEventListener("click", () => retryWithVersion(editor, version));
    return button;
  }

  // Somebody else changed the record: explain what happened, link to the current state and
  // offer to save the draft on top of it.
  function appendConflictResolution(editor, conflict) {
    const error = editor.querySelector(".editor-error");
    const isOperation = editor.hasAttribute("data-operation");

    appendParagraph(error, conflict.context);
    appendParagraph(
      error,
      isOperation
        ? document.body.dataset.editorOperationConflict
        : document.body.dataset.editorPageConflict,
    );
    error.append(createReviewLink(conflict.reviewUrl));
    error.append(createRetryButton(editor, isOperation, conflict.version));
  }

  function showRejectionInPlace(state, event) {
    const xhr = event.detail.xhr;
    event.detail.shouldSwap = false;
    event.detail.isError = false;

    const response = readJsonResponse(xhr);
    editorError(state.editorBeforeSwap, response?.error || xhr.responseText);

    const isResolvableConflict =
      xhr.status === STATUS_CONFLICT && response?.reviewUrl && response.version;
    if (isResolvableConflict) {
      appendConflictResolution(state.editorBeforeSwap, response);
    }
  }

  // A response that comes from the sign-in pages means the request was not allowed, for
  // example because the session has expired.
  function wasRedirectedToSignIn(xhr) {
    const responseUrl = xhr.responseURL;
    return (
      responseUrl &&
      new URL(responseUrl).pathname.startsWith("/Auth/") &&
      !location.pathname.startsWith("/Auth/")
    );
  }

  document.addEventListener("htmx:beforeSwap", (event) => {
    const state = requestStates.get(event.detail.xhr);

    if (state?.retainOnError && REJECTED_STATUSES.includes(event.detail.xhr.status)) {
      showRejectionInPlace(state, event);
    }

    if (state && wasRedirectedToSignIn(event.detail.xhr)) {
      state.denied = true;
      event.detail.shouldSwap = false;
      event.detail.isError = false;
    }
  });

  // --- HTMX: putting drafts back into the swapped content ---

  function restoreEditorsAfterSorting(savedEditors) {
    for (const saved of savedEditors) {
      const editor = document.getElementById(saved.id);
      if (!editor) {
        continue;
      }

      editor.dataset.opener = saved.opener;
      guardedForms(editor).forEach((form, index) => {
        const draft = saved.forms[index];
        if (!draft) {
          return;
        }

        restoreDraft(form, draft.values);
        if (draft.dirty) {
          formsForcedDirty.add(form);
        }

        refreshDependentFields(form);
      });

      if (saved.open) {
        showEditor(editor, currentOpener(editor), null, false);
      }
    }
  }

  // Forms of one editor are told apart by the value of their "action" field.
  function findFormByAction(forms, values) {
    const action = new Map(values).get("action");
    return forms.find((form) => form.elements.namedItem("action")?.value === action);
  }

  function restoreRejectedDraft(form, values) {
    restoreDraft(form, values);
    formsForcedDirty.add(form);
    refreshDependentFields(form);
  }

  // Lets the user look at the record as it is now without losing the draft.
  function createReviewButton(editor) {
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = document.body.dataset.editorReview;
    button.addEventListener("click", () => {
      // Keep the draft available while showing the newly loaded record.
      hideEditor(editor);
      currentOpener(editor)?.setAttribute("aria-expanded", "false");
      restoreFocus(currentOpener(editor), true);
    });
    return button;
  }

  // The rejected editor was rendered again from the stored record; the user's input goes back
  // in and the editor stays open.
  function restoreRejectedEditor(editor, state, status) {
    // A failed reset/removal must also retain edits in the adjacent main form.
    for (const values of state.relatedDrafts || []) {
      const relatedForm = findFormByAction(guardedForms(editor), values);
      if (!relatedForm) {
        continue;
      }

      restoreRejectedDraft(relatedForm, values);
    }

    const submittedForm = findFormByAction([...editor.querySelectorAll("form")], state.draft);
    if (submittedForm) {
      restoreRejectedDraft(submittedForm, state.draft);
    }

    showEditor(editor, currentOpener(editor), null, false);

    if (status === STATUS_CONFLICT) {
      editorError(editor, document.body.dataset.editorConflict);
      editor.querySelector(".editor-error").append(createReviewButton(editor));
    } else {
      const error = editor.querySelector(".editor-error");
      error?.focus({ preventScroll: true });
    }
  }

  document.addEventListener("htmx:afterSwap", (event) => {
    const state = requestStates.get(event.detail.xhr);
    initialize();
    if (!state) {
      return;
    }

    if (state.sorting) {
      restoreEditorsAfterSorting(state.sorting);
      return;
    }

    if (state.tab) {
      selectTab(state.tab);
    }

    const editor = document.getElementById(state.editorId);
    if (!editor) {
      return;
    }

    editor.dataset.opener = state.openerId;
    if (REJECTED_STATUSES.includes(event.detail.xhr.status)) {
      restoreRejectedEditor(editor, state, event.detail.xhr.status);
    }
  });

  // --- HTMX: after a request has finished ---

  document.addEventListener("htmx:afterSettle", (event) => {
    const state = requestStates.get(event.detail.xhr);
    if (!state) {
      return;
    }

    // A saved editor is gone after the swap, so the focus returns to where it was opened.
    const succeeded = event.detail.xhr.status < 400 && !state.denied;
    if (succeeded && !state.sorting) {
      requestAnimationFrame(() =>
        restoreFocus(
          document.getElementById(state.openerId) || document.getElementById(state.fallbackId),
          true,
        ),
      );
    }

    requestStates.delete(event.detail.xhr);
  });

  function failureMessage(status, isPermissionFailure) {
    if (isPermissionFailure) {
      return document.body.dataset.editorPermissionError;
    }

    if (status === 404) {
      return document.body.dataset.editorMissingError;
    }

    if (status === 429) {
      return document.body.dataset.requestRateError;
    }

    return document.body.dataset.editorNetworkError;
  }

  function releaseEditor(editor) {
    busyEditors.delete(editor);
    editor.removeAttribute("aria-busy");
  }

  document.addEventListener("htmx:afterRequest", (event) => {
    const state = requestStates.get(event.detail.xhr);
    const editor = state?.editorBeforeSwap || event.detail.elt?.closest("[data-editor]");
    if (editor) {
      releaseEditor(editor);
    }

    // Rejected forms were handled during the swap; what remains are requests that failed
    // without usable content: no connection, a server error or a lost session.
    const status = event.detail.xhr.status;
    const hasFailed = status === 0 || status >= 400 || state?.denied;
    if (!state || REJECTED_STATUSES.includes(status) || !hasFailed) {
      return;
    }

    const isPermissionFailure = [401, 403].includes(status) || state.denied;
    const isMissing = status === 404;
    const response = readJsonResponse(event.detail.xhr);
    editorError(
      document.getElementById(state.editorId),
      response?.error || failureMessage(status, isPermissionFailure),
      isPermissionFailure || isMissing,
    );
    requestStates.delete(event.detail.xhr);
  });

  // --- Page load ---

  // Page forms finish configuring dependent fields before recording their baseline.
  document.addEventListener(
    "DOMContentLoaded",
    () => {
      initialize();
      const target = document.getElementById(location.hash.slice(1));
      if (target?.hasAttribute("data-return-focus")) {
        target.focus({ preventScroll: true });
      }
    },
    { once: true },
  );

  function hashPointsToAssessments() {
    if (location.hash === "#requirements") {
      return true;
    }

    return (
      location.hash.length > 1 &&
      document.getElementById(location.hash.slice(1))?.closest(`#${ASSESSMENTS_TAB}`)
    );
  }

  // Technical data opens first; a link into the requirements selects their tab.
  if (hashPointsToAssessments()) {
    selectTab(ASSESSMENTS_TAB);
  }

  // The workspace shows sharing and settings as sections selected by the query string, so a
  // link that names them in the fragment is turned into that form.
  const isWorkspaceSectionLink =
    document.getElementById("evaluation-workspace") &&
    ["#sharing", "#case-settings"].includes(location.hash);
  if (isWorkspaceSectionLink) {
    const url = new URL(location.href);
    url.searchParams.set("section", location.hash === "#sharing" ? "sharing" : "settings");
    url.hash = "";
    location.replace(url);
  }
})();
