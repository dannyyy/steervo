// Workspaces marked with data-preserve-viewport are replaced as a whole after a form request.
// This script carries what the user sees across that swap: the scroll position relative to the
// submitted record, scrolled regions, opened details, unsaved field values and the focus.
(() => {
  // What was captured for each running request, keyed by its XMLHttpRequest.
  const requestSnapshots = new WeakMap();

  // --- Capturing the state before the swap ---

  function originalValue(field) {
    if (field instanceof HTMLSelectElement) {
      const defaultOption = [...field.options].find((option) => option.defaultSelected);
      return defaultOption?.value ?? field.options[0]?.value;
    }

    return field.defaultValue;
  }

  // Fields of guarded editors are restored by the editor script; fields of the form that was
  // just saved successfully must show the values returned by the server.
  function collectUnsavedFields(workspace, submittedArea, requestSucceeded) {
    return [...workspace.querySelectorAll("input[id], select[id], textarea[id]")]
      .filter((field) => {
        if (field.closest("form[data-guard-draft]")) {
          return false;
        }

        if (requestSucceeded && submittedArea?.contains(field)) {
          return false;
        }

        return field.value !== originalValue(field);
      })
      .map((field) => [field.id, field.value]);
  }

  function captureSnapshot(snapshot, xhr) {
    const { form, workspace } = snapshot;
    const anchor = form.closest("article[id]") || workspace;

    // Clearing an assessment resets the whole record, not only the submitted form.
    const clearsAssessment = form.elements.action?.value === "clear-assessment";
    const submittedArea = clearsAssessment ? form.closest("article") : form;

    snapshot.anchorId = anchor.id;
    snapshot.top = anchor.getBoundingClientRect().top;
    snapshot.x = scrollX;
    snapshot.y = scrollY;
    snapshot.focusId = document.activeElement?.id;
    snapshot.scrollRegions = [...workspace.querySelectorAll("[data-preserve-scroll][id]")].map(
      (region) => [region.id, region.scrollLeft, region.scrollTop],
    );
    snapshot.open = [...workspace.querySelectorAll("details[open][id]")].map(
      (details) => details.id,
    );
    snapshot.fields = collectUnsavedFields(workspace, submittedArea, xhr.status < 400);
  }

  // --- Restoring the state after the swap ---

  function restoreScrollRegions(snapshot) {
    for (const [id, scrollLeft, scrollTop] of snapshot.scrollRegions) {
      const region = document.getElementById(id);
      if (region) {
        region.scrollLeft = scrollLeft;
        region.scrollTop = scrollTop;
      }
    }
  }

  // Keeps the anchor record at the same distance from the top of the window as before.
  function restoreViewport(snapshot) {
    restoreScrollRegions(snapshot);

    const anchor = document.getElementById(snapshot.anchorId);
    const top = anchor ? scrollY + anchor.getBoundingClientRect().top - snapshot.top : snapshot.y;

    window.scrollTo({
      left: snapshot.x,
      top,
      behavior: "instant",
    });
  }

  function reopenDetails(snapshot) {
    for (const id of snapshot.open) {
      const details = document.getElementById(id);
      if (details) {
        details.open = true;
      }
    }
  }

  function restoreUnsavedFields(snapshot) {
    for (const [id, value] of snapshot.fields) {
      const field = document.getElementById(id);
      if (field) {
        field.value = value;
      }
    }
  }

  // A control that is disabled after the swap hands the focus to the first link of its record.
  function restoreFocus(snapshot) {
    const focused = document.getElementById(snapshot.focusId);
    if (focused && !focused.disabled) {
      focused.focus({ preventScroll: true });
    } else {
      focused?.closest("article")?.querySelector("a")?.focus({ preventScroll: true });
    }
  }

  // --- HTMX request lifecycle ---

  document.addEventListener("htmx:beforeRequest", (event) => {
    const form = event.detail.elt.closest("form");

    // Sorting preserves its own viewport.
    if (form?.hasAttribute("data-sort")) {
      return;
    }

    const workspace = form?.closest("[data-preserve-viewport]");
    if (workspace) {
      requestSnapshots.set(event.detail.xhr, { form, workspace });
    }
  });

  document.addEventListener("htmx:beforeSwap", (event) => {
    const snapshot = requestSnapshots.get(event.detail.xhr);
    if (!snapshot || !event.detail.shouldSwap) {
      return;
    }

    captureSnapshot(snapshot, event.detail.xhr);
    document.documentElement.classList.add("workspace-swapping");
  });

  document.addEventListener("htmx:afterSwap", (event) => {
    const snapshot = requestSnapshots.get(event.detail.xhr);

    // Without captured details the response was not swapped in.
    if (!snapshot?.open) {
      return;
    }

    reopenDetails(snapshot);
    restoreUnsavedFields(snapshot);
    restoreFocus(snapshot);
    restoreViewport(snapshot);
  });

  document.addEventListener("htmx:afterSettle", (event) => {
    const snapshot = requestSnapshots.get(event.detail.xhr);
    if (!snapshot?.open) {
      return;
    }

    // Settling can move the content once more, so the position is applied a second time.
    restoreViewport(snapshot);
    requestSnapshots.delete(event.detail.xhr);
    requestAnimationFrame(() => document.documentElement.classList.remove("workspace-swapping"));
  });
})();
