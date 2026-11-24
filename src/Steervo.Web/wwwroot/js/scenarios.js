// Scenario pages: the edit form shows only the inputs of the selected mode; the comparison keeps
// its source link on the selected revision and scrolls to the scenario it was opened for.

// --- Scenario form ---

document.querySelectorAll("[data-scenario-form]").forEach((form) => {
  const modeSelect = form.querySelector("[data-scenario-mode]");

  function appliesToSelectedMode(inputGroup) {
    const modes = inputGroup.dataset.scenarioModes;
    return modes === "all" || modes.split(" ").includes(modeSelect.value);
  }

  function updateInputGroups() {
    form.querySelectorAll("[data-scenario-modes]").forEach((inputGroup) => {
      inputGroup.hidden = !appliesToSelectedMode(inputGroup);
      inputGroup.querySelectorAll("input").forEach((input) => {
        input.disabled = inputGroup.hidden;
      });
    });
  }

  // A section disappears once none of its input groups applies to the selected mode.
  function updateSections() {
    form.querySelectorAll("[data-scenario-group]").forEach((section) => {
      const inputGroups = [...section.querySelectorAll("[data-scenario-modes]")];
      section.hidden = inputGroups.every((inputGroup) => inputGroup.hidden);
    });
  }

  function updateForSelectedMode() {
    updateInputGroups();
    updateSections();
  }

  modeSelect.addEventListener("change", updateForSelectedMode);
  updateForSelectedMode();
});

// --- Comparison: the source link follows the selected revision ---

const revision = document.querySelector("[data-scenario-revision]");
const source = document.querySelector("[data-scenario-source]");

revision?.addEventListener("change", () => {
  const sourceUrl = new URL(source.href);
  sourceUrl.searchParams.set("revision", revision.value);
  source.href = sourceUrl.href;
});

// --- Comparison: scroll to the scenario the page was opened for ---

const comparison = document.getElementById("scenario-comparison-scroll");

if (comparison?.dataset.focusScenario) {
  const focusedColumn = [...comparison.querySelectorAll("[data-scenario-column]")].find(
    (cell) => cell.dataset.scenarioColumn === comparison.dataset.focusScenario,
  );

  if (focusedColumn) {
    requestAnimationFrame(() => {
      // Leave room for the leading label column so the scenario lands right beside it.
      const labelColumnWidth = comparison.querySelector("th").offsetWidth;
      comparison.scrollLeft = Math.max(0, focusedColumn.offsetLeft - labelColumnWidth);
    });
  }
}
