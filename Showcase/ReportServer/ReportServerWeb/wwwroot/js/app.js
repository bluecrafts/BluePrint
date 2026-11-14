// ReportServerWeb - the viewer.
//
// Plain JavaScript, no framework. Everything it shows comes from ReportServerAPI
// through /api (ReportServerWeb passes those requests on):
//
//   GET /api/reports                               the reports and their conditions
//   GET /api/reports/{id}/info?<conditions>         page count and page sizes (renders once)
//   GET /api/reports/{id}/pages/{n}?<conditions>&layout=paper|table
//                                                   the HTML of ONE page
//   GET /api/reports/{id}/pdf?<conditions>[&pages=n]
//
// A page is shown in an <iframe>, so the report's own CSS and the page around it
// cannot affect each other. The viewer adds the fonts, frame.css and the theme to
// the page's <head>; changing the theme swaps one stylesheet in both documents and
// asks the server for nothing.

"use strict";

const THEMES = [
  { id: "classic", name: "Classic" },
  { id: "slate", name: "Slate (dark)" },
  { id: "sepia", name: "Sepia" },
  { id: "ocean", name: "Ocean" },
  { id: "mono", name: "Mono" },
];

const ZOOM_STEPS = [0.5, 0.75, 1, 1.25, 1.5, 2];
const PAGE_CACHE_SIZE = 24;     // pages kept in the browser, so turning back is instant
const CSS_PX_PER_MM = 96 / 25.4;

const state = {
  reports: [],
  report: null,          // the selected report
  shown: null,           // { report, query, info } of what the viewer shows
  page: 1,
  layout: "paper",
  zoom: "width",
  theme: "classic",
  pageBytes: 0,
  loadToken: 0,
};

const pageCache = new Map();     // url -> html
const $ = (id) => document.getElementById(id);

// ---- start -------------------------------------------------------------------

document.addEventListener("DOMContentLoaded", start);

async function start() {
  setupTheme();
  setupToolbar();
  setupSidebar();

  try {
    state.reports = await getJson("/api/reports");
  } catch (error) {
    showMessage("The reports could not be loaded", error.message, start);
    return;
  }
  buildReportList();

  // A link with ?report=... opens that report with its conditions and page.
  const url = new URLSearchParams(location.search);
  const initial = state.reports.find((r) => r.id === url.get("report"));
  if (url.get("layout") === "table") state.layout = "table";
  if (url.get("zoom")) state.zoom = url.get("zoom");
  if (THEMES.some((t) => t.id === url.get("theme"))) applyTheme(url.get("theme"));
  updateLayoutButtons();
  $("zoom-select").value = state.zoom;

  if (initial) {
    selectReport(initial, url);
    showReport(Number(url.get("page")) || 1);
  } else {
    updateToolbar();
  }
}

// ---- theme -------------------------------------------------------------------

function setupTheme() {
  const select = $("theme-select");
  for (const theme of THEMES) select.add(new Option(theme.name, theme.id));

  let saved = null;
  try { saved = localStorage.getItem("rs-theme"); } catch { /* storage may be off */ }
  if (!THEMES.some((t) => t.id === saved)) {
    saved = matchMedia("(prefers-color-scheme: dark)").matches ? "slate" : "classic";
  }
  applyTheme(saved);

  select.addEventListener("change", () => {
    applyTheme(select.value);
    try { localStorage.setItem("rs-theme", select.value); } catch { /* ignore */ }
  });
}

function themeHref(id) {
  return `/themes/${id}.css`;
}

function applyTheme(id) {
  state.theme = id;
  $("theme-select").value = id;
  $("theme-link").href = themeHref(id);
  // The page in the viewer has its own copy of the theme link: swap it too.
  const frameLink = frameDocument()?.getElementById("rs-theme");
  if (frameLink) frameLink.href = themeHref(id);
}

// ---- report list and conditions ------------------------------------------------

function buildReportList() {
  const list = $("report-list");
  list.replaceChildren();
  for (const report of state.reports) {
    const item = document.createElement("li");
    item.role = "option";
    item.tabIndex = 0;
    item.dataset.id = report.id;
    item.setAttribute("aria-selected", "false");

    const title = document.createElement("div");
    title.className = "report-title";
    title.textContent = report.title;
    const description = document.createElement("div");
    description.className = "report-description";
    description.textContent = report.description;
    item.append(title, description);

    const choose = () => {
      selectReport(report, currentValues());
      showReport(1);
      closeSidebar();
    };
    item.addEventListener("click", choose);
    item.addEventListener("keydown", (e) => {
      if (e.key === "Enter" || e.key === " ") { e.preventDefault(); choose(); }
    });
    list.append(item);
  }
}

// keep: values to carry over (the period stays when another report is chosen)
function selectReport(report, keep) {
  state.report = report;
  for (const item of $("report-list").children) {
    item.setAttribute("aria-selected", String(item.dataset.id === report.id));
  }

  const fields = $("condition-fields");
  fields.replaceChildren();
  for (const p of report.parameters) {
    const field = document.createElement("div");
    field.className = "field" + (p.kind === "select" ? " wide" : "");

    const label = document.createElement("label");
    label.textContent = p.label;
    label.htmlFor = "cond-" + p.name;

    let input;
    if (p.kind === "date") {
      input = document.createElement("input");
      input.type = "date";
      if (p.min) input.min = p.min;
      if (p.max) input.max = p.max;
      input.addEventListener("input", updatePresets);
    } else {
      input = document.createElement("select");
      for (const o of p.options) input.add(new Option(o.label, o.value));
    }
    input.id = "cond-" + p.name;
    input.name = p.name;

    const wanted = keep instanceof URLSearchParams ? keep.get(p.name) : keep?.[p.name];
    input.value = wanted ?? p.defaultValue;
    if (input.value === "" || (input.tagName === "SELECT" && input.selectedIndex < 0)) input.value = p.defaultValue;

    field.append(label, input);
    fields.append(field);
  }
  buildPresets();
}

function currentValues() {
  const values = {};
  for (const el of $("condition-fields").querySelectorAll("input, select")) values[el.name] = el.value;
  return values;
}

// The quick choices for the period: every year that has orders, and all of them.
function buildPresets() {
  const box = $("period-presets");
  box.replaceChildren();
  const from = state.report.parameters.find((p) => p.name === "FromDate");
  const to = state.report.parameters.find((p) => p.name === "ToDate");
  if (!from?.min || !to?.max) return;

  const presets = [];
  for (let year = Number(from.min.slice(0, 4)); year <= Number(to.max.slice(0, 4)); year++) {
    presets.push({ label: String(year), from: `${year}-01-01`, to: `${year}-12-31` });
  }
  presets.push({ label: "All", from: from.min, to: to.max });

  for (const preset of presets) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "button";
    button.textContent = preset.label;
    button.dataset.from = preset.from;
    button.dataset.to = preset.to;
    button.addEventListener("click", () => {
      $("cond-FromDate").value = preset.from;
      $("cond-ToDate").value = preset.to;
      updatePresets();
      showReport(1);
    });
    box.append(button);
  }
  updatePresets();
}

function updatePresets() {
  const from = $("cond-FromDate")?.value;
  const to = $("cond-ToDate")?.value;
  for (const b of $("period-presets").children) {
    b.setAttribute("aria-pressed", String(b.dataset.from === from && b.dataset.to === to));
  }
}

// ---- showing a report ------------------------------------------------------------

async function showReport(page) {
  if (!state.report) return;
  const report = state.report;
  const query = new URLSearchParams(currentValues());
  const token = ++state.loadToken;

  showBusy(`Rendering "${report.title}"... The first request for a set of conditions renders every page once; turning pages after that is quick.`);
  try {
    const info = await getJson(`/api/reports/${report.id}/info?${query}`);
    if (token !== state.loadToken) return;     // a newer request took over
    state.shown = { report, query, info };
    showInfo();
    await loadPage(Math.min(Math.max(1, page), info.pageCount), token);
  } catch (error) {
    if (token !== state.loadToken) return;
    showMessage("The report could not be shown", error.message, () => showReport(page));
  }
}

function pageUrl(n, layout = state.layout) {
  const { report, query } = state.shown;
  return `/api/reports/${report.id}/pages/${n}?${query}&layout=${layout}`;
}

async function fetchPage(url) {
  if (pageCache.has(url)) {
    const html = pageCache.get(url);
    pageCache.delete(url);           // move to the end: most recently used
    pageCache.set(url, html);
    return { html, fromBrowser: true };
  }
  const response = await fetch(url);
  if (!response.ok) throw new Error(await problemText(response));
  const html = await response.text();
  pageCache.set(url, html);
  while (pageCache.size > PAGE_CACHE_SIZE) pageCache.delete(pageCache.keys().next().value);
  return { html, fromBrowser: false };
}

async function loadPage(n, token = ++state.loadToken) {
  if (!state.shown) return;
  const count = state.shown.info.pageCount;
  n = Math.min(Math.max(1, n), count);

  // Only a slow page gets the spinner; one from the cache shows at once.
  const busyTimer = setTimeout(() => showBusy(`Loading page ${n} of ${count}...`), 150);
  try {
    const url = pageUrl(n);
    const { html, fromBrowser } = await fetchPage(url);
    if (token !== state.loadToken) return;
    state.page = n;
    state.pageBytes = fromBrowser ? -1 : new Blob([html]).size;
    await showInFrame(html);
    hideOverlays();
    updateToolbar();
    showInfo();
    saveToAddress();
    prefetch(n + 1);
  } catch (error) {
    if (token !== state.loadToken) return;
    showMessage(`Page ${n} could not be loaded`, error.message, () => loadPage(n));
  } finally {
    clearTimeout(busyTimer);
  }
}

// The next page is asked for while the reader looks at this one.
function prefetch(n) {
  if (!state.shown || n > state.shown.info.pageCount) return;
  fetchPage(pageUrl(n)).catch(() => { /* it is asked for again when needed */ });
}

// Puts the HTML of a page into the iframe, with the fonts, frame.css and the theme
// added at the end of its <head> (after BluePrint's own <style>, so they win).
function showInFrame(html) {
  const frame = $("page-frame");
  const page = state.shown.info.pages[state.page - 1];
  const paperSize = state.layout === "paper"
    ? `${(page.width / CSS_PX_PER_MM).toFixed(2)}mm ${(page.height / CSS_PX_PER_MM).toFixed(2)}mm`
    : (state.shown.report.orientation === "landscape" ? "A4 landscape" : "A4 portrait");
  const margin = state.layout === "paper" ? "0" : "12mm";

  const head =
    `<link rel="stylesheet" href="/api/fonts/fonts.css">` +
    `<link rel="stylesheet" href="/css/frame.css">` +
    `<link id="rs-theme" rel="stylesheet" href="${themeHref(state.theme)}">` +
    `<style id="rs-print">@page { size: ${paperSize}; margin: ${margin}; }</style>`;

  const doc = html
    .replace(/<html([^>]*)>/i, `<html$1 class="rs-frame rs-${state.layout}">`)
    .replace(/<\/head>/i, head + "</head>");

  return new Promise((resolve) => {
    frame.onload = () => {
      const fd = frameDocument();
      fd?.addEventListener("keydown", onKey);
      // Wait for the fonts, so "fit" measures the page as it will be drawn - but not
      // for ever: a font that never arrives must not leave the spinner on.
      const fontsReady = fd?.fonts?.ready ?? Promise.resolve();
      const enough = new Promise((r) => setTimeout(r, 2000));
      Promise.race([fontsReady, enough]).then(() => { applyZoom(); resolve(); });
    };
    frame.srcdoc = doc;
  });
}

function frameDocument() {
  try { return $("page-frame").contentDocument; } catch { return null; }
}

// ---- zoom -------------------------------------------------------------------------

function applyZoom() {
  const fd = frameDocument();
  if (!fd?.documentElement || !state.shown) return;
  const page = state.shown.info.pages[state.page - 1];
  const stage = $("stage");

  let factor = Number(state.zoom);
  if (state.zoom === "width" || state.zoom === "page") {
    const content = state.layout === "table" ? fd.querySelector("table") : fd.querySelector(".bp-page");
    const width = content?.offsetWidth || page.width;
    const height = content?.offsetHeight || page.height;
    const byWidth = (stage.clientWidth - 48) / width;
    const byHeight = (stage.clientHeight - 48) / height;
    factor = state.zoom === "width" ? byWidth : Math.min(byWidth, byHeight);
    factor = Math.max(0.25, Math.min(factor, 3));
  }
  fd.documentElement.style.zoom = String(factor);
}

function stepZoom(direction) {
  const current = Number(frameDocument()?.documentElement.style.zoom) || 1;
  const steps = direction > 0 ? ZOOM_STEPS.filter((z) => z > current + 0.01) : ZOOM_STEPS.filter((z) => z < current - 0.01).reverse();
  if (!steps.length) return;
  state.zoom = String(steps[0]);
  $("zoom-select").value = state.zoom;
  applyZoom();
  saveToAddress();
}

// ---- toolbar ----------------------------------------------------------------------

function setupToolbar() {
  $("first-page").addEventListener("click", () => loadPage(1));
  $("prev-page").addEventListener("click", () => loadPage(state.page - 1));
  $("next-page").addEventListener("click", () => loadPage(state.page + 1));
  $("last-page").addEventListener("click", () => loadPage(state.shown?.info.pageCount ?? 1));
  $("page-input").addEventListener("change", (e) => loadPage(Number(e.target.value) || 1));

  $("layout-paper").addEventListener("click", () => setLayout("paper"));
  $("layout-table").addEventListener("click", () => setLayout("table"));

  $("zoom-select").addEventListener("change", (e) => { state.zoom = e.target.value; applyZoom(); saveToAddress(); });
  $("zoom-in").addEventListener("click", () => stepZoom(1));
  $("zoom-out").addEventListener("click", () => stepZoom(-1));
  addEventListener("resize", () => { if (state.zoom === "width" || state.zoom === "page") applyZoom(); });

  // Print the page on the screen: the iframe holds that page only.
  $("print-button").addEventListener("click", () => {
    const win = $("page-frame").contentWindow;
    if (!win || !state.shown) return;
    win.focus();
    win.print();
  });

  $("conditions").addEventListener("submit", (e) => { e.preventDefault(); showReport(1); closeSidebar(); });
  $("message-retry").addEventListener("click", () => state.retry?.());
  document.addEventListener("keydown", onKey);
}

function setLayout(layout) {
  if (state.layout === layout) return;
  state.layout = layout;
  updateLayoutButtons();
  if (state.shown) loadPage(state.page);
}

function updateLayoutButtons() {
  $("layout-paper").setAttribute("aria-checked", String(state.layout === "paper"));
  $("layout-table").setAttribute("aria-checked", String(state.layout === "table"));
}

function updateToolbar() {
  const shown = !!state.shown;
  const count = state.shown?.info.pageCount ?? 0;
  $("page-input").value = shown ? state.page : "";
  $("page-input").max = String(count || 1);
  $("page-input").disabled = !shown;
  $("page-count").textContent = shown ? `of ${count}` : "of -";
  $("first-page").disabled = $("prev-page").disabled = !shown || state.page <= 1;
  $("next-page").disabled = $("last-page").disabled = !shown || state.page >= count;
  $("print-button").disabled = !shown;
  $("welcome").hidden = shown;

  for (const [id, pages] of [["pdf-all", null], ["pdf-page", state.page]]) {
    const link = $(id);
    if (!shown) {
      link.setAttribute("aria-disabled", "true");
      link.removeAttribute("href");
      continue;
    }
    const { report, query } = state.shown;
    link.removeAttribute("aria-disabled");
    link.href = `/api/reports/${report.id}/pdf?${query}` + (pages ? `&pages=${pages}` : "");
  }
}

function onKey(e) {
  if (!state.shown) return;
  const target = e.target;
  if (target instanceof HTMLElement && target.matches?.("input, select, textarea")) return;
  const keys = {
    ArrowLeft: () => loadPage(state.page - 1),
    PageUp: () => loadPage(state.page - 1),
    ArrowRight: () => loadPage(state.page + 1),
    PageDown: () => loadPage(state.page + 1),
    Home: () => loadPage(1),
    End: () => loadPage(state.shown.info.pageCount),
  };
  if (keys[e.key] && !e.ctrlKey && !e.altKey && !e.metaKey) {
    e.preventDefault();
    keys[e.key]();
  }
}

// ---- side panel on narrow screens ---------------------------------------------------

function setupSidebar() {
  $("menu-button").addEventListener("click", () => {
    const open = !$("sidebar").classList.contains("open");
    $("sidebar").classList.toggle("open", open);
    $("scrim").hidden = !open;
    $("menu-button").setAttribute("aria-expanded", String(open));
  });
  $("scrim").addEventListener("click", closeSidebar);
}

function closeSidebar() {
  $("sidebar").classList.remove("open");
  $("scrim").hidden = true;
  $("menu-button").setAttribute("aria-expanded", "false");
}

// ---- information, messages ------------------------------------------------------------

function showInfo() {
  const field = (name) => $("render-info").querySelector(`[data-field="${name}"]`);
  const info = state.shown?.info;
  if (!info) return;
  field("pages").textContent = number(info.pageCount);
  field("rows").textContent = number(info.rowCount);
  field("render").textContent = info.fromMemory
    ? `from memory (took ${number(info.renderMilliseconds)} ms)`
    : `${number(info.renderMilliseconds)} ms`;
  field("page").textContent = state.pageBytes < 0
    ? "from the browser's cache"
    : state.pageBytes > 0 ? `${(state.pageBytes / 1024).toFixed(1)} KB of HTML` : "-";
}

function showBusy(text) {
  $("overlay-text").textContent = text;
  $("message").hidden = true;
  $("overlay").hidden = false;
}

function showMessage(title, detail, retry) {
  state.retry = retry;
  $("message-title").textContent = title;
  $("message-detail").textContent = detail;
  $("message-retry").hidden = !retry;
  $("overlay").hidden = true;
  $("message").hidden = false;
}

function hideOverlays() {
  $("overlay").hidden = true;
  $("message").hidden = true;
}

// The address bar holds the report, its conditions, the page and the layout, so a
// link opens the same page and Reload keeps it.
function saveToAddress() {
  if (!state.shown) return;
  const q = new URLSearchParams({ report: state.shown.report.id });
  for (const [k, v] of state.shown.query) q.set(k, v);
  q.set("page", String(state.page));
  if (state.layout !== "paper") q.set("layout", state.layout);
  if (state.zoom !== "width") q.set("zoom", state.zoom);
  history.replaceState(null, "", "?" + q);
}

// ---- HTTP -------------------------------------------------------------------------------

async function getJson(url) {
  const response = await fetch(url, { headers: { Accept: "application/json" } });
  if (!response.ok) throw new Error(await problemText(response));
  return response.json();
}

// The API answers an error with application/problem+json: "detail" is a sentence for the reader.
async function problemText(response) {
  try {
    const problem = await response.json();
    return problem.detail || problem.title || `${response.status} ${response.statusText}`;
  } catch {
    return `The server answered ${response.status} ${response.statusText}.`;
  }
}

function number(n) {
  return Number(n).toLocaleString("en-US");
}
