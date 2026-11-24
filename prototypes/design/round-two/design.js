// Design prototype, round two: three directions (Studio, Canvas, Focus) with photographs and
// stronger colours. Frozen reference; runs on its own. Everything is rendered from sample data
// into #app; changes persist only in this browser tab's session storage.

// ---------------------------------------------------------------------------
// Fixed values
// ---------------------------------------------------------------------------

const DEMO_STORAGE_KEY = "steervo-design-demo-v2";
const TOAST_DURATION_MS = 3200;
// Gallery previews embed the prototype in an iframe; a fixed height keeps them comparable.
const EMBEDDED_MIN_HEIGHT = "960px";
const EVALUATION_NAME = "Familienauto 2026";
// Unknown is a state of its own: never zero and never "no".
const UNKNOWN_LABEL = "Noch offen";
const NO_NOTE_LABEL = "Noch keine Notiz erfasst.";
const REQUIRED_PRIORITY = "Muss";
const FAVOURITE_STATUS = "Favorit";
const SHORTLIST_STATUS = "In Auswahl";
const BATTERY_SPEC_INDEX = 3;
// Only the sample cars come with an illustrative photograph.
const PHOTOGRAPHED_CAR_IDS = ["enyaq", "id7", "i5"];
const ASSESSMENT_SOURCES = [
  UNKNOWN_LABEL,
  "Herstellerangabe",
  "Offerte",
  "Probefahrt",
  "Eigene Recherche",
];
const BOOLEAN_CHOICES = ["Ja", "Nein"];
const RATING_CHOICES = ["1", "2", "3", "4", "5"];
const COMPARED_SPEC_LABELS = ["Länge", "Breite ohne Spiegel", "Sitzplätze", "Batterie (netto)"];
const SPEC_FIELD_LABELS = [
  "Länge (mm)",
  "Breite ohne Spiegel (mm)",
  "Sitzplätze",
  "Batterie netto (kWh)",
];
// These actions open the side sheet; the editor kind is the action without "edit-".
const SHEET_EDITOR_ACTIONS = ["add", "edit-car", "edit-notes", "edit-specs"];
const FOCUSABLE_SELECTOR = "button,input,select,textarea,a[href]";
const HTML_ESCAPES = { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" };

const icons = {
  mark: '<path d="m7 3-5 18h6l5-18H7Zm10 0-5 18h5l5-18h-5Z" fill="currentColor" stroke="none"/>',
  car: '<path d="m5 7 2-4h10l2 4M3 10l2-3h14l2 3v8H3v-8Zm3 8v3m12-3v3M3 13h18M6 10h1m10 0h1"/>',
  compare:
    '<rect x="3" y="4" width="7" height="16" rx="1"/><rect x="14" y="4" width="7" height="16" rx="1"/><path d="M6 8h1m10 0h1M6 12h1m10 0h1"/>',
  list: '<path d="m3 6 1 1 2-2m3 1h12M3 12h3m3 0h12M3 18h3m3 0h12"/>',
  wallet:
    '<rect x="3" y="5" width="18" height="15" rx="2"/><path d="M3 7V4a1 1 0 0 1 1-1h13v2m4 6h-6v5h6m-3-2.5h.01"/>',
  down: '<path d="m7 10 5 5 5-5"/>',
  right: '<path d="m9 5 7 7-7 7"/>',
  left: '<path d="m15 5-7 7 7 7"/>',
  plus: '<path d="M12 5v14M5 12h14"/>',
  close: '<path d="m6 6 12 12M18 6 6 18"/>',
  edit: '<path d="m14 5 5 5M4 20l5-1L21 7l-5-5L4 14v6Z"/>',
  check: '<path d="m5 12 4 4L19 6"/>',
  info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v6m0-10h.01"/>',
  star: '<path d="m12 3 2.7 5.6 6.2.9-4.5 4.4 1.1 6.1-5.5-2.9L6.5 20l1-6.1L3 9.5l6.2-.9L12 3Z"/>',
  arrow: '<path d="M4 12h16m-6-6 6 6-6 6"/>',
  file: '<path d="M14 3H5v18h14V8l-5-5Zm0 0v5h5M8 13h8m-8 4h6"/>',
};

// ---------------------------------------------------------------------------
// Design directions and sample data
// ---------------------------------------------------------------------------

const directions = {
  workspace: {
    letter: "A",
    name: "Studio",
    summary: "Fahrzeuge im Fokus. Erfassen und Vergleichen bleiben direkt verbunden.",
    description:
      "Elektrisches Blau, eine kompakte Navigation und eine grosse Fahrzeugbühne. Rechts stehen deine Anforderungen; die Auswahl bleibt immer griffbereit.",
    best: "Schneller Wechsel zwischen Recherche und Vergleich.",
    tradeoff: "Bilder nehmen bewusst Raum ein.",
  },
  dossier: {
    letter: "B",
    name: "Canvas",
    summary: "Eine offene Fahrzeugakte. Apricot, Aubergine und Bearbeitung direkt im Inhalt.",
    description:
      "Ein grosszügiger, warmer Entwurf mit horizontaler Navigation. Fahrzeug, persönliche Eindrücke und Anforderungen bilden eine zusammenhängende Seite.",
    best: "In Ruhe recherchieren, Eindrücke festhalten.",
    tradeoff: "Mehr Platz und längere Seiten pro Fahrzeug.",
  },
  matrix: {
    letter: "C",
    name: "Focus",
    summary:
      "Der Vergleich als Arbeitsfläche. Dunkle Flächen, klare Werte und direkte Bearbeitung.",
    description:
      "Ein dunkler Vergleichsbereich mit visueller Fahrzeugauswahl. Unterschiede und offene Angaben führen durch die Entscheidung.",
    best: "Eine engere Auswahl gründlich gegeneinander prüfen.",
    tradeoff: "Auf dem Handy wird der Vergleich seitlich gescrollt.",
  },
};

const initialCars = [
  {
    id: "enyaq",
    name: "Škoda Enyaq",
    trim: "85 · Selection",
    year: "2025",
    mileage: "12’400",
    status: "Favorit",
    note: "Gutes Raumgefühl bei der Probefahrt. Anhängerkupplung und Liefertermin noch mit dem Anbieter klären.",
    specs: ["4’658 mm", "1’879 mm", "5", "77 kWh"],
    price: "49’800",
  },
  {
    id: "id7",
    name: "VW ID.7 Tourer",
    trim: "Pro · Business",
    year: "2025",
    mileage: "8’600",
    status: "In Auswahl",
    note: "Ruhig auf der Autobahn. Die Länge nochmals in der Garage prüfen.",
    specs: ["4’961 mm", "1’862 mm", "5", "77 kWh"],
    price: "53’400",
  },
  {
    id: "i5",
    name: "BMW i5 Touring",
    trim: "eDrive40 · Sport",
    year: "2024",
    mileage: "19’200",
    status: "In Auswahl",
    note: "Sehr komfortabel. Höherer Kaufpreis, noch keine aktuelle Offerte erhalten.",
    specs: ["5’060 mm", "1’900 mm", "5", "81 kWh"],
    price: null,
  },
];

const criteria = [
  {
    id: "boot",
    name: "Kofferraum",
    type: "number",
    unit: "l",
    threshold: 550,
    priority: "Muss",
    group: "Alltag & Platz",
    hint: "Mindestens 550 l",
  },
  {
    id: "tow",
    name: "Anhängerkupplung",
    type: "boolean",
    priority: "Muss",
    group: "Alltag & Platz",
    hint: "Vorhanden oder nachrüstbar",
  },
  {
    id: "range",
    name: "Reichweite im Winter",
    type: "number",
    unit: "km",
    threshold: 350,
    priority: "Muss",
    group: "Unterwegs",
    hint: "Mindestens 350 km",
  },
  {
    id: "charge",
    name: "Ladezeit 20–80 %",
    type: "number",
    unit: "min",
    threshold: 30,
    max: true,
    priority: "Gewünscht",
    group: "Unterwegs",
    hint: "Höchstens 30 min",
  },
  {
    id: "comfort",
    name: "Sitzkomfort",
    type: "rating",
    priority: "Gewünscht",
    group: "Unterwegs",
    hint: "Persönliche Einschätzung · 1–5",
  },
];

const initialValues = {
  enyaq: {
    boot: { value: "585", source: "Herstellerangabe" },
    tow: { value: "", source: "Noch offen" },
    range: { value: "340", source: "Eigene Recherche" },
    charge: { value: "28", source: "Herstellerangabe" },
    comfort: { value: "4", source: "Probefahrt" },
  },
  id7: {
    boot: { value: "605", source: "Herstellerangabe" },
    tow: { value: "Ja", source: "Offerte" },
    range: { value: "380", source: "Eigene Recherche" },
    charge: { value: "28", source: "Herstellerangabe" },
    comfort: { value: "5", source: "Probefahrt" },
  },
  i5: {
    boot: { value: "570", source: "Herstellerangabe" },
    tow: { value: "Nein", source: "Offerte" },
    range: { value: "", source: "Noch offen" },
    charge: { value: "30", source: "Herstellerangabe" },
    comfort: { value: "4", source: "Probefahrt" },
  },
};

const navigationItems = [
  { key: "capture", label: "Garage", icon: "car" },
  { key: "compare", label: "Vergleich", icon: "compare" },
  { key: "criteria", label: "Kriterien", icon: "list" },
  { key: "finance", label: "Offerten", icon: "wallet" },
];

const viewBreadcrumbs = {
  capture: "Deine Garage",
  compare: "Dein Vergleich",
  criteria: "Deine Kriterien",
  finance: "Deine Offerten",
};

const detailTabs = [
  { key: "criteria", label: "Kriterien" },
  { key: "specs", label: "Technische Daten" },
  { key: "offers", label: "Offerten" },
  { key: "notes", label: "Notizen" },
];

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

const params = new URLSearchParams(location.search);

// Direction key from `directions`, or null for the gallery.
const concept = Object.hasOwn(directions, params.get("concept")) ? params.get("concept") : null;

// Set when the gallery shows this page as a preview inside an iframe.
const embedded = params.has("embed");

// Sample cars and their assessments, restored from this tab's session if present.
const storedDemo = readStoredDemo();
let cars = storedDemo?.cars || structuredClone(initialCars);
let values = storedDemo?.values || structuredClone(initialValues);

// What is shown: view, selected and compared car, tabs, filter, open editor, offer details.
// Focus (matrix) starts with the comparison.
let state = {
  view: params.get("view") === "compare" || concept === "matrix" ? "compare" : "capture",
  car: cars[0].id,
  second: cars[1].id,
  tab: "criteria",
  filter: "all",
  compareTab: "criteria",
  editor: null,
  offer: false,
};

// Data attributes of the control that opened the editor, used to restore focus on close.
let lastFocus;
let toastTimer;

const app = document.querySelector("#app");

function readStoredDemo() {
  try {
    return JSON.parse(sessionStorage.getItem(DEMO_STORAGE_KEY) || "null");
  } catch {
    // Missing storage or unreadable content: start from the sample data.
    return undefined;
  }
}

function persist() {
  try {
    sessionStorage.setItem(DEMO_STORAGE_KEY, JSON.stringify({ cars, values }));
  } catch {
    // Without storage the demo still works for the current page view.
  }
}

// ---------------------------------------------------------------------------
// Data helpers
// ---------------------------------------------------------------------------

function carById(id) {
  return cars.find((car) => car.id === id);
}

function assessmentOf(carId, criterionId) {
  return values[carId]?.[criterionId] || { value: "", source: UNKNOWN_LABEL, notes: "" };
}

function isUnknown(carId, criterion) {
  return assessmentOf(carId, criterion.id).value === "";
}

function countOpenCriteria(carId) {
  return criteria.filter((criterion) => isUnknown(carId, criterion)).length;
}

function assessmentStatus(criterion, value) {
  if (value === "") {
    return { label: UNKNOWN_LABEL, modifier: "open" };
  }

  // Personal ratings are never judged against a threshold.
  if (criterion.type === "rating") {
    return { label: "Persönlich", modifier: "neutral" };
  }

  const isMet = meetsRequirement(criterion, value);
  return { label: isMet ? "Erfüllt" : "Nicht erfüllt", modifier: isMet ? "" : "bad" };
}

function meetsRequirement(criterion, value) {
  if (criterion.type === "boolean") {
    return value === "Ja";
  }

  // `max` marks an upper limit, such as the charging time.
  if (criterion.max) {
    return Number(value) <= criterion.threshold;
  }

  return Number(value) >= criterion.threshold;
}

function visibleCriteria(includeSecondCar = false) {
  return criteria.filter((criterion) => matchesFilter(criterion, includeSecondCar));
}

function matchesFilter(criterion, includeSecondCar) {
  switch (state.filter) {
    case "all":
      return true;
    case "required":
      return criterion.priority === REQUIRED_PRIORITY;
    case "open":
      return (
        isUnknown(state.car, criterion) || (includeSecondCar && isUnknown(state.second, criterion))
      );
    case "different":
      return (
        assessmentOf(state.car, criterion.id).value !==
        assessmentOf(state.second, criterion.id).value
      );
    default:
      return false;
  }
}

function groupNames(criteriaList) {
  return [...new Set(criteriaList.map((criterion) => criterion.group))];
}

function criteriaInGroup(criteriaList, group) {
  return criteriaList.filter((criterion) => criterion.group === group);
}

// Canvas (dossier) edits assessments inline; Studio and Focus use the side sheet.
function editsAssessmentsInline() {
  return concept === "dossier";
}

// ---------------------------------------------------------------------------
// Markup helpers
// ---------------------------------------------------------------------------

// Joins markup fragments without separators, so spreading markup over several lines never
// adds whitespace to the DOM. Arrays (for example from map) are flattened one level.
function joinMarkup(...fragments) {
  return fragments.flat().join("");
}

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, (character) => HTML_ESCAPES[character]);
}

function icon(name) {
  return `<svg class="icon" viewBox="0 0 24 24" aria-hidden="true">${icons[name] || icons.info}</svg>`;
}

function editHintIcon() {
  return icon("edit").replace('class="icon"', 'class="icon edit-hint"');
}

function activeClass(isActive) {
  return isActive ? "active" : "";
}

function ariaCurrent(isCurrent, value) {
  return isCurrent ? `aria-current="${value}"` : "";
}

function renderBrand() {
  return joinMarkup(
    '<span class="brand">',
    icon("mark"),
    '<span>steervo<span class="brand-dot">.</span></span>',
    "</span>",
  );
}

function renderButton(label, action, extraAttributes = "", variant = "") {
  return `<button class="button ${variant}" data-action="${action}" ${extraAttributes}>${label}</button>`;
}

function renderCloseEditorButton() {
  return `<button class="icon-button" data-action="close-editor" aria-label="Bearbeitung schliessen">${icon("close")}</button>`;
}

function renderStatusTag(criterion, value) {
  const status = assessmentStatus(criterion, value);
  const checkIcon = status.modifier === "" ? icon("check") : "";
  return `<span class="status ${status.modifier}">${checkIcon}${status.label}</span>`;
}

function renderPriority(criterion) {
  const modifier = criterion.priority === REQUIRED_PRIORITY ? "required" : "";
  return `<span class="pill ${modifier}">${criterion.priority}</span>`;
}

function formatValue(criterion, value) {
  if (value === "") {
    return UNKNOWN_LABEL;
  }

  return `${escapeHtml(value)}${valueSuffix(criterion)}`;
}

function valueSuffix(criterion) {
  if (criterion.type === "rating") {
    return " / 5";
  }

  if (criterion.unit) {
    return " " + criterion.unit;
  }

  return "";
}

function renderVehicleImage(carId, className) {
  if (PHOTOGRAPHED_CAR_IDS.includes(carId)) {
    return `<img class="${className}" src="assets/${carId}.jpg" alt="Illustratives Modellbild ${escapeHtml(carById(carId).name)}" decoding="async">`;
  }

  return joinMarkup(
    `<div class="${className} image-empty">`,
    icon("car"),
    "<span>Noch kein Fahrzeugbild</span>",
    "</div>",
  );
}

// ---------------------------------------------------------------------------
// Feedback
// ---------------------------------------------------------------------------

function toast(message) {
  document.querySelector(".toast")?.remove();

  const toastElement = document.createElement("div");
  toastElement.className = "toast";
  toastElement.setAttribute("role", "status");
  toastElement.innerHTML = icon("check") + escapeHtml(message);
  document.body.append(toastElement);

  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => toastElement.remove(), TOAST_DURATION_MS);
}

// ---------------------------------------------------------------------------
// Review shell: switcher bar and gallery (proposal controls, not product UI)
// ---------------------------------------------------------------------------

function renderReviewBar() {
  return joinMarkup(
    '<div class="review-bar">',
    `<a href="./">${icon("left")} Alle Entwürfe</a>`,
    '<nav class="switcher" aria-label="Designrichtung">',
    Object.entries(directions).map(([key, direction]) => renderDirectionLink(key, direction)),
    "</nav>",
    '<span class="demo-label">Entwurfsrunde 2 · Beispieldaten</span>',
    "</div>",
  );
}

function renderDirectionLink(key, direction) {
  const isCurrent = key === concept;
  return `<a href="?concept=${key}" class="${activeClass(isCurrent)}" ${ariaCurrent(isCurrent, "page")}>${direction.letter}<span>${direction.name}</span></a>`;
}

function renderGallery() {
  return joinMarkup(
    '<main id="main" class="gallery" tabindex="-1">',
    '<div class="gallery-top">',
    renderBrand(),
    '<span class="gallery-round">Entwurfsrunde 2</span>',
    "</div>",
    '<div class="gallery-heading">',
    "<h1>Steervo neu denken.</h1>",
    "<p>Drei eigenständige Richtungen für deine Fahrzeugentscheidung.<br>Erfassen, vergleichen, weiterdenken. Direkt ausprobieren.</p>",
    "</div>",
    '<div class="gallery-options">',
    Object.entries(directions).map(([key, direction]) => renderDirectionCard(key, direction)),
    "</div>",
    '<footer class="gallery-footer">',
    "<span>Eine Angabe bearbeiten, speichern und im Vergleich wiederfinden.</span>",
    '<span>Illustrative Bilder und Beispieldaten. <button class="plain-link" data-action="reset">Demo zurücksetzen</button></span>',
    "</footer>",
    "</main>",
  );
}

function renderDirectionCard(key, direction) {
  const href = `?concept=${key}`;

  return joinMarkup(
    `<article class="direction ${key}">`,
    '<div class="direction-title">',
    `<h2><span>${direction.letter}</span>${direction.name}</h2>`,
    `<a class="open-direction" href="${href}" aria-label="${direction.name} öffnen">${icon("arrow")}</a>`,
    "</div>",
    `<a class="direction-preview" href="${href}" aria-label="${direction.name} ausprobieren">`,
    `<iframe src="?concept=${key}&embed=1" title="Vorschau ${direction.name}" tabindex="-1" loading="eager" aria-hidden="true"></iframe>`,
    "</a>",
    `<p>${direction.description}</p>`,
    `<a class="button primary" href="${href}">${direction.name} ausprobieren ${icon("arrow")}</a>`,
    "<dl>",
    `<div><dt>Stärke </dt><dd>${direction.best}</dd></div>`,
    `<div><dt>Abwägung </dt><dd>${direction.tradeoff}</dd></div>`,
    "</dl>",
    "</article>",
  );
}

function renderReviewNote() {
  const direction = directions[concept];

  return joinMarkup(
    '<div class="review-note">',
    `<strong>${direction.letter} · ${direction.name}</strong>`,
    `<span>${direction.summary}</span>`,
    '<button class="plain-link" data-action="reset">Demo zurücksetzen</button>',
    "</div>",
  );
}

// ---------------------------------------------------------------------------
// Product frame: navigation, context bar, sidebar
// ---------------------------------------------------------------------------

function renderNavigation() {
  return joinMarkup(navigationItems.map(renderNavigationItem));
}

function renderNavigationItem(item) {
  const isCurrent = state.view === item.key;

  return joinMarkup(
    `<button data-action="navigate" data-view="${item.key}" class="${activeClass(isCurrent)}" ${ariaCurrent(isCurrent, "page")}>`,
    icon(item.icon),
    `<span>${item.label}</span>`,
    "</button>",
  );
}

function renderContextBar() {
  return joinMarkup(
    '<header class="context-bar">',
    '<div class="crumbs">',
    `<strong>${EVALUATION_NAME}</strong>`,
    icon("right"),
    `<span>${viewBreadcrumbs[state.view]}</span>`,
    "</div>",
    `<span class="context-count">${cars.length} Autos in deiner Auswahl</span>`,
    "</header>",
  );
}

function renderSidebar() {
  return joinMarkup(
    '<aside class="sidebar">',
    `<a class="rail-brand" href="?concept=workspace" aria-label="Steervo Studio">${icon("mark")}</a>`,
    `<nav class="side-nav" aria-label="Evaluation">${renderNavigation()}</nav>`,
    '<div class="sidebar-bottom"><span class="avatar" aria-label="Dein Bereich">DU</span></div>',
    "</aside>",
  );
}

function renderTopNavigation() {
  return joinMarkup(
    '<header class="top-navigation">',
    renderBrand(),
    `<nav aria-label="Evaluation">${renderNavigation()}</nav>`,
    '<span class="avatar" aria-label="Dein persönlicher Bereich">DU</span>',
    "</header>",
  );
}

// ---------------------------------------------------------------------------
// Garage: candidate ribbon and vehicle record
// ---------------------------------------------------------------------------

function renderCandidateRibbon() {
  return joinMarkup(
    '<div class="shortlist">',
    '<div class="candidate-ribbon" aria-label="Fahrzeuge">',
    cars.map(renderCandidateChip),
    "</div>",
    renderButton(icon("plus") + " Auto hinzufügen", "add"),
    "</div>",
  );
}

function renderCandidateChip(car) {
  const isSelected = car.id === state.car;
  const caption =
    car.status === FAVOURITE_STATUS ? "Dein Favorit" : countOpenCriteria(car.id) + " Angaben offen";

  return joinMarkup(
    `<button class="candidate-chip ${activeClass(isSelected)}" data-action="select-car" data-car="${car.id}" ${ariaCurrent(isSelected, "true")}>`,
    renderVehicleImage(car.id, "thumbnail"),
    "<span>",
    `<strong>${escapeHtml(car.name)}</strong>`,
    `<small>${caption}</small>`,
    "</span>",
    isSelected ? icon("check") : "",
    "</button>",
  );
}

function renderCapture() {
  return joinMarkup(
    '<div class="capture-layout">',
    renderCandidateRibbon(),
    renderRecord(),
    "</div>",
  );
}

function renderRecord() {
  const car = carById(state.car);
  const tabContent = renderTabContent();

  return joinMarkup(
    '<section class="record">',
    renderVehicleStage(car),
    '<div class="record-content">',
    renderDetailTabs(),
    concept === "dossier" ? renderDossierContent(tabContent) : tabContent,
    state.tab === "criteria"
      ? '<p class="record-bottom-note">Deine Kriterien, deine Entscheidung. Ein verfehltes Muss schliesst kein Auto automatisch aus.</p>'
      : "",
    "</div>",
    "</section>",
  );
}

function renderVehicleStage(car) {
  const starIcon = car.status === FAVOURITE_STATUS ? icon("star") : "";

  return joinMarkup(
    '<div class="vehicle-stage">',
    '<div class="vehicle-title">',
    `<span class="status">${starIcon}${escapeHtml(car.status)}</span>`,
    `<h1>${escapeHtml(car.name)}</h1>`,
    `<p class="record-subtitle">${escapeHtml(car.trim)}</p>`,
    "</div>",
    '<div class="vehicle-image-wrap">',
    renderVehicleImage(car.id, "vehicle-image"),
    '<span class="image-caption">Illustratives Modellbild</span>',
    "</div>",
    '<div class="vehicle-facts">',
    `<span>${escapeHtml(car.year)}<small>Erstzulassung</small></span>`,
    `<span>${escapeHtml(car.mileage)} km<small>Kilometerstand</small></span>`,
    `<span>${escapeHtml(car.specs[BATTERY_SPEC_INDEX] || "Offen")}<small>Batterie netto</small></span>`,
    "</div>",
    '<div class="row-actions">',
    renderButton(icon("edit") + " Stammdaten", "edit-car"),
    renderButton("Vergleichen " + icon("arrow"), "navigate", 'data-view="compare"', "primary"),
    "</div>",
    "</div>",
  );
}

function renderDetailTabs() {
  return joinMarkup(
    '<nav class="tabs" aria-label="Fahrzeugdetails">',
    detailTabs.map(renderDetailTab),
    "</nav>",
  );
}

function renderDetailTab(tab) {
  const isCurrent = state.tab === tab.key;
  return `<button class="${activeClass(isCurrent)}" data-action="tab" data-tab="${tab.key}" ${ariaCurrent(isCurrent, "page")}>${tab.label}</button>`;
}

function renderTabContent() {
  switch (state.tab) {
    case "criteria":
      return renderAssessments();
    case "specs":
      return renderSpecs();
    case "offers":
      return renderOffers();
    case "notes":
      return renderNotes();
  }
}

function renderDossierContent(tabContent) {
  return joinMarkup(
    '<div class="dossier-content">',
    `<div>${tabContent}</div>`,
    renderDossierAside(),
    "</div>",
  );
}

function renderDossierAside() {
  const car = carById(state.car);

  return joinMarkup(
    '<aside class="dossier-aside">',
    '<section class="aside-section">',
    "<h3>Dein Eindruck</h3>",
    `<p>${escapeHtml(car.note || NO_NOTE_LABEL)}</p>`,
    renderButton("Notiz bearbeiten", "edit-notes", "", "text"),
    "</section>",
    '<section class="aside-section">',
    "<h3>Als Nächstes klären</h3>",
    `<p>${nextStepText(car)}</p>`,
    renderButton("Im Vergleich ansehen " + icon("arrow"), "navigate", 'data-view="compare"', ""),
    "</section>",
    "</aside>",
  );
}

function nextStepText(car) {
  if (!countOpenCriteria(car.id)) {
    return "Alle Kriterien sind erfasst. Prüfe jetzt die Unterschiede im Vergleich.";
  }

  return criteria
    .filter((criterion) => isUnknown(car.id, criterion))
    .map((criterion) => criterion.name)
    .join(", ");
}

function renderFilters(includeDifferences = false) {
  const filterOptions = [
    { key: "all", label: "Alle" },
    ...(includeDifferences ? [{ key: "different", label: "Unterschiede" }] : []),
    { key: "required", label: "Muss" },
    { key: "open", label: "Offen" },
  ];

  return joinMarkup(
    '<div class="filter-row" aria-label="Kriterien filtern">',
    filterOptions.map(renderFilterButton),
    "</div>",
  );
}

function renderFilterButton(option) {
  const isActive = state.filter === option.key;
  return `<button class="filter-button ${activeClass(isActive)}" aria-pressed="${isActive}" data-action="filter" data-filter="${option.key}">${option.label}</button>`;
}

// ---------------------------------------------------------------------------
// Record tabs: criteria, technical data, offers, notes
// ---------------------------------------------------------------------------

function renderAssessments() {
  const rows = visibleCriteria();
  const groups = groupNames(rows).map((group) => renderAssessmentGroup(group, rows));
  const capturedCount = criteria.length - countOpenCriteria(state.car);

  return joinMarkup(
    '<div class="section-toolbar">',
    "<div>",
    "<h3>Deine Anforderungen</h3>",
    "<p>Direkt auswählen und ergänzen.</p>",
    "</div>",
    renderFilters(),
    "</div>",
    groups.length ? groups : renderNoOpenCriteria(),
    '<div class="assessment-summary">',
    `<span>${capturedCount} von ${criteria.length} Angaben erfasst</span>`,
    "<span>Keine automatische Gesamtnote</span>",
    "</div>",
  );
}

function renderAssessmentGroup(group, rows) {
  const groupCriteria = criteriaInGroup(rows, group);

  return joinMarkup(
    "<section>",
    '<div class="assessment-heading">',
    `<strong>${group}</strong>`,
    `<span>${groupCriteria.length} Kriterien</span>`,
    "</div>",
    groupCriteria.map(renderAssessmentRow),
    "</section>",
  );
}

function renderNoOpenCriteria() {
  return joinMarkup(
    '<div class="empty">',
    "<h3>Hier ist alles geklärt.</h3>",
    "<p>Mit diesem Filter sind keine Kriterien offen.</p>",
    '<button class="button text" data-action="filter" data-filter="all">Alle Kriterien anzeigen</button>',
    "</div>",
  );
}

function renderAssessmentRow(criterion) {
  const assessment = assessmentOf(state.car, criterion.id);
  const status = assessmentStatus(criterion, assessment.value);
  const valueText = formatValue(criterion, assessment.value);
  const isEditingInline = editsAssessmentsInline() && state.editor?.criterion === criterion.id;

  return joinMarkup(
    `<button class="assessment-row" data-action="edit" data-car="${state.car}" data-criterion="${criterion.id}" aria-label="${escapeHtml(criterion.name)} bearbeiten, ${valueText}">`,
    '<span class="criterion-title">',
    criterion.name,
    renderPriority(criterion),
    `<small>${criterion.hint}</small>`,
    "</span>",
    '<span class="assessment-value">',
    valueText,
    `<small>${escapeHtml(assessment.source)}</small>`,
    // Phones hide the status column; the status then follows the value as text.
    `<small class="mobile-state ${status.modifier}">${status.label}</small>`,
    "</span>",
    renderStatusTag(criterion, assessment.value),
    editHintIcon(),
    "</button>",
    isEditingInline ? renderAssessmentEditor(true) : "",
  );
}

function renderSpecs() {
  const car = carById(state.car);
  const sections = [
    {
      title: "Abmessungen & Platz",
      labels: ["Länge", "Breite ohne Spiegel", "Sitzplätze"],
      values: car.specs.slice(0, 3),
    },
    {
      title: "Fahrzeug & Antrieb",
      labels: ["Erstzulassung", "Kilometerstand", "Batterie (netto)"],
      values: [car.year, car.mileage + " km", car.specs[BATTERY_SPEC_INDEX]],
    },
  ];

  return joinMarkup(
    '<div class="section-toolbar">',
    "<div>",
    "<h3>Technische Daten</h3>",
    "<p>Fakten zum Auto, unabhängig von deinen Kriterien.</p>",
    "</div>",
    renderButton(icon("edit") + " Bearbeiten", "edit-specs"),
    "</div>",
    '<div class="secondary-grid">',
    sections.map(renderSpecSection),
    "</div>",
  );
}

function renderSpecSection(section) {
  const entries = section.labels.map((label, index) =>
    joinMarkup(
      "<div>",
      `<dt>${label}</dt>`,
      `<dd>${escapeHtml(section.values[index] || UNKNOWN_LABEL)}</dd>`,
      "</div>",
    ),
  );

  return joinMarkup(
    '<section class="detail-section">',
    `<h3>${section.title}</h3>`,
    '<dl class="data-list">',
    entries,
    "</dl>",
    "</section>",
  );
}

function renderOffers() {
  const car = carById(state.car);

  return joinMarkup(
    '<div class="offer-heading">',
    `<h3>Offerten für ${escapeHtml(car.name)}</h3>`,
    '<span class="demo-note">Beispielofferte</span>',
    "</div>",
    car.price ? renderOffer(car) : renderNoOffer(),
    '<p class="record-bottom-note">Offerten behalten ihre Versionen und Dokumente. Kosten und Rechenszenarien werden in einer nächsten Ausarbeitung in dieselbe Struktur eingeordnet.</p>',
  );
}

function renderOffer(car) {
  const toggleLabel = state.offer ? "Details schliessen" : "Offerte ansehen";

  return joinMarkup(
    '<div class="offer-row">',
    "<div>",
    "<h3>Autohaus am See</h3>",
    "<p>Kauf · CHF · Version 2</p>",
    '<p><span class="status">Aktuell</span> <span class="demo-note">Stand 10.11.2026</span></p>',
    "</div>",
    '<div class="offer-price">',
    `CHF ${escapeHtml(car.price)}`,
    "<small>Bestätigter Gesamtpreis · Beispieldaten</small>",
    renderButton(toggleLabel + " " + icon("right"), "offer", "", "text"),
    "</div>",
    "</div>",
    state.offer ? renderOfferDetail() : "",
  );
}

function renderOfferDetail() {
  return joinMarkup(
    '<div class="offer-detail">',
    "<p><strong>Version 2</strong> · Kaufpreis inklusive Zubehör und Bereitstellung.</p>",
    "<p>Die Änderungen zur ersten Version sowie Dokumente würden hier gemeinsam erscheinen.</p>",
    '<p class="demo-note">Layoutvorschlag für Offerten. Versionierung und Dokumente bleiben Teil der bestehenden Anwendung.</p>',
    "</div>",
  );
}

function renderNoOffer() {
  return joinMarkup(
    '<div class="empty">',
    "<h3>Noch keine Offerte</h3>",
    "<p>Für dieses Beispielauto liegt kein Angebot vor.</p>",
    "</div>",
  );
}

function renderNotes() {
  const car = carById(state.car);

  return joinMarkup(
    '<section class="detail-section">',
    '<div class="section-toolbar">',
    "<h3>Deine Notizen</h3>",
    renderButton(icon("edit") + " Bearbeiten", "edit-notes"),
    "</div>",
    `<p class="notes-copy">${escapeHtml(car.note || NO_NOTE_LABEL)}</p>`,
    '<p class="record-bottom-note">Notizen zu einzelnen Bewertungen findest du direkt beim jeweiligen Kriterium.</p>',
    "</section>",
  );
}

// ---------------------------------------------------------------------------
// Comparison
// ---------------------------------------------------------------------------

function renderComparison() {
  const showsCriteria = state.compareTab === "criteria";
  const heading =
    concept === "matrix" ? "Was macht den Unterschied?" : "Deine Favoriten. Im direkten Vergleich.";

  return joinMarkup(
    '<section class="comparison-main">',
    '<div class="comparison-heading">',
    "<div>",
    `<h1>${heading}</h1>`,
    "<p>Vergleiche deine Anforderungen. Ergänze fehlende Angaben direkt im Vergleich.</p>",
    "</div>",
    renderButton(icon("car") + " Auto erfassen", "add", "", "primary"),
    "</div>",
    '<div class="comparison-tools">',
    '<nav class="comparison-tabs" aria-label="Vergleichsinhalt">',
    renderComparisonTab("criteria", "Kriterien"),
    renderComparisonTab("specs", "Technische Daten"),
    "</nav>",
    showsCriteria
      ? renderFilters(true)
      : '<span class="demo-note">Technische Daten werden nicht bewertet.</span>',
    "</div>",
    '<p class="comparison-scroll-hint">Seitlich wischen, um beide Autos zu sehen.</p>',
    '<div class="comparison-scroll" role="region" aria-label="Fahrzeugvergleich, seitlich scrollbar" tabindex="0">',
    '<table class="comparison-table">',
    "<thead>",
    "<tr>",
    '<th class="comparison-label" scope="col">',
    `<strong>${showsCriteria ? "Deine Anforderungen" : "Technische Daten"}</strong>`,
    showsCriteria ? "Werte anklicken und bearbeiten" : "Fakten nebeneinander",
    "</th>",
    `<th scope="col">${renderComparedCar(state.car, "first")}</th>`,
    `<th scope="col">${renderComparedCar(state.second, "second")}</th>`,
    "</tr>",
    "</thead>",
    `<tbody>${renderComparisonRows()}</tbody>`,
    "</table>",
    "</div>",
    '<div class="comparison-footer">',
    "<p>Unbekannt ist kein Nein. Persönliche Bewertungen bleiben subjektiv. Ein verfehltes Muss führt nicht zum automatischen Ausschluss.</p>",
    `<span class="scroll-hint">${renderButton("Zum Fahrzeug " + icon("arrow"), "navigate", 'data-view="capture"', "text")}</span>`,
    "</div>",
    "</section>",
  );
}

function renderComparisonTab(key, label) {
  const isActive = state.compareTab === key;
  return `<button data-action="compare-tab" data-tab="${key}" class="${activeClass(isActive)}" aria-pressed="${isActive}">${label}</button>`;
}

// `side` is "first" or "second"; each select offers every car except the other side's.
function renderComparedCar(carId, side) {
  const car = carById(carId);
  const ordinal = side === "first" ? "Erstes" : "Zweites";
  const otherCarId = side === "first" ? state.second : state.car;
  const options = cars
    .filter((candidate) => candidate.id !== otherCarId)
    .map(
      (candidate) =>
        `<option value="${candidate.id}" ${candidate.id === carId ? "selected" : ""}>${escapeHtml(candidate.name)}</option>`,
    );

  return joinMarkup(
    '<div class="compare-visual">',
    renderVehicleImage(carId, "compare-image"),
    `<span class="status">${escapeHtml(car.status)}</span>`,
    "</div>",
    `<label class="compare-candidate" for="compare-${side}">${ordinal} Auto</label>`,
    `<select id="compare-${side}" class="candidate-select" data-compare="${side}" aria-label="${ordinal} Auto">`,
    options,
    "</select>",
    `<span class="compare-trim">${escapeHtml(car.trim)}</span>`,
  );
}

function renderComparisonRows() {
  if (state.compareTab === "specs") {
    return joinMarkup(COMPARED_SPEC_LABELS.map(renderSpecComparisonRow));
  }

  const rows = visibleCriteria(true);
  const groups = groupNames(rows).map((group) => renderComparisonGroup(group, rows));
  if (!groups.length) {
    return '<tr><td colspan="3" class="compare-empty">Keine Kriterien für diesen Filter. Wähle «Alle», um weiterzuarbeiten.</td></tr>';
  }

  return joinMarkup(groups);
}

function renderSpecComparisonRow(label, specIndex) {
  const cells = [state.car, state.second].map(
    (carId) =>
      `<td class="static-cell">${escapeHtml(carById(carId).specs[specIndex] || UNKNOWN_LABEL)}</td>`,
  );

  return joinMarkup("<tr>", `<th scope="row">${label}</th>`, cells, "</tr>");
}

function renderComparisonGroup(group, rows) {
  return joinMarkup(
    `<tr class="group-label"><th colspan="3">${group}</th></tr>`,
    criteriaInGroup(rows, group).map(renderCriterionComparisonRow),
  );
}

function renderCriterionComparisonRow(criterion) {
  // Canvas opens the editor in its own table row below the edited criterion.
  const isEditingInline = editsAssessmentsInline() && state.editor?.criterion === criterion.id;

  return joinMarkup(
    "<tr>",
    `<th scope="row">${criterion.name} ${renderPriority(criterion)}<small>${criterion.hint}</small></th>`,
    [state.car, state.second].map((carId) => renderComparisonCell(carId, criterion)),
    "</tr>",
    isEditingInline
      ? `<tr class="comparison-editor-row"><td colspan="3">${renderAssessmentEditor(true)}</td></tr>`
      : "",
  );
}

function renderComparisonCell(carId, criterion) {
  const assessment = assessmentOf(carId, criterion.id);
  const status = assessmentStatus(criterion, assessment.value);

  return joinMarkup(
    "<td>",
    `<button class="cell-edit ${status.modifier}" data-action="edit" data-car="${carId}" data-criterion="${criterion.id}" aria-label="${escapeHtml(criterion.name)} für ${escapeHtml(carById(carId).name)} bearbeiten">`,
    "<span>",
    `<span class="value">${formatValue(criterion, assessment.value)}</span>`,
    `<small>${status.label} · ${escapeHtml(assessment.source)}</small>`,
    "</span>",
    icon("edit"),
    "</button>",
    "</td>",
  );
}

// ---------------------------------------------------------------------------
// Criteria and offers views
// ---------------------------------------------------------------------------

function renderCriteriaView() {
  return joinMarkup(
    '<section class="record">',
    '<div class="record-heading">',
    "<div>",
    "<h1>Was ist dir wichtig?</h1>",
    '<p class="record-subtitle">Anforderungen für alle Autos dieser Evaluation.</p>',
    "</div>",
    "</div>",
    '<p class="criteria-help">Dieselben Gruppen und Prioritäten erscheinen beim Erfassen und Vergleichen. Diese Konzeptansicht zeigt die gemeinsame Struktur.</p>',
    criteria.map(renderCriterionOverviewRow),
    '<p class="record-bottom-note">Kriterien anlegen, gruppieren und sortieren bleiben im späteren Ausbau erhalten. In dieser Vorschau werden Fahrzeugbewertungen bearbeitet.</p>',
    "</section>",
  );
}

function renderCriterionOverviewRow(criterion) {
  return joinMarkup(
    '<div class="assessment-row" style="grid-template-columns:1fr 1fr 100px">',
    `<span class="criterion-title">${criterion.name}<small>${criterion.group}</small></span>`,
    `<span class="muted">${criterion.hint}</span>`,
    renderPriority(criterion),
    "</div>",
  );
}

function renderFinanceView() {
  return joinMarkup(
    renderCandidateRibbon(),
    '<section class="record finance-record">',
    "<h1>Deine Offerten</h1>",
    '<p class="record-subtitle">Angebote und Unterlagen beim jeweiligen Auto.</p>',
    renderOffers(),
    "</section>",
  );
}

// ---------------------------------------------------------------------------
// Page rendering
// ---------------------------------------------------------------------------

function render() {
  if (!concept) {
    document.documentElement.lang = "de";
    app.innerHTML = renderGallery();
    return;
  }

  document.documentElement.lang = "de";
  document.title = `Steervo · ${directions[concept].name}`;

  const isInlineAssessment = editsAssessmentsInline() && state.editor?.kind === "assessment";
  const showsSheet = state.editor && !isInlineAssessment;

  app.innerHTML = joinMarkup(
    embedded ? "" : renderReviewBar(),
    `<div class="product ${concept}">`,
    concept === "workspace" ? renderStudioFrame() : renderTopNavigationFrame(),
    "</div>",
    embedded ? "" : renderReviewNote(),
    showsSheet ? renderSheet() : "",
  );

  document.body.classList.toggle("editing", !!document.querySelector(".editor-panel"));
  if (embedded) {
    document.querySelector(".product").style.minHeight = EMBEDDED_MIN_HEIGHT;
  }
}

function renderStudioFrame() {
  return joinMarkup(
    renderSidebar(),
    '<div class="work-area">',
    renderContextBar(),
    `<main id="main" tabindex="-1">${renderViewContent()}</main>`,
    "</div>",
  );
}

// Canvas (dossier) and Focus (matrix) share the horizontal navigation.
function renderTopNavigationFrame() {
  const isDossier = concept === "dossier";

  return joinMarkup(
    renderTopNavigation(),
    `<main id="main" tabindex="-1" class="${isDossier ? "dossier-main-wrap" : "matrix-main"}">`,
    renderContextBar(),
    `<div class="${isDossier ? "dossier-main" : ""}">${renderViewContent()}</div>`,
    "</main>",
  );
}

function renderViewContent() {
  switch (state.view) {
    case "capture":
      return renderCapture();
    case "compare":
      return renderComparison();
    case "criteria":
      return renderCriteriaView();
    default:
      return renderFinanceView();
  }
}

// ---------------------------------------------------------------------------
// Editors: assessment editor (inline or in the sheet) and side sheet
// ---------------------------------------------------------------------------

function renderAssessmentEditor(isInline = false) {
  const editor = state.editor;
  const criterion = criteria.find((candidate) => candidate.id === editor.criterion);
  const car = carById(editor.car);

  return joinMarkup(
    `<section class="${isInline ? "inline-editor" : ""}" ${isInline ? 'role="region" aria-label="Bewertung bearbeiten"' : ""}>`,
    '<div class="editor-heading">',
    "<div>",
    `<h2 id="editor-title">${criterion.name}</h2>`,
    `<p>${escapeHtml(car.name)} · Bewertung bearbeiten</p>`,
    "</div>",
    renderCloseEditorButton(),
    "</div>",
    `<div class="editor-context">${renderPriority(criterion)}<span>${criterion.hint}</span></div>`,
    '<form id="editor-form" class="editor-form" data-form="assessment">',
    renderAssessmentFields(criterion, assessmentOf(editor.car, editor.criterion)),
    '<div class="editor-actions">',
    renderButton("Abbrechen", "close-editor"),
    `<button class="button primary" type="submit">${icon("check")} Speichern</button>`,
    "</div>",
    "</form>",
    isInline
      ? ""
      : '<p class="demo-note">Die Änderung erscheint auch im Fahrzeug und im Vergleich. Nur in diesem Browser-Tab gespeichert.</p>',
    "</section>",
  );
}

function renderAssessmentFields(criterion, assessment) {
  const unitSuffix = criterion.unit ? " (" + criterion.unit + ")" : "";
  const sourceOptions = ASSESSMENT_SOURCES.map(
    (source) => `<option ${assessment.source === source ? "selected" : ""}>${source}</option>`,
  );

  return joinMarkup(
    '<div class="field-grid">',
    '<div class="field">',
    `<label for="edit-value">Deine Angabe${unitSuffix}</label>`,
    renderValueInput(criterion, assessment),
    "</div>",
    '<div class="field">',
    '<label for="edit-source">Grundlage</label>',
    '<select name="source" id="edit-source">',
    sourceOptions,
    "</select>",
    "</div>",
    "</div>",
    '<div class="field notes-field">',
    '<label for="edit-notes">Notiz <span class="muted">(optional)</span></label>',
    `<textarea id="edit-notes" name="notes" rows="3" placeholder="Was möchtest du dazu festhalten?">${escapeHtml(assessment.notes || "")}</textarea>`,
    "</div>",
  );
}

// An empty value means unknown, so every input offers "Noch offen" instead of a default.
function renderValueInput(criterion, assessment) {
  if (criterion.type === "number") {
    return joinMarkup(
      `<input id="edit-value" name="value" type="number" min="0" step="any" value="${escapeHtml(assessment.value)}" placeholder="Noch offen" inputmode="decimal">`,
      `<small>Leeres Feld = unbekannt · ${criterion.unit}</small>`,
    );
  }

  const choices = criterion.type === "boolean" ? BOOLEAN_CHOICES : RATING_CHOICES;
  const ratingSuffix = criterion.type === "rating" ? " / 5" : "";
  const options = choices.map(
    (choice) =>
      `<option value="${choice}" ${assessment.value === choice ? "selected" : ""}>${choice}${ratingSuffix}</option>`,
  );

  return joinMarkup(
    '<select id="edit-value" name="value">',
    '<option value="">Noch offen</option>',
    options,
    "</select>",
  );
}

function renderSheet() {
  const content =
    state.editor.kind === "assessment" ? renderAssessmentEditor() : renderSheetEditor();

  return joinMarkup(
    '<div class="editor-shade" data-action="close-editor"></div>',
    `<section class="editor-panel ${concept}" role="dialog" aria-modal="true" aria-labelledby="editor-title">`,
    content,
    "</section>",
  );
}

function renderSheetEditor() {
  const editor = state.editor;
  const car = carById(editor.car || state.car);
  const isAdding = editor.kind === "add";

  return joinMarkup(
    '<div class="editor-heading">',
    "<div>",
    `<h2 id="editor-title">${sheetTitle(editor.kind)}</h2>`,
    `<p>${isAdding ? EVALUATION_NAME : escapeHtml(car.name)}</p>`,
    "</div>",
    renderCloseEditorButton(),
    "</div>",
    `<form id="editor-form" data-form="${editor.kind}">`,
    renderSheetFields(editor.kind, car),
    '<div class="editor-actions">',
    renderButton("Abbrechen", "close-editor"),
    `<button type="submit" class="button primary">${isAdding ? "Auto hinzufügen" : "Speichern"}</button>`,
    "</div>",
    "</form>",
    '<p class="demo-note">Beispieldaten · Änderungen bleiben in diesem Browser-Tab.</p>',
  );
}

function sheetTitle(kind) {
  switch (kind) {
    case "add":
      return "Auto erfassen";
    case "car":
      return "Stammdaten bearbeiten";
    case "notes":
      return "Notiz bearbeiten";
    default:
      return "Technische Daten";
  }
}

function renderSheetFields(kind, car) {
  switch (kind) {
    case "add":
      return renderCarFields({}, false);
    case "car":
      return renderCarFields(car, true);
    case "notes":
      return renderNoteField(car);
    default:
      return renderSpecFields(car);
  }
}

function renderCarFields(car, includesStatus) {
  return joinMarkup(
    renderTextField("Name des Autos", "name", car.name, true),
    renderTextField("Variante / Ausstattung", "trim", car.trim),
    '<div class="field-grid">',
    renderTextField("Jahr", "year", car.year),
    renderTextField("Kilometerstand", "mileage", car.mileage),
    "</div>",
    includesStatus ? renderStatusField(car) : "",
  );
}

function renderStatusField(car) {
  return joinMarkup(
    '<div class="field">',
    '<label for="form-status">Einordnung</label>',
    '<select id="form-status" name="status">',
    `<option ${car.status === SHORTLIST_STATUS ? "selected" : ""}>In Auswahl</option>`,
    `<option ${car.status === FAVOURITE_STATUS ? "selected" : ""}>Favorit</option>`,
    "</select>",
    "<small>Zurückstellen und Ausscheiden mit Begründung folgen im späteren Ausbau.</small>",
    "</div>",
  );
}

function renderNoteField(car) {
  return joinMarkup(
    '<div class="field">',
    `<label for="form-note">Dein Eindruck zu ${escapeHtml(car.name)}</label>`,
    `<textarea id="form-note" name="note" rows="7" maxlength="2000">${escapeHtml(car.note)}</textarea>`,
    "</div>",
  );
}

function renderSpecFields(car) {
  return joinMarkup(
    SPEC_FIELD_LABELS.map((label, index) =>
      renderTextField(label, "spec" + index, car.specs[index]),
    ),
  );
}

function renderTextField(label, name, value = "", required = false) {
  const limits = required ? 'required maxlength="80"' : 'maxlength="200"';

  return joinMarkup(
    '<div class="field">',
    `<label for="form-${name}">${label}</label>`,
    `<input id="form-${name}" name="${name}" value="${escapeHtml(value)}" ${limits}>`,
    "</div>",
  );
}

// ---------------------------------------------------------------------------
// Editor lifecycle and focus
// ---------------------------------------------------------------------------

function findControl(predicate) {
  return [...document.querySelectorAll("[data-action]")].find(predicate);
}

function openEditor(editor, opener) {
  lastFocus = { ...opener.dataset };
  state.editor = editor;
  render();
  focusEditor();
}

function focusEditor() {
  // Wait for the new markup to be laid out before moving focus and scrolling.
  requestAnimationFrame(() => {
    const editorElement = document.querySelector(".editor-panel,.inline-editor");
    editorElement?.querySelector("input,select,textarea")?.focus({ preventScroll: true });
    if (editsAssessmentsInline() && state.editor?.kind === "assessment") {
      editorElement?.scrollIntoView({ block: "nearest", behavior: "instant" });
    }
  });
}

function closeEditor() {
  state.editor = null;
  render();

  if (!lastFocus) {
    return;
  }

  // Escape and cancel return focus to the control that opened the editor.
  const opener = findControl(isEditorOpener);
  opener?.focus({ preventScroll: true });
}

function isEditorOpener(control) {
  const matchesAction = control.dataset.action === lastFocus.action;
  const matchesCriterion =
    !lastFocus.criterion || control.dataset.criterion === lastFocus.criterion;
  const matchesCar = !lastFocus.car || control.dataset.car === lastFocus.car;
  return matchesAction && matchesCriterion && matchesCar;
}

// ---------------------------------------------------------------------------
// Event handling
// ---------------------------------------------------------------------------

function handleClick(event) {
  const control = event.target.closest("[data-action]");
  if (!control) {
    return;
  }

  const action = control.dataset.action;

  if (action === "reset") {
    resetDemo();
    return;
  }

  if (action === "close-editor") {
    closeEditor();
    return;
  }

  if (action === "edit") {
    const editor = {
      kind: "assessment",
      car: control.dataset.car,
      criterion: control.dataset.criterion,
    };
    openEditor(editor, control);
    return;
  }

  if (SHEET_EDITOR_ACTIONS.includes(action)) {
    openEditor({ kind: action.replace("edit-", ""), car: state.car }, control);
    return;
  }

  applyViewAction(action, control.dataset);
  render();

  // Re-rendering replaces the clicked control; focus its new counterpart.
  const counterpart = findControl(
    (candidate) =>
      candidate.dataset.action === action && isCounterpart(candidate.dataset, control.dataset),
  );
  counterpart?.focus({ preventScroll: true });
}

// The most specific data attribute of the clicked control identifies its counterpart.
function isCounterpart(candidateData, clickedData) {
  if (clickedData.filter) {
    return candidateData.filter === clickedData.filter;
  }

  if (clickedData.tab) {
    return candidateData.tab === clickedData.tab;
  }

  if (clickedData.car) {
    return candidateData.car === clickedData.car;
  }

  if (clickedData.view) {
    return candidateData.view === clickedData.view;
  }

  return true;
}

function applyViewAction(action, data) {
  state.editor = null;

  switch (action) {
    case "navigate":
      state.view = data.view;
      state.filter = "all";
      break;
    case "select-car":
      selectCar(data.car);
      break;
    case "tab":
      state.tab = data.tab;
      break;
    case "filter":
      state.filter = data.filter;
      break;
    case "compare-tab":
      state.compareTab = data.tab;
      break;
    case "offer":
      state.offer = !state.offer;
      break;
  }
}

function selectCar(carId) {
  const previousCarId = state.car;
  state.car = carId;

  // A car is never compared with itself: swap when the compared car is selected.
  if (state.car === state.second) {
    state.second = previousCarId;
  }
}

function resetDemo() {
  cars = structuredClone(initialCars);
  values = structuredClone(initialValues);
  state = { ...state, car: cars[0].id, second: cars[1].id, editor: null, filter: "all" };
  persist();
  render();
  toast("Beispieldaten zurückgesetzt");
}

function handleCompareChange(event) {
  const side = event.target.dataset.compare;
  if (!side) {
    return;
  }

  state[side === "first" ? "car" : "second"] = event.target.value;
  state.editor = null;
  render();
  document.querySelector(`#compare-${side}`)?.focus();
}

function handleSubmit(event) {
  const form = event.target;
  if (!form.matches("[data-form]")) {
    return;
  }

  event.preventDefault();

  const formValues = Object.fromEntries(new FormData(form));
  const editor = state.editor;
  const car = carById(editor.car || state.car);

  switch (editor.kind) {
    case "assessment":
      saveAssessment(editor, formValues);
      break;
    case "add":
      addCar(formValues);
      break;
    case "car":
      saveCarDetails(car, formValues);
      break;
    case "notes":
      car.note = formValues.note;
      break;
    case "specs":
      car.specs = [formValues.spec0, formValues.spec1, formValues.spec2, formValues.spec3];
      break;
  }

  persist();
  closeEditor();
  toast(
    editor.kind === "add"
      ? "Auto zur Auswahl hinzugefügt"
      : "Gespeichert · Fahrzeug und Vergleich sind aktuell",
  );
}

function saveAssessment(editor, formValues) {
  values[editor.car] ??= {};
  values[editor.car][editor.criterion] = {
    value: formValues.value,
    source: formValues.source,
    notes: formValues.notes,
  };
}

// `formValues.trim` is the trim-level field, not a string method.
function addCar(formValues) {
  const id = "demo-" + Date.now();

  cars.push({
    id,
    name: formValues.name.trim(),
    trim: formValues.trim.trim() || "Variante offen",
    year: formValues.year || "Jahr offen",
    mileage: formValues.mileage || "Offen",
    status: SHORTLIST_STATUS,
    note: "",
    specs: ["", "", "", ""],
    price: null,
  });
  values[id] = {};

  // Show the new car with its criteria right away.
  state.car = id;
  state.view = "capture";
  state.tab = "criteria";
  state.filter = "all";
}

function saveCarDetails(car, formValues) {
  Object.assign(car, {
    name: formValues.name.trim(),
    trim: formValues.trim.trim(),
    year: formValues.year,
    mileage: formValues.mileage,
    status: formValues.status,
  });
}

function handleKeydown(event) {
  if (event.key === "Escape" && state.editor) {
    event.preventDefault();
    closeEditor();
    return;
  }

  if (event.key === "Tab") {
    keepFocusInSheet(event);
  }
}

// The side sheet is modal: Tab and Shift+Tab cycle within it.
function keepFocusInSheet(event) {
  const panel = document.querySelector(".editor-panel");
  if (!panel) {
    return;
  }

  const focusable = [...panel.querySelectorAll(FOCUSABLE_SELECTOR)].filter(
    (element) => !element.disabled,
  );
  const first = focusable[0];
  const last = focusable.at(-1);

  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault();
    first.focus();
  }
}

// ---------------------------------------------------------------------------
// Start
// ---------------------------------------------------------------------------

app.addEventListener("click", handleClick);
app.addEventListener("change", handleCompareChange);
app.addEventListener("submit", handleSubmit);
document.addEventListener("keydown", handleKeydown);

render();
