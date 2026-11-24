// Technical data form: values that follow from other fields (weights, engine power) are offered
// as a hint and placeholder as long as the field itself is empty.
(() => {
  const KILOWATTS_PER_PS = 0.73549875;
  const numberFormat = new Intl.NumberFormat(document.documentElement.lang, {
    maximumFractionDigits: 2,
  });

  // --- Reading the entered values ---

  function collectInputsByField(container) {
    const entries = [...container.querySelectorAll("[data-spec-field]")].map((input) => [
      input.dataset.specField,
      input,
    ]);
    return Object.fromEntries(entries);
  }

  // Returns the entered number, or null when the field is missing, empty or not a number.
  function enteredNumber(inputs, fieldName) {
    const input = inputs[fieldName];
    if (input?.value === "") {
      return null;
    }

    return Number.isFinite(input?.valueAsNumber) ? input.valueAsNumber : null;
  }

  // --- Deriving values from the other fields ---

  function deriveValues(inputs) {
    function fromBoth(firstField, secondField, operation) {
      const first = enteredNumber(inputs, firstField);
      const second = enteredNumber(inputs, secondField);
      return first !== null && second !== null ? operation(first, second) : null;
    }

    const powerPs = enteredNumber(inputs, "PowerPs");
    const powerKw = enteredNumber(inputs, "PowerKw");

    return {
      KerbWeightKg: fromBoth("GrossWeightKg", "PayloadKg", (gross, payload) => gross - payload),
      GrossWeightKg: fromBoth("KerbWeightKg", "PayloadKg", (kerb, payload) => kerb + payload),
      PayloadKg: fromBoth("GrossWeightKg", "KerbWeightKg", (gross, kerb) => gross - kerb),
      PowerKw: powerPs === null ? null : powerPs * KILOWATTS_PER_PS,
      PowerPs: powerKw === null ? null : powerKw / KILOWATTS_PER_PS,
    };
  }

  // A payload of zero is plausible; every other derived value has to be positive.
  function isPlausible(fieldName, value) {
    if (value === null) {
      return false;
    }

    return fieldName === "PayloadKg" ? value >= 0 : value > 0;
  }

  // --- Showing the derived values ---

  function updateCalculatedValues(container) {
    const inputs = collectInputsByField(container);
    const derivedValues = deriveValues(inputs);

    for (const output of container.querySelectorAll("[data-calculated]")) {
      const fieldName = output.dataset.calculated;
      const input = inputs[fieldName];

      // An entered value always wins over the derived one.
      const isEmpty = enteredNumber(inputs, fieldName) === null;
      const value = isEmpty ? derivedValues[fieldName] : null;
      const showValue = isPlausible(fieldName, value);

      output.textContent = showValue
        ? container.dataset.calculatedLabel.replace(
            "{0}",
            `${numberFormat.format(value)} ${input.dataset.unit}`,
          )
        : "";
      input.placeholder = showValue ? numberFormat.format(value) : "";
    }
  }

  function updateAllForms() {
    document.querySelectorAll("[data-specifications]").forEach(updateCalculatedValues);
  }

  // A field inside a collapsed section cannot show its validation message.
  function openEnclosingDetails(field) {
    for (let parent = field.parentElement; parent; parent = parent.parentElement) {
      if (parent.tagName === "DETAILS") {
        parent.open = true;
      }
    }
  }

  // --- Event wiring ---

  document.addEventListener("input", (event) => {
    const container = event.target.closest("[data-specifications]");
    if (container) {
      updateCalculatedValues(container);
    }
  });

  // "invalid" does not bubble, so it is observed in the capture phase.
  document.addEventListener(
    "invalid",
    (event) => {
      if (!event.target.closest("[data-specifications]")) {
        return;
      }

      openEnclosingDetails(event.target);
    },
    true,
  );

  document.addEventListener("htmx:afterSettle", updateAllForms);
  updateAllForms();
})();
