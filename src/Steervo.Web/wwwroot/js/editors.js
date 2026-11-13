(() => {
  const initial = new WeakMap();
  const forcedDirty = new WeakSet();
  const requests = new WeakMap();
  const busy = new WeakSet();
  const confirmation = document.getElementById('action-confirmation');
  let pendingConfirmation;
  let leaving = false;
  let noticeTimer;

  const fields = form => [...new FormData(form)].filter(([key]) => key !== '__RequestVerificationToken' && key.toLowerCase() !== 'version');
  function restoreField(field, value) {
    if (!field || !('value' in field)) return;
    if (field.type === 'file') return;
    value = value ?? '';
    if (field instanceof HTMLSelectElement && value !== '' && ![...field.options].some(option => option.value === value))
      field.add(new Option(value, value));
    field.value = value;
  }
  const signature = form => JSON.stringify(fields(form).map(([name, value]) =>
    [name, value instanceof File ? (value.name ? [value.name, value.size, value.lastModified] : null) : value]));
  const guardedForms = root => [...root.querySelectorAll('form[data-guard-draft]')];
  const dirty = root => guardedForms(root).some(form => forcedDirty.has(form) || (initial.has(form) && initial.get(form) !== signature(form)));
  const activeEditors = () => [...document.querySelectorAll('[data-editor]')].filter(editor => editor instanceof HTMLDialogElement ? editor.open : !editor.hidden);
  const pendingEditors = () => [...document.querySelectorAll('[data-editor]')].filter(editor => activeEditors().includes(editor) || dirty(editor));
  const currentOpener = editor => document.getElementById(editor.dataset.opener);

  function confirmAction(title, description, accept, cancel) {
    if (pendingConfirmation) return Promise.resolve(false);
    confirmation.querySelector('#confirmation-title').textContent = title;
    confirmation.querySelector('#confirmation-description').textContent = description;
    confirmation.querySelector('[data-confirm-accept]').textContent = accept;
    confirmation.querySelector('[data-confirm-cancel]').textContent = cancel;
    confirmation.showModal();
    confirmation.querySelector('[data-confirm-cancel]').focus();
    return new Promise(resolve => { pendingConfirmation = resolve; });
  }
  function resolveConfirmation(value) {
    const resolve = pendingConfirmation;
    pendingConfirmation = null;
    confirmation.close();
    resolve?.(value);
  }
  confirmation.addEventListener('cancel', event => { event.preventDefault(); resolveConfirmation(false); });
  confirmation.querySelector('[data-confirm-cancel]').addEventListener('click', () => resolveConfirmation(false));
  confirmation.querySelector('[data-confirm-accept]').addEventListener('click', () => resolveConfirmation(true));

  function restoreFocus(opener, reveal = false) {
    if (!opener) return;
    opener.focus({ preventScroll: true });
    if (reveal) {
      const bounds = opener.getBoundingClientRect();
      if (bounds.top < 12 || bounds.bottom > innerHeight - 12)
        window.scrollTo({ top: scrollY + bounds.top - Math.min(180, innerHeight / 3), behavior: 'instant' });
    }
  }
  function closeEditor(editor, restore = true) {
    if (editor.hasAttribute('data-page-editor')) {
      guardedForms(editor).forEach(form => { forcedDirty.delete(form); initial.set(form, signature(form)); });
      return;
    }
    guardedForms(editor).forEach(form => {
      form.reset();
      if (form.hasAttribute('data-new-object')) {
        for (const field of form.elements) {
          if (!field.name?.startsWith('Input.')) continue;
          field.value = field.name === 'Input.Condition' ? 'Unknown' : '';
        }
      }
      forcedDirty.delete(form);
      form.querySelectorAll('[data-criterion-type]').forEach(field => field.dispatchEvent(new Event('change')));
      initial.set(form, signature(form));
    });
    const error = editor.querySelector('.editor-error');
    if (error) error.hidden = true;
    if (editor instanceof HTMLDialogElement) editor.close();
    else editor.hidden = true;
    const opener = currentOpener(editor);
    opener?.setAttribute('aria-expanded', 'false');
    if (restore) restoreFocus(opener, true);
  }
  async function canClose(editors) {
    if (editors.some(editor => busy.has(editor))) return false;
    if (!editors.some(dirty)) return true;
    return confirmAction(confirmation.dataset.discardTitle, confirmation.dataset.discardDescription,
      confirmation.dataset.discardAction, confirmation.dataset.continueAction);
  }
  async function requestClose(editor) {
    if (editor && await canClose([editor])) {
      closeEditor(editor);
      if (editor.hasAttribute('data-page-editor')) { leaving = true; location.assign(editor.dataset.returnUrl); }
    }
  }
  function showEditor(editor, opener, focusId, focus = true) {
    if (opener?.id) editor.dataset.opener = opener.id;
    if (editor instanceof HTMLDialogElement) { if (!editor.open) editor.showModal(); }
    else editor.hidden = false;
    currentOpener(editor)?.setAttribute('aria-expanded', 'true');
    if (!focus) return;
    const field = (focusId && document.getElementById(focusId)) || editor.querySelector('[data-editor-focus]') || editor.querySelector('input:not([type=hidden]):not([readonly]), select, textarea');
    for (let parent = field?.parentElement; parent && parent !== editor; parent = parent.parentElement)
      if (parent instanceof HTMLDetailsElement) parent.open = true;
    field?.focus({ preventScroll: true });
    if (!(editor instanceof HTMLDialogElement)) editor.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    else if (focusId) field?.scrollIntoView({ block: 'center' });
  }
  async function openEditor(editor, opener) {
    if (!editor) return;
    const previous = pendingEditors().filter(item => item !== editor);
    if (!await canClose(previous)) return;
    previous.forEach(item => closeEditor(item, false));
    showEditor(editor, opener, opener?.dataset.focusId);
  }
  function selectTab(id, updateUrl = false) {
    document.querySelectorAll('[data-record-tab]').forEach(tab => {
      const selected = tab.dataset.recordTab === id;
      tab.setAttribute('aria-selected', String(selected));
      tab.tabIndex = selected ? 0 : -1;
      const panel = document.getElementById(tab.dataset.recordTab);
      if (panel) panel.hidden = !selected;
    });
    if (updateUrl) history.replaceState(null, '', `${location.pathname}${location.search}${id === 'record-specifications' ? '#specifications' : ''}`);
  }
  function initialize() {
    guardedForms(document).forEach(form => {
      if (initial.has(form)) return;
      initial.set(form, signature(form));
      if (form.dataset.submittedValues) {
        for (const [name, value] of Object.entries(JSON.parse(form.dataset.submittedValues))) {
          if (['__RequestVerificationToken', 'version'].includes(name)) continue;
          const field = form.elements.namedItem(name);
          restoreField(field, value);
        }
        forcedDirty.add(form);
        form.querySelectorAll('[data-criterion-type]').forEach(field => field.dispatchEvent(new Event('change')));
      }
    });
    document.querySelectorAll('[data-editor]').forEach(editor => {
      if (editor.dataset.initialized) return;
      editor.dataset.initialized = 'true';
      if (editor instanceof HTMLDialogElement) {
        editor.addEventListener('cancel', event => { event.preventDefault(); requestClose(editor); });
        if (editor.dataset.openOnLoad === 'true') showEditor(editor, currentOpener(editor), null, false);
      }
      const error = editor.querySelector('.editor-error:not([hidden]), .notice.failure:not([hidden])');
      if (error) guardedForms(editor).forEach(form => forcedDirty.add(form));
    });
    clearTimeout(noticeTimer);
    const notice = document.querySelector('.workspace-notice.success');
    if (notice) noticeTimer = setTimeout(() => notice.remove(), 3200);
  }

  document.addEventListener('click', async event => {
    const opener = event.target.closest('[data-editor-open], [data-dialog-open]');
    if (opener) {
      const editor = document.getElementById(opener.dataset.editorOpen || opener.dataset.dialogOpen);
      if (editor?.matches('[data-editor]')) { event.preventDefault(); await openEditor(editor, opener); return; }
    }
    const closer = event.target.closest('[data-editor-close]');
    if (closer) { event.preventDefault(); await requestClose(closer.closest('[data-editor]')); return; }
    const tab = event.target.closest('[data-record-tab]');
    if (tab) {
      const editors = pendingEditors();
      if (!await canClose(editors)) return;
      editors.forEach(editor => closeEditor(editor, false));
      selectTab(tab.dataset.recordTab, true);
      return;
    }
    const link = event.target.closest('a[href]');
    if (!link || link.target === '_blank' || link.hasAttribute('download') || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    if (!pendingEditors().some(dirty)) return;
    event.preventDefault();
    if (await canClose(pendingEditors())) {
      pendingEditors().forEach(editor => closeEditor(editor, false));
      leaving = true; location.assign(link.href);
    }
  });
  document.addEventListener('keydown', event => {
    const tab = event.target.closest('[data-record-tab]');
    if (tab && ['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
      event.preventDefault();
      const tabs = [...document.querySelectorAll('[data-record-tab]')];
      const index = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1
        : (tabs.indexOf(tab) + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
      tabs[index].focus(); tabs[index].click(); return;
    }
    if (event.key === 'Escape' && !confirmation.open) {
      const editor = activeEditors().find(item => !(item instanceof HTMLDialogElement));
      if (editor) { event.preventDefault(); requestClose(editor); }
    }
  });
  document.addEventListener('submit', async event => {
    const form = event.target;
    // Sorting preserves open drafts through the fragment exchange.
    if (form.hasAttribute('data-sort')) return;
    if (form.dataset.confirmed === 'true') { delete form.dataset.confirmed; return; }
    if (form.dataset.confirmAction) {
      event.preventDefault(); event.stopImmediatePropagation();
      if (await confirmAction(form.dataset.confirmAction, form.dataset.confirmDescription,
        event.submitter?.textContent?.trim() || form.dataset.confirmAction, confirmation.dataset.cancelAction)) {
        form.dataset.confirmed = 'true'; form.requestSubmit(event.submitter);
      }
      return;
    }
    const other = pendingEditors().filter(editor => !editor.contains(form));
    if (other.some(dirty)) {
      event.preventDefault(); event.stopImmediatePropagation();
      if (await canClose(other)) {
        other.forEach(editor => closeEditor(editor, false));
        form.dataset.confirmed = 'true'; form.requestSubmit(event.submitter);
      }
    }
    if (!form.hasAttribute('hx-post') && !event.defaultPrevented) leaving = true;
  }, true);
  window.addEventListener('beforeunload', event => {
    if (!leaving && pendingEditors().some(dirty)) { event.preventDefault(); event.returnValue = ''; }
  });

  function editorError(editor, message, permission = false) {
    if (!editor) return;
    let error = editor.querySelector('.editor-error');
    if (!error) { error = document.createElement('div'); error.className = 'editor-error'; error.setAttribute('role', 'alert'); error.tabIndex = -1; editor.prepend(error); }
    error.textContent = message; error.hidden = false;
    if (permission) editor.querySelectorAll('button[type=submit]').forEach(button => { button.disabled = true; });
    error.focus({ preventScroll: true });
    error.scrollIntoView({ block: 'nearest' });
    document.getElementById('connection-error').hidden = true;
  }
  document.addEventListener('htmx:beforeOnLoad', event => {
    if (event.detail.xhr.status < 400 && event.detail.xhr.getResponseHeader('HX-Redirect')) leaving = true;
  });
  document.addEventListener('htmx:beforeRequest', event => {
    const form = event.detail.elt.closest('form');
    const editor = form?.closest('[data-editor]');
    if (form?.hasAttribute('data-sort')) {
      requests.set(event.detail.xhr, { sorting: pendingEditors().map(item => ({
        id: item.id, opener: item.dataset.opener, open: activeEditors().includes(item),
        forms: guardedForms(item).map(saved => ({ values: fields(saved), dirty: forcedDirty.has(saved) || initial.get(saved) !== signature(saved) }))
      })) });
      return;
    }
    if (!editor) {
      if (form?.dataset.focusFallback) requests.set(event.detail.xhr, { fallbackId: form.dataset.focusFallback });
      return;
    }
    busy.add(editor); editor.setAttribute('aria-busy', 'true');
    requests.set(event.detail.xhr, {
      editorId: editor.id, openerId: editor.dataset.opener,
      fallbackId: editor.dataset.focusFallback, retain: form.hasAttribute('data-retain-on-error'),
      tab: document.querySelector('[data-record-tab][aria-selected=true]')?.dataset.recordTab,
      draft: fields(form), oldEditor: editor,
      relatedDrafts: guardedForms(editor).filter(other => other !== form).map(other => fields(other))
    });
  });
  document.addEventListener('htmx:beforeSwap', event => {
    const state = requests.get(event.detail.xhr);
    if (state?.retain && [409, 422].includes(event.detail.xhr.status)) {
      event.detail.shouldSwap = false; event.detail.isError = false;
      const json = event.detail.xhr.getResponseHeader('Content-Type')?.includes('application/json')
        ? JSON.parse(event.detail.xhr.responseText) : null;
      editorError(state.oldEditor, json?.error || event.detail.xhr.responseText);
      if (event.detail.xhr.status === 409 && json?.reviewUrl && json.version) {
        const error = state.oldEditor.querySelector('.editor-error');
        const context = document.createElement('p'); context.textContent = json.context; error.append(context);
        const help = document.createElement('p'); help.textContent = document.body.dataset.editorPageConflict; error.append(help);
        const review = document.createElement('a'); review.href = json.reviewUrl; review.target = '_blank'; review.rel = 'noopener';
        review.textContent = document.body.dataset.editorReview; error.append(review);
        const retry = document.createElement('button'); retry.type = 'button'; retry.textContent = document.body.dataset.editorRetry;
        retry.addEventListener('click', () => {
          if (busy.has(state.oldEditor)) return;
          const form = state.oldEditor.querySelector('form[data-guard-draft]');
          form.elements.namedItem('Version').value = json.version;
          form.requestSubmit(form.querySelector('button[type=submit]'));
        });
        error.append(retry);
      }
    }
    const responseUrl = event.detail.xhr.responseURL;
    if (state && responseUrl && new URL(responseUrl).pathname.startsWith('/Auth/')) {
      state.denied = true;
      event.detail.shouldSwap = false;
      event.detail.isError = false;
    }
  });
  document.addEventListener('htmx:afterSwap', event => {
    const state = requests.get(event.detail.xhr);
    initialize();
    if (!state) return;
    if (state.sorting) {
      for (const saved of state.sorting) {
        const editor = document.getElementById(saved.id);
        if (!editor) continue;
        editor.dataset.opener = saved.opener;
        guardedForms(editor).forEach((form, index) => {
          const draft = saved.forms[index];
          if (!draft) return;
          for (const [name, value] of draft.values) restoreField(form.elements.namedItem(name), value);
          if (draft.dirty) forcedDirty.add(form);
          form.querySelectorAll('[data-criterion-type]').forEach(field => field.dispatchEvent(new Event('change')));
        });
        if (saved.open) showEditor(editor, currentOpener(editor), null, false);
      }
      return;
    }
    if (state.tab) selectTab(state.tab);
    const editor = document.getElementById(state.editorId);
    if (!editor) return;
    editor.dataset.opener = state.openerId;
    if ([409, 422].includes(event.detail.xhr.status)) {
      // A failed reset/removal must also retain edits in the adjacent main form.
      for (const values of state.relatedDrafts || []) {
        const action = new Map(values).get('action');
        const related = guardedForms(editor).find(item => item.elements.namedItem('action')?.value === action);
        if (!related) continue;
        for (const [name, value] of values) restoreField(related.elements.namedItem(name), value);
        forcedDirty.add(related);
        related.querySelectorAll('[data-criterion-type]').forEach(field => field.dispatchEvent(new Event('change')));
      }
      const action = new Map(state.draft).get('action');
      const form = [...editor.querySelectorAll('form')].find(item => item.elements.namedItem('action')?.value === action);
      if (form) {
        for (const [name, value] of state.draft) {
          const field = form.elements.namedItem(name);
          restoreField(field, value);
        }
        forcedDirty.add(form);
        form.querySelectorAll('[data-criterion-type]').forEach(field => field.dispatchEvent(new Event('change')));
      }
      showEditor(editor, currentOpener(editor), null, false);
      if (event.detail.xhr.status === 409) {
        editorError(editor, document.body.dataset.editorConflict);
        const review = document.createElement('button'); review.type = 'button'; review.textContent = document.body.dataset.editorReview;
        review.addEventListener('click', () => {
          // Keep the draft available while showing the newly loaded record.
          if (editor instanceof HTMLDialogElement) editor.close(); else editor.hidden = true;
          currentOpener(editor)?.setAttribute('aria-expanded', 'false');
          restoreFocus(currentOpener(editor), true);
        });
        editor.querySelector('.editor-error').append(review);
      } else {
        const error = editor.querySelector('.editor-error');
        error?.focus({ preventScroll: true });
      }
    }
  });
  document.addEventListener('htmx:afterSettle', event => {
    const state = requests.get(event.detail.xhr);
    if (!state) return;
    if (event.detail.xhr.status < 400 && !state.denied) {
      if (!state.sorting) requestAnimationFrame(() => restoreFocus(document.getElementById(state.openerId) || document.getElementById(state.fallbackId), true));
    }
    requests.delete(event.detail.xhr);
  });
  document.addEventListener('htmx:afterRequest', event => {
    const state = requests.get(event.detail.xhr);
    const oldEditor = state?.oldEditor || event.detail.elt?.closest('[data-editor]');
    if (oldEditor) { busy.delete(oldEditor); oldEditor.removeAttribute('aria-busy'); }
    const status = event.detail.xhr.status;
    if (state && ![409, 422].includes(status) && (status === 0 || status >= 400 || state.denied)) {
      const permission = [401, 403].includes(status) || state.denied;
      editorError(document.getElementById(state.editorId), permission ? document.body.dataset.editorPermissionError : document.body.dataset.editorNetworkError, permission);
      requests.delete(event.detail.xhr);
    }
  });
  // Page forms finish configuring dependent fields before recording their baseline.
  document.addEventListener('DOMContentLoaded', initialize, { once: true });
  if (location.hash === '#specifications') selectTab('record-specifications');
  if (document.getElementById('evaluation-workspace') && ['#sharing', '#case-settings'].includes(location.hash)) {
    const url = new URL(location.href);
    url.searchParams.set('section', location.hash === '#sharing' ? 'sharing' : 'settings');
    url.hash = ''; location.replace(url);
  }
})();
