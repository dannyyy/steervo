// Offer pages: the comparison scrolls to the offer it was opened for; the offer form shows the
// fields that apply to each position and keeps the currency labels and totals up to date.
(() => {
  const FALLBACK_LOCALE = "de";

  // --- Comparison: scroll to the offer the page was opened for ---

  const comparison = document.getElementById("offer-comparison-scroll");

  if (comparison?.dataset.focusOffer) {
    const focusedColumn = [...comparison.querySelectorAll("[data-offer-column]")].find(
      (column) => column.dataset.offerColumn === comparison.dataset.focusOffer,
    );

    if (focusedColumn) {
      requestAnimationFrame(() => {
        // Leave room for the leading label column so the offer lands right beside it.
        const labelColumnWidth = comparison.querySelector("th").offsetWidth;
        comparison.scrollLeft = Math.max(0, focusedColumn.offsetLeft - labelColumnWidth);
      });
    }
  }

  // --- Offer form: reading the header fields ---

  function offerKind(form) {
    return form.querySelector("#offer-kind").value;
  }

  // Leasing and financing run over a contract, so positions can be part of the financed sum.
  function isContractKind(kind) {
    return kind === "Leasing" || kind === "Financing";
  }

  function offerCurrency(form) {
    return form.querySelector("#offer-currency").value.trim().toUpperCase();
  }

  // --- Offer form: fields of one position ---

  function setFieldsVisible(line, selector, visible) {
    for (const container of line.querySelectorAll(selector)) {
      container.hidden = !visible;
      container.querySelectorAll("input, select").forEach((input) => {
        input.disabled = !visible;
      });
    }
  }

  function updateLine(line) {
    const isBenefit = line.querySelector("[data-line-kind]").value === "Benefit";
    const hasAmount = line.querySelector("[data-line-price]").value === "Amount";
    const isCostSaving = line.querySelector("[data-line-effect]").value === "CostSaving";
    const isIncluded = line.querySelector("[data-line-price]").value === "Included";
    const isContract = isContractKind(offerKind(line.closest("form")));

    // The checkbox keeps its state while hidden, so switching the offer kind back restores it.
    line.querySelector("[data-line-financed]").hidden = !(isContract && hasAmount);

    setFieldsVisible(line, "[data-line-amount]", hasAmount);
    setFieldsVisible(line, "[data-line-value]", isIncluded);
    setFieldsVisible(line, "[data-line-benefit]", isBenefit);
    setFieldsVisible(line, "[data-line-category]", isBenefit && isCostSaving);

    line.querySelector("[data-line-amount] input").required = hasAmount;
  }

  function updateAllLines(form) {
    form.querySelectorAll("[data-offer-line]").forEach(updateLine);
  }

  // --- Offer form: currency labels ---

  function updateCurrency(form) {
    const currency = offerCurrency(form);
    form.querySelectorAll("[data-offer-currency-label]").forEach((label) => {
      label.textContent = currency;
    });
  }

  // --- Offer form: totals ---

  // Adds up the positions the way the offer is priced: amounts before discounts, the discounts,
  // the value of included benefits and the part that goes into the contract.
  function sumLines(form) {
    const sums = {
      beforeDiscounts: 0,
      discounts: 0,
      includedValue: 0,
      financed: 0,
      pricedLines: 0,
      vehiclePriceOpen: false,
    };

    for (const line of form.querySelectorAll("[data-offer-line]")) {
      const price = line.querySelector("[data-line-price]").value;
      const lineKind = line.querySelector("[data-line-kind]").value;

      if (price === "Unknown" && lineKind === "Vehicle") {
        sums.vehiclePriceOpen = true;
      }

      if (price === "Included") {
        sums.includedValue += Number(line.querySelector("[data-line-value] input").value) || 0;
        continue;
      }

      const amountInput = line.querySelector("[data-line-amount-input]");
      const amount = Number(amountInput.value);
      const hasUsableAmount =
        price === "Amount" && Number.isFinite(amount) && amountInput.value !== "";
      if (!hasUsableAmount) {
        continue;
      }

      const isDiscount = lineKind === "Discount";
      sums.pricedLines++;
      if (isDiscount) {
        sums.discounts += amount;
      } else {
        sums.beforeDiscounts += amount;
      }

      if (line.querySelector("[data-line-financed-input]").checked) {
        sums.financed += isDiscount ? -amount : amount;
      }
    }

    return sums;
  }

  function formatMoney(value, currency) {
    const sign = value < 0 ? "−" : "";
    const amount = new Intl.NumberFormat(document.documentElement.lang || FALLBACK_LOCALE, {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(Math.abs(value));

    return sign + amount + " " + currency;
  }

  // The concession in percent of the price before discounts, with one decimal place.
  function concessionShare(concession, beforeDiscounts) {
    if (beforeDiscounts > 0 && concession > 0) {
      return " · " + Math.round((concession * 1000) / beforeDiscounts) / 10 + " %";
    }

    return "";
  }

  function updateTotals(form) {
    const totals = form.querySelector("[data-offer-totals]");
    if (!totals) {
      return;
    }

    const kind = offerKind(form);
    const isContract = isContractKind(kind);
    const sums = sumLines(form);
    const currency = offerCurrency(form);
    const total = sums.beforeDiscounts - sums.discounts;
    const concession = sums.discounts + sums.includedValue;

    function setTotal(name, text) {
      totals.querySelector(`[data-total="${name}"]`).textContent = text;
    }

    // Without the vehicle's own price the sum of the other positions is not a total.
    totals.hidden = sums.vehiclePriceOpen || (sums.pricedLines === 0 && concession === 0);

    setTotal("total", sums.pricedLines ? formatMoney(total, currency) : "");
    setTotal("financed", formatMoney(sums.financed, currency));
    setTotal("handover", formatMoney(total - sums.financed, currency));
    setTotal(
      "concession",
      formatMoney(concession, currency) + concessionShare(concession, sums.beforeDiscounts),
    );

    totals.querySelectorAll("[data-total-contract]").forEach((row) => {
      row.hidden = !isContract || !sums.pricedLines;
    });
    totals.querySelector("[data-total-concession]").hidden = concession <= 0;

    form.querySelectorAll("[data-financed-label]").forEach((label) => {
      label.textContent =
        kind === "Financing" ? totals.dataset.labelFinancing : totals.dataset.labelLeasing;
    });
  }

  // --- Offer form: adding, removing and moving positions ---

  function addLine(form) {
    const template = form.querySelector("[data-offer-line-template]");
    const container = document.createElement("template");

    // Each new position gets a unique index for the names and ids of its fields.
    container.innerHTML = template.innerHTML.replaceAll("__index__", crypto.randomUUID());
    const line = container.content.firstElementChild;

    form.querySelector("[data-offer-lines]").append(line);
    updateLine(line);
    updateCurrency(form);
    updateTotals(form);
    line.querySelector("input:not([type=hidden])").focus();
  }

  function handleLineButton(form, line, button) {
    if (button.hasAttribute("data-offer-line-remove")) {
      line.remove();
      form.querySelector("[data-offer-line-add]").focus({ preventScroll: true });
    }

    if (button.hasAttribute("data-offer-line-up") && line.previousElementSibling) {
      line.previousElementSibling.before(line);
      button.focus({ preventScroll: true });
    }

    if (button.hasAttribute("data-offer-line-down") && line.nextElementSibling) {
      line.nextElementSibling.after(line);
      button.focus({ preventScroll: true });
    }

    updateTotals(form);
  }

  // An empty vehicle description is filled in from the selected car.
  function suggestVehicleDescription(form, candidateSelect) {
    const description = form.querySelector("#offer-VehicleDescription");
    if (description.value === "") {
      description.value = candidateSelect.selectedOptions[0]?.dataset.description || "";
    }
  }

  // --- Offer form: event wiring ---

  function initializeOfferForm(form) {
    updateAllLines(form);
    updateCurrency(form);
    updateTotals(form);

    form.addEventListener("input", (event) => {
      if (event.target.id === "offer-currency") {
        updateCurrency(form);
      }

      updateTotals(form);
    });

    form.addEventListener("change", (event) => {
      const line = event.target.closest("[data-offer-line]");
      if (line) {
        updateLine(line);
      }

      // The offer kind decides whether positions can be financed.
      if (event.target.id === "offer-kind") {
        updateAllLines(form);
      }

      updateTotals(form);

      if (event.target.matches("[data-offer-candidate]")) {
        suggestVehicleDescription(form, event.target);
      }
    });

    form.addEventListener("click", (event) => {
      const button = event.target.closest("button");
      if (!button) {
        return;
      }

      if (button.hasAttribute("data-offer-line-add")) {
        addLine(form);
        return;
      }

      const line = button.closest("[data-offer-line]");
      if (!line) {
        return;
      }

      handleLineButton(form, line, button);
    });
  }

  for (const form of document.querySelectorAll("[data-offer-form]")) {
    initializeOfferForm(form);
  }
})();
