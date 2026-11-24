// Behaviour shared by all pages: criterion type fields, the connection warning, the pending
// state of forms and the selection of cars to compare.

// The server answers a rejected form with these statuses and the content to show.
const validationFailureStatuses = [409, 422];

// The warning shown when a request fails or the browser goes offline.
const connection = document.getElementById("connection-error");

// Up to five cars can be marked in the overview and opened side by side.
const comparedCars = new Set();

// --- Criterion type: measurement fields ---

function updateMeasurementFields(typeSelect, measurementFields) {
  const isMeasurement = typeSelect.value === "Measurement";
  measurementFields.hidden = !isMeasurement;
  measurementFields.querySelectorAll("input").forEach((input) => {
    input.required = isMeasurement;
    input.disabled = !isMeasurement;
  });
}

function initializeFields(root = document) {
  root.querySelectorAll("[data-criterion-type]").forEach((typeSelect) => {
    if (typeSelect.dataset.initialized) {
      return;
    }

    typeSelect.dataset.initialized = "true";
    const measurementFields = typeSelect.closest("form").querySelector("[data-measurement-fields]");
    const update = () => updateMeasurementFields(typeSelect, measurementFields);

    typeSelect.addEventListener("change", update);
    update();
  });
}

initializeFields();

document.addEventListener("htmx:afterSwap", (event) => {
  initializeFields();

  // Sorting and editors restore the focus themselves; otherwise the new notice receives it.
  const isSortRequest = event.detail.requestConfig?.elt?.matches("[data-sort]");
  const swappedInEditor = event.detail.target?.querySelector("[data-editor]");
  if (!isSortRequest && !swappedInEditor) {
    document.querySelector(".notice[tabindex]")?.focus({ preventScroll: true });
  }
});

// --- Rejected forms ---

document.addEventListener("htmx:beforeSwap", (event) => {
  // HTMX discards error responses by default, but these carry the form with its messages.
  if (validationFailureStatuses.includes(event.detail.xhr.status)) {
    event.detail.shouldSwap = true;
    event.detail.isError = false;
  }
});

// --- Connection warning ---

function showConnection(offline = false) {
  connection.querySelector("[data-connection-message]").textContent = offline
    ? connection.dataset.offline
    : connection.dataset.failure;
  connection.hidden = false;
}

function hideConnection() {
  connection.hidden = true;
}

function requestFailed(event) {
  // Editors and operation forms report failures next to the form.
  const reportsOwnFailures = event.detail.elt?.closest(
    "[data-editor], [data-request-form], [data-operation]",
  );
  if (!reportsOwnFailures) {
    showConnection(!navigator.onLine);
  }
}

document.addEventListener("htmx:sendError", requestFailed);
document.addEventListener("htmx:responseError", requestFailed);
window.addEventListener("offline", () => showConnection(true));
window.addEventListener("online", hideConnection);
connection.querySelector("[data-dismiss-connection]").addEventListener("click", hideConnection);

// --- Pending state of forms ---

function showRequestStatus(form, pending) {
  let status = form.querySelector(".request-status");
  if (!status && pending) {
    status = document.createElement("p");
    status.className = "request-status";
    status.setAttribute("role", "status");
    form.append(status);
  }

  if (status) {
    status.textContent = pending ? document.body.dataset.requestPending : "";
    status.hidden = !pending;
  }
}

function requestPending(form, pending) {
  if (!form) {
    return;
  }

  form.toggleAttribute("data-pending", pending);
  form.setAttribute("aria-busy", String(pending));

  // Sort forms are submitted by dragging and keyboard shortcuts and show no status text.
  if (form.hasAttribute("data-sort")) {
    return;
  }

  showRequestStatus(form, pending);
}

document.addEventListener("htmx:beforeRequest", (event) =>
  requestPending(event.detail.elt.closest("form"), true),
);
document.addEventListener("htmx:afterRequest", (event) =>
  requestPending(event.detail.elt.closest("form"), false),
);
document.addEventListener("form:pending", (event) =>
  requestPending(event.target.closest("form"), event.detail),
);

// --- Cars selected for comparison ---

// With restore, the remembered selection is applied to checkboxes that were just swapped in;
// otherwise the checkboxes are the source and the selection is read from them.
function updateCompareSelection(restore = false) {
  const form = document.querySelector("[data-compare-form]");
  if (!form) {
    return;
  }

  const checkboxes = [...document.querySelectorAll("[data-compare-select]")];
  if (restore) {
    checkboxes.forEach((checkbox) => {
      checkbox.checked = comparedCars.has(checkbox.value);
    });
  }

  comparedCars.clear();
  checkboxes
    .filter((checkbox) => checkbox.checked)
    .forEach((checkbox) => comparedCars.add(checkbox.value));

  // Once the maximum is reached, only the selected cars stay changeable.
  const maximum = Number(form.dataset.compareMaximum);
  const isFull = comparedCars.size >= maximum;
  checkboxes.forEach((checkbox) => {
    checkbox.disabled = isFull && !checkbox.checked;
  });

  const compareButton = form.querySelector("[data-compare-label]");
  compareButton.querySelector("span").textContent =
    `${compareButton.dataset.compareLabel} (${comparedCars.size}/${maximum})`;
}

document.addEventListener("change", (event) => {
  if (event.target.matches("[data-compare-select]")) {
    updateCompareSelection();
  }
});
document.addEventListener("htmx:afterSwap", () => updateCompareSelection(true));
window.addEventListener("pageshow", () => updateCompareSelection());
updateCompareSelection();
