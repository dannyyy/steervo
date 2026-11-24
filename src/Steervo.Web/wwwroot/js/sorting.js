// Reordering of criteria and criterion groups in the evaluation workspace, by dragging a handle
// with mouse, pen or finger and with the arrow keys. Every move is posted through a sort form
// (form[data-sort]); the server answers with the re-rendered workspace.
(() => {
  // The pointer has to travel this far (in pixels) before a press on the handle becomes a drag.
  const DRAG_START_DISTANCE = 6;

  // Within this distance (in pixels) from the top or bottom of the window the page scrolls.
  const SCROLL_EDGE = 64;

  // Scroll speed at the very edge, in pixels per millisecond.
  const SCROLL_SPEED = 0.8;

  // Longest time (in milliseconds) one frame may count for, so a stalled frame cannot jump.
  const LONGEST_FRAME = 32;

  // What was captured for each running sort request, keyed by its XMLHttpRequest.
  const sortRequests = new WeakMap();

  // The drag in progress, from pressing a handle until release or cancellation; otherwise empty.
  let drag;

  if (!window.htmx || !window.PointerEvent) {
    return;
  }

  // Lets the stylesheet show the drag handles only when sorting actually works.
  document.documentElement.classList.add("sorting-enabled");

  // --- Preserving the view across the workspace swap ---
  //
  // Replacing the workspace can make the browser anchor the viewport to the footer.
  // Preserve the list's viewport position and the user's open editors across sorting.

  function originalValue(field) {
    if (field instanceof HTMLSelectElement) {
      const defaultOption = [...field.options].find((option) => option.defaultSelected);
      return defaultOption?.value ?? field.options[0]?.value;
    }

    return field.defaultValue;
  }

  // Fields of guarded editors are restored by the editor script.
  function collectUnsavedFields(workspace) {
    return [...workspace.querySelectorAll("input[id], select[id], textarea[id]")]
      .filter((field) => {
        if (field.closest("form[data-guard-draft]")) {
          return false;
        }

        return field.value !== originalValue(field);
      })
      .map((field) => [field.id, field.value]);
  }

  function restoreViewport(snapshot) {
    const criteriaList = document.querySelector("#evaluation-workspace .criteria-main");
    if (criteriaList) {
      window.scrollTo({
        left: snapshot.x,
        top: scrollY + criteriaList.getBoundingClientRect().top - snapshot.top,
        behavior: "instant",
      });
    }
  }

  function restoreUnsavedState(snapshot) {
    for (const id of snapshot.open) {
      const details = document.getElementById(id);
      if (details) {
        details.open = true;
      }
    }

    for (const [id, value] of snapshot.fields) {
      const field = document.getElementById(id);
      if (field) {
        field.value = value;
      }
    }

    document
      .querySelectorAll("[data-criterion-type]")
      .forEach((typeSelect) => typeSelect.dispatchEvent(new Event("change")));
  }

  // A control that is disabled after the move (first or last place) hands the focus to the
  // drag handle of its item.
  function restoreFocus(snapshot) {
    const control = document.getElementById(snapshot.focus);
    if (control && !control.disabled) {
      control.focus({ preventScroll: true });
    } else {
      control
        ?.closest("[data-sort-kind]")
        ?.querySelector("[data-drag-handle]")
        ?.focus({ preventScroll: true });
    }
  }

  document.addEventListener("htmx:beforeRequest", (event) => {
    const form = event.detail.elt;
    if (!form.matches("form[data-sort]")) {
      return;
    }

    sortRequests.set(event.detail.xhr, {
      focus: form.dataset.focusId || document.activeElement?.id,
    });
  });

  document.addEventListener("htmx:beforeSwap", (event) => {
    const snapshot = sortRequests.get(event.detail.xhr);
    if (!snapshot || !event.detail.shouldSwap) {
      return;
    }

    const workspace = document.getElementById("evaluation-workspace");
    snapshot.x = scrollX;
    snapshot.top = workspace.querySelector(".criteria-main").getBoundingClientRect().top;
    snapshot.open = [...workspace.querySelectorAll("details[open][id]")].map(
      (details) => details.id,
    );
    snapshot.fields = collectUnsavedFields(workspace);
    document.documentElement.classList.add("workspace-swapping");
  });

  document.addEventListener("htmx:afterSwap", (event) => {
    const snapshot = sortRequests.get(event.detail.xhr);

    // Without captured details the response was not swapped in.
    if (!snapshot?.open) {
      return;
    }

    // A stale response must show the current server state, not overwrite it with old fields.
    if (event.detail.xhr.status < 400) {
      restoreUnsavedState(snapshot);
    }

    restoreFocus(snapshot);
    restoreViewport(snapshot);
  });

  document.addEventListener("htmx:afterSettle", (event) => {
    const snapshot = sortRequests.get(event.detail.xhr);
    if (!snapshot?.open) {
      return;
    }

    // Settling can move the content once more, so the position is applied a second time.
    restoreViewport(snapshot);
    sortRequests.delete(event.detail.xhr);
    requestAnimationFrame(() => document.documentElement.classList.remove("workspace-swapping"));
  });

  // --- Shared helpers ---

  // No new move starts while the workspace is still loading or settling a previous one.
  function isWorkspaceBusy() {
    return !!document.querySelector(
      "#evaluation-workspace.htmx-settling, #evaluation-workspace .htmx-request",
    );
  }

  // Criteria belong to a group; groups are ordered within their scope and have no group id.
  function groupIdOf(kind, element) {
    return kind === "criterion" ? element.closest(".criterion-group").dataset.groupId : "";
  }

  // --- Dragging: finding the drop position ---

  // Lists show one kind of criterion at a time, so the target is derived from the stored positions of the neighbours.
  function targetPosition(item, sameList, next, previous, count) {
    const ownPosition = Number(item.dataset.position);

    // Taking the item out of its place moves every later neighbour of the same list up by one.
    function shiftAfterRemoval(neighbour) {
      return sameList && ownPosition < Number(neighbour.dataset.position) ? 1 : 0;
    }

    if (next) {
      return Number(next.dataset.position) - shiftAfterRemoval(next);
    }

    if (previous) {
      return Number(previous.dataset.position) + 1 - shiftAfterRemoval(previous);
    }

    return sameList ? ownPosition : count;
  }

  // The criterion list under the pointer, if the dragged criterion may be dropped into it.
  function criterionListAt(hitElement, item) {
    const group = hitElement?.closest(".criterion-group");
    const list = group?.querySelector(".criterion-list");
    if (!list) {
      return list;
    }

    // Criteria stay among their own kind; a group without criteria accepts both.
    const isOtherScope = group.closest(".criteria-scope") !== item.closest(".criteria-scope");
    if (isOtherScope && list.dataset.count !== "0") {
      return null;
    }

    return list;
  }

  // Groups can only be moved within their own scope.
  function groupScopeAt(hitElement, item) {
    const scope = item.closest(".criteria-scope");
    return hitElement?.closest(".criteria-scope") === scope ? scope : null;
  }

  // The items the dragged one can be placed between, in document order and without itself.
  function dropSiblings(kind, container, item) {
    if (!container) {
      return [];
    }

    if (kind === "criterion") {
      return [...container.children].filter(
        (child) => child.matches(".criterion") && child !== item,
      );
    }

    return [...container.querySelectorAll(':scope > [data-sort-kind="group"]')].filter(
      (child) => child !== item,
    );
  }

  // The first sibling whose vertical centre lies below the pointer; past the end if there is none.
  function insertionIndex(siblings, pointerY) {
    const index = siblings.findIndex((sibling) => {
      const bounds = sibling.getBoundingClientRect();
      return pointerY < bounds.top + bounds.height / 2;
    });

    return index === -1 ? siblings.length : index;
  }

  // Draws the marker line at the gap: above the next item, below the previous one, or in the
  // middle of an empty container.
  function placeMarker(marker, container, next, previous) {
    const bounds = container.getBoundingClientRect();
    let top;
    if (next) {
      top = next.getBoundingClientRect().top;
    } else if (previous) {
      top = previous.getBoundingClientRect().bottom;
    } else {
      top = bounds.top + bounds.height / 2;
    }

    Object.assign(marker.style, {
      left: `${bounds.left}px`,
      width: `${bounds.width}px`,
      top: `${top - 1}px`,
    });
  }

  function locateDestination() {
    const { item, kind, x, y, marker } = drag;
    const hitElement = document.elementFromPoint(x, y);
    const container =
      kind === "criterion" ? criterionListAt(hitElement, item) : groupScopeAt(hitElement, item);
    const siblings = dropSiblings(kind, container, item);

    marker.hidden = !container;
    drag.destination = null;
    if (!container) {
      return;
    }

    const index = insertionIndex(siblings, y);
    const next = siblings[index];
    const previous = siblings[index - 1];
    placeMarker(marker, container, next, previous);

    const groupId = groupIdOf(kind, container);
    drag.destination = {
      groupId,
      position: targetPosition(
        item,
        groupId === drag.groupId,
        next,
        previous,
        Number(container.dataset.count || 0),
      ),
    };
  }

  // --- Dragging: scrolling near the window edges ---

  // Between -1 (up) and 1 (down): the closer the pointer is to the edge, the faster.
  function scrollDirection(pointerY) {
    if (pointerY < SCROLL_EDGE) {
      return -Math.min(1, (SCROLL_EDGE - pointerY) / SCROLL_EDGE);
    }

    if (pointerY > innerHeight - SCROLL_EDGE) {
      return Math.min(1, (pointerY - innerHeight + SCROLL_EDGE) / SCROLL_EDGE);
    }

    return 0;
  }

  function autoScroll(time) {
    if (!drag?.active) {
      return;
    }

    const elapsed = Math.min(time - (drag.time || time), LONGEST_FRAME);
    drag.time = time;

    const direction = scrollDirection(drag.y);
    if (direction) {
      window.scrollBy({ top: direction * elapsed * SCROLL_SPEED, behavior: "instant" });
    }

    // The page may have moved under a resting pointer.
    locateDestination();
    drag.frame = requestAnimationFrame(autoScroll);
  }

  // --- Dragging: start, move, finish ---

  function pressHandle(handle, event) {
    const item = handle.closest("[data-sort-kind]");
    const kind = item.dataset.sortKind;
    drag = {
      handle,
      item,
      kind,
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      x: event.clientX,
      y: event.clientY,
      groupId: groupIdOf(kind, item),
      position: Number(item.dataset.position),
    };
  }

  function activateDrag() {
    drag.active = true;
    drag.marker = document.createElement("div");
    drag.marker.className = "sorting-marker";
    drag.marker.setAttribute("aria-hidden", "true");
    document.body.append(drag.marker);
    drag.item.classList.add("sorting-source");
    document.documentElement.classList.add("sorting-active");
    drag.frame = requestAnimationFrame(autoScroll);
  }

  function submitPlacement(finishedDrag) {
    const { groupId, position } = finishedDrag.destination;
    const item = finishedDrag.item;
    const form = document.getElementById("reorder-form");
    form.elements.action.value = `place-${finishedDrag.kind}`;
    form.elements.itemId.value = item.dataset.criterionId || item.dataset.groupId;
    form.elements.groupId.value = groupId;
    form.elements.position.value = position;
    form.dataset.focusId = finishedDrag.handle.id;
    form.requestSubmit();
  }

  function finishDrag(cancelled = false) {
    if (!drag) {
      return;
    }

    // Clear the state first: the cleanup below raises events (lostpointercapture) whose
    // handlers would otherwise finish the same drag again.
    const finishedDrag = drag;
    drag = null;

    cancelAnimationFrame(finishedDrag.frame);
    finishedDrag.marker?.remove();
    finishedDrag.item.classList.remove("sorting-source");
    document.documentElement.classList.remove("sorting-active");
    if (finishedDrag.handle.hasPointerCapture(finishedDrag.pointerId)) {
      finishedDrag.handle.releasePointerCapture(finishedDrag.pointerId);
    }

    if (cancelled || !finishedDrag.active || !finishedDrag.destination) {
      return;
    }

    const { groupId, position } = finishedDrag.destination;
    const isUnchanged = groupId === finishedDrag.groupId && position === finishedDrag.position;
    if (isUnchanged) {
      return;
    }

    submitPlacement(finishedDrag);
  }

  function isDragPointer(event) {
    return drag && event.pointerId === drag.pointerId;
  }

  document.addEventListener("pointerdown", (event) => {
    const handle = event.target.closest("[data-drag-handle]");
    const isPrimaryPress = event.button === 0 && event.isPrimary;
    if (!handle || !isPrimaryPress || isWorkspaceBusy() || drag) {
      return;
    }

    pressHandle(handle, event);
    handle.focus({ preventScroll: true });
    handle.setPointerCapture(event.pointerId);
    event.preventDefault();
  });

  document.addEventListener("pointermove", (event) => {
    if (!drag || event.pointerId !== drag.pointerId) {
      return;
    }

    drag.x = event.clientX;
    drag.y = event.clientY;

    const distance = Math.hypot(drag.x - drag.startX, drag.y - drag.startY);
    if (!drag.active && distance >= DRAG_START_DISTANCE) {
      activateDrag();
    }

    if (drag.active) {
      event.preventDefault();
      locateDestination();
    }
  });

  document.addEventListener("pointerup", (event) => {
    if (isDragPointer(event)) {
      finishDrag();
    }
  });

  document.addEventListener("pointercancel", (event) => {
    if (isDragPointer(event)) {
      finishDrag(true);
    }
  });

  document.addEventListener("lostpointercapture", (event) => {
    if (isDragPointer(event)) {
      finishDrag(true);
    }
  });

  window.addEventListener("blur", () => finishDrag(true));

  // --- Keyboard: Escape cancels a drag, the arrow keys move the focused item ---

  // The item's own sort form for one step up (-1) or down (1).
  function stepForm(item, direction) {
    return [...item.querySelectorAll("form[data-sort]")].find(
      (candidate) =>
        candidate.elements.action.value === `place-${item.dataset.sortKind}` &&
        candidate.dataset.direction === direction,
    );
  }

  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && drag) {
      event.preventDefault();
      finishDrag(true);
      return;
    }

    const handle = event.target.closest("[data-drag-handle]");
    if (!handle || !["ArrowUp", "ArrowDown"].includes(event.key)) {
      return;
    }

    // The arrow keys never scroll the page while a handle has the focus.
    event.preventDefault();
    if (isWorkspaceBusy() || drag) {
      return;
    }

    const item = handle.closest("[data-sort-kind]");
    const form = stepForm(item, event.key === "ArrowUp" ? "-1" : "1");
    if (form && !form.querySelector("button").disabled) {
      form.dataset.focusId = handle.id;
      form.requestSubmit();
    }
  });
})();
