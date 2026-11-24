// Cost form: the selected frequency decides which of the amount, direction and end fields apply.
document.querySelectorAll("[data-cost-form]").forEach((form) => {
  const frequencySelect = form.querySelector("[data-cost-frequency]");
  const amountField = form.querySelector("[data-cost-amount]");
  const endField = form.querySelector("[data-cost-end]");
  const directionSelect = form.querySelector("[data-cost-direction]");

  function updateFieldsForFrequency() {
    // An included cost has no amount of its own and is always recorded as an expense.
    const isIncluded = frequencySelect.value === "Included";
    amountField.hidden = isIncluded;
    amountField.querySelector("input").disabled = isIncluded;
    directionSelect.disabled = isIncluded;
    if (isIncluded) {
      directionSelect.value = "Expense";
    }

    // A one-off cost has no end date.
    endField.hidden = frequencySelect.value === "Once";
    endField.querySelector("input").disabled = endField.hidden;
  }

  frequencySelect.addEventListener("change", updateFieldsForFrequency);
  updateFieldsForFrequency();
});
