"use strict";

const $ = (selector, root = document) => root.querySelector(selector);
const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];
const esc = value => String(value ?? "").replace(/[&<>"']/g, char => ({"&":"&amp;", "<":"&lt;", ">":"&gt;", '"':"&quot;", "'":"&#39;"}[char]));
const normalize = text => String(text).normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase();
const icons = {"Všechny zvuky":"▦", "Pohyb a povrchy":"♧", "Zbraně a zásahy":"⌁", "Postavy":"♙", "Prostředí":"⌂", "Pickupy":"◇", "Zápas a UI":"▤", "Hlášky":"☷", "Už ve hře":"✓"};
let data, selected = "pova_weapon", group = "Postavy", filter = "suggested", query = "", includeOptional = false;
let fileMap = new Map(), fileSelection = new Set(), fileQuery = "", fileFilter = "inbox";
let saveChain = Promise.resolve(), pendingSaves = 0, refreshing = false, noteTimer, pendingNote, saveError = false;
let lastFileSignature = "", toastTimer;
const noteDrafts = new Map();
let recommendationMap = new Map(), candidateQuery = "", candidateTier = "", candidateSource = "", initialViewOpened = false;

function itemById(id) { return data?.catalog.items.find(item => item.id === id); }
function entry(item) {
  return data.state.items[item.id] ?? {files:item.existing ?? [], done:!!item.existing?.length, skipped:false, note:""};
}
function status(item) {
  const value = entry(item);
  if (value.files.some(id => !fileMap.get(id)?.exists)) return "broken";
  if (value.skipped) return "skipped";
  if (value.done && value.files.length) return "done";
  return value.files.length ? "candidate" : "missing";
}
const statusNames = {missing:"Chybí zvuk", candidate:"Přiřazeno k posouzení", done:"Hotovo", broken:"Chybí soubor", skipped:"Přeskočeno"};
const tierNames = {A:"Začít tady", B:"K úpravě / jako vrstva", C:"Doplněk"};
function recommendedFor(item) {
  return (data.recommendations || []).filter(row => row.item_ids.includes(item.id) && fileMap.get(row.file)?.exists);
}
function targetName(item) { return (item.section ? item.section + " / " : "") + item.title; }

function toast(text) {
  clearTimeout(toastTimer); $("#toast").textContent = text; $("#toast").hidden = false;
  toastTimer = setTimeout(() => { $("#toast").hidden = true; }, 4000);
}
function showError(message) {
  const banner = $("#error-banner");
  banner.replaceChildren(document.createTextNode(message));
  const retry = document.createElement("button"); retry.textContent = "Obnovit přehled";
  retry.addEventListener("click", () => refresh(true)); banner.append(retry); banner.hidden = false;
}
function renderSaveStatus() {
  $("#save-status").textContent = saveError ? "Chyba uložení" : pendingSaves || pendingNote ? "Ukládám…" : "✓ Uloženo v projektu";
}
async function request(path, options) {
  const response = await fetch(path, {cache:"no-store", ...options});
  let result;
  try { result = await response.json(); } catch { throw new Error("Přehled nedostal platnou odpověď. Je místní aplikace spuštěná?"); }
  if (!response.ok) throw new Error(result.error || "Požadavek se nepovedl.");
  return result;
}
function updateFileMap() {
  fileMap = new Map(data.files.map(file => [file.id, file]));
  recommendationMap = new Map((data.recommendations || []).map(row => [row.file, row]));
}

async function refresh(force = false) {
  if (refreshing || (!force && (pendingSaves || pendingNote || document.visibilityState === "hidden"))) return;
  refreshing = true;
  try {
    const result = await request("/api/snapshot");
    // A request started before a save must not replace a newer in-memory revision.
    if (data && result.state.revision < data.state.revision) return;
    const signature = result.files.map(file => `${file.id}:${file.size}:${file.modified}:${file.exists}`).join("|") + JSON.stringify(result.recommendations || []);
    const changed = !data || result.state.revision !== data.state.revision || signature !== lastFileSignature;
    const oldIds = new Set(data?.files.filter(file => file.inbox).map(file => file.id));
    const newFiles = data ? result.files.filter(file => file.inbox && !oldIds.has(file.id)) : [];
    data = result; updateFileMap(); lastFileSignature = signature;
    $("#error-banner").hidden = true;
    if (changed || force) {
      renderSummary(); renderGroups(); renderList();
      // Polling should not interrupt playing audio or an unfinished text edit.
      if (force || !$("#detail").contains(document.activeElement)) renderDetail();
      if ($("#file-dialog").open) renderFiles();
      if ($("#candidate-dialog").open) renderCandidateLibrary();
    }
    if (newFiles.length) toast(`Ve složce ${newFiles.length === 1 ? "přibyl nový zvuk" : "přibyly nové zvuky"}: ${newFiles.length}`);
    if (result.warnings.length) showError(result.warnings[0]);
    renderSaveStatus();
    if (!initialViewOpened) {
      initialViewOpened = true;
      const params = new URLSearchParams(location.search);
      if (["gdc", "candidates"].includes(params.get("view"))) openCandidateLibrary(params.get("source") || (params.get("view") === "gdc" ? "gdc2026" : ""));
      else if (itemById(params.get("item"))) focusItem(params.get("item"));
    }
  } catch (error) {
    showError("Místní přehled není dostupný. Spusť Zvuky.cmd a pak obnov tuto stránku. " + error.message);
    $("#save-status").textContent = "Přehled není připojený";
  } finally { refreshing = false; }
}

function save(action, payload, {detail = true} = {}) {
  if (detail && pendingNote) flushNote();
  pendingSaves++; renderSaveStatus();
  const job = saveChain.catch(() => {}).then(async () => {
    try {
      const result = await request("/api/" + action, {method:"POST", headers:{"Content-Type":"application/json", "X-Collector-Token":data.token}, body:JSON.stringify({...payload, revision:data.state.revision})});
      data.state = result.state; saveError = false;
      renderSummary(); renderGroups(); renderList();
      if (detail) renderDetail();
      return true;
    } catch (error) { saveError = true; showError(error.message); return false; }
    finally { pendingSaves--; renderSaveStatus(); }
  });
  saveChain = job;
  return job;
}
function saveItem(id, changes, options) { return save("item", {id, changes}, options); }
function flushNote() {
  clearTimeout(noteTimer);
  if (!pendingNote) return;
  const note = pendingNote; pendingNote = null;
  return saveItem(note.id, {note:note.text}, {detail:false}).then(ok => {
    if (ok && noteDrafts.get(note.id) === note.text) noteDrafts.delete(note.id);
  });
}

function renderSummary() {
  const collected = data.catalog.items.filter(item => item.group !== "Už ve hře" && !item.optional);
  const done = collected.filter(item => status(item) === "done").length;
  const skipped = collected.filter(item => status(item) === "skipped").length;
  const candidates = data.catalog.items.filter(item => item.group !== "Už ve hře" && entry(item).files.length).length;
  const inbox = data.files.filter(file => file.inbox && file.exists);
  $("#progress-text").textContent = `${done} / ${collected.length} hotovo`;
  $("#progress").max = Math.max(collected.length, 1); $("#progress").value = done;
  $("#progress-subtitle").textContent = `Hlavní seznam · doplňky a hotové původní nahrávky se nepočítají${skipped ? ` · ${skipped} přeskočeno` : ""}`;
  $("#candidate-count").textContent = candidates;
  $("#inbox-count").textContent = inbox.length;
  const availableCandidates = (data.recommendations || []).filter(row => fileMap.get(row.file)?.exists).length;
  $("#unassigned-count").textContent = availableCandidates;
  $("#gdc-count").textContent = availableCandidates;
  $("#folder-path").textContent = data.folder; $("#folder-path").title = data.folder;
  $("#folder-button").title = "Otevřít " + data.folder;
  $("#folder-caption").textContent = inbox.length ? `${inbox.length} nahrávek · automatická kontrola` : "Sem stahuj nové nahrávky";
  $("#help-folder").textContent = "Tvoje zvuková složka: " + data.folder;
}

function renderGroups() {
  $("#groups").innerHTML = Object.keys(icons).map(name => {
    const pool = data.catalog.items.filter(item => (name === "Všechny zvuky" ? item.group !== "Už ve hře" : item.group === name) && (includeOptional || !item.optional));
    const count = pool.filter(item => !["done","skipped"].includes(status(item))).length;
    return `${name === "Už ve hře" ? '<div class="nav-divider"></div>' : ""}<button class="nav-button ${group === name ? "active" : ""}" data-group="${esc(name)}" ${group === name ? 'aria-current="page"' : ""}><span class="nav-icon" aria-hidden="true">${icons[name]}</span><span>${esc(name)}</span><span class="nav-count">${name === "Už ve hře" ? pool.length : count}</span></button>`;
  }).join("");
}

function visibleItems() {
  return data.catalog.items.filter(item => {
    if (group === "Všechny zvuky" ? item.group === "Už ve hře" : item.group !== group) return false;
    if (!includeOptional && item.optional) return false;
    const current = status(item);
    if (filter === "remaining" && ["done", "skipped"].includes(current)) return false;
    if (filter === "suggested" && (!recommendedFor(item).length || ["done", "skipped"].includes(current))) return false;
    if (filter === "candidate" && !entry(item).files.length) return false;
    if (filter === "done" && current !== "done") return false;
    return !query || normalize([item.title, item.section, item.description, item.filename, item.group, entry(item).note].join(" ")).includes(normalize(query));
  });
}

function renderList() {
  const items = visibleItems();
  $("#group-title").textContent = group; $("#visible-count").textContent = items.length;
  $$("#status-filters button").forEach(button => { button.classList.toggle("active", button.dataset.filter === filter); button.setAttribute("aria-pressed", String(button.dataset.filter === filter)); });
  if (!items.length) {
    $("#task-list").innerHTML = '<div class="empty-state"><strong>Tady máš čisto.</strong>Tomuto filtru neodpovídá žádná položka.<br>Zkus „Vše“, jinou skupinu nebo zapni doplňky.</div>';
    return;
  }
  let section;
  $("#task-list").innerHTML = items.map(item => {
    const current = status(item), value = entry(item), label = item.section || item.group;
    const heading = label !== section ? `<div class="section-label">${esc(label)}</div>` : "";
    section = label;
    const canComplete = value.files.length > 0 && value.files.every(file => fileMap.get(file)?.exists);
    return `${heading}<div class="task-row ${selected === item.id ? "selected" : ""} ${current === "done" ? "done-row" : ""}">
      <input class="task-check" type="checkbox" data-check="${esc(item.id)}" aria-label="Hotovo: ${esc(item.title)}" ${current === "done" ? "checked" : ""} ${canComplete ? "" : 'disabled title="Nejdřív přiřaď zvuk"'}>
      <button class="task-open" data-select="${esc(item.id)}" aria-pressed="${selected === item.id}"><span class="task-title">${esc(item.title)}</span><span class="task-subtitle"><span class="status-dot ${current}"></span>${current === "missing" && recommendedFor(item).length ? `${recommendedFor(item).length} doporučení k poslechu` : statusNames[current]}${value.files.length ? ` · ${value.files.length} soub.` : ""}${item.priority === 1 ? " · Nejdřív" : ""}${item.optional ? " · Doplněk" : ""}</span></button><span class="task-arrow" aria-hidden="true">›</span></div>`;
  }).join("");
}

function audioElement(file) {
  if (!file?.exists) return `<p class="broken-message">${file?.pointer ? "Je tu pouze Git LFS odkaz. Skutečná nahrávka ještě není stažená." : "Soubor už ve složce není. Pokud jsi ho přejmenoval, přiřaď nový název."}</p>`;
  return `<audio controls preload="none" src="/api/audio?id=${encodeURIComponent(file.id)}&amp;v=${file.modified}" aria-label="Přehrát ${esc(file.name)}"></audio>`;
}

function attachment(id, index) {
  const file = fileMap.get(id), meta = data.state.files[id] || {};
  return `<article class="attachment-card"><div class="attachment-name"><span class="file-type">${esc(file?.format || "?")}</span><strong>${esc(file?.name || id)}</strong><button class="icon-button" data-detach="${esc(id)}" aria-label="Odebrat přiřazení ${esc(file?.name || id)}" title="Odebrat přiřazení; soubor zůstane ve složce">✕</button></div>
    <div class="file-location">${file?.inbox ? "sounds_dir / " : "Už v projektu / "}${esc(file?.relative || id)}</div>${audioElement(file)}
    <details class="source-details"><summary>Zdroj a licence ${meta.source || meta.license ? "· vyplněno" : "· volitelné"}</summary><form class="source-fields" data-source="${esc(id)}">
      <label for="source-${index}">Odkaz na původní stránku<input id="source-${index}" name="source" type="url" placeholder="https://freesound.org/…" value="${esc(meta.source)}" maxlength="2000"></label>
      <div class="source-row"><label for="license-${index}">Licence<input id="license-${index}" name="license" placeholder="Např. CC0, CC BY, Sonniss…" value="${esc(meta.license)}" maxlength="2000"></label><label for="author-${index}">Autor / knihovna<input id="author-${index}" name="author" placeholder="Jméno autora nebo balíčku" value="${esc(meta.author)}" maxlength="2000"></label></div>
      <div><button type="submit" class="button tiny">Uložit zdroj</button>${/^https?:\/\//i.test(meta.source || "") ? `<a class="source-link" href="${esc(meta.source)}" target="_blank" rel="noopener noreferrer">Otevřít zdroj ↗</a>` : ""}</div>
    </form></details></article>`;
}

function recommendationCard(row, library = false) {
  const file = fileMap.get(row.file);
  const assignedHere = entry(itemById(selected)).files.includes(row.file);
  const targets = row.item_ids.map(itemById).filter(Boolean);
  const duration = Number.isFinite(row.seconds) ? `${row.seconds.toFixed(1)} s` : "";
  return `<article class="recommendation-card"><div class="recommendation-meta"><span class="tier-badge tier-${esc(row.tier)}">${esc(row.source_label || "GDC 2026")} · ${esc(row.tier)} · ${tierNames[row.tier]}</span><span>${esc(duration)}</span></div>
    <h4>${esc(library ? targetName(targets[0] || {title:file?.name || row.file}) : file?.name || row.file)}</h4>
    ${library ? `<code>${esc(file?.name || row.file)}</code>` : ""}<p>${esc(row.note)}</p>${audioElement(file)}
    ${library ? `<div class="candidate-targets"><small>PŘIŘADIT K POUŽITÍ:</small>${targets.map(item => {
      const assigned = entry(item).files.includes(row.file);
      return `<button class="button tiny ${assigned ? "quiet" : ""}" ${assigned ? `data-open-item="${esc(item.id)}"` : `data-assign-item="${esc(item.id)}" data-assign-file="${esc(row.file)}"`}>${assigned ? "✓ Přiřazeno · " : "＋ "}${esc(targetName(item))}${assigned ? " ↗" : ""}</button>`;
    }).join("")}</div>` : `<button class="button tiny ${assignedHere ? "quiet" : "primary"}" data-assign-item="${esc(selected)}" data-assign-file="${esc(row.file)}" ${assignedHere ? "disabled" : ""}>${assignedHere ? "✓ Už přiřazeno" : "＋ Přiřadit k této položce"}</button>`}
    <details class="candidate-origin"><summary>Původní soubor</summary><p>${esc(row.original)}</p></details></article>`;
}

function renderCandidateLibrary() {
  const sourceSelect = $("#candidate-source");
  const sources = new Map((data.recommendations || []).map(row => [row.source_id || "gdc2026", row.source_label || "GDC 2026"]));
  const options = '<option value="">Všechny knihovny</option>' + [...sources].map(([id,label]) => `<option value="${esc(id)}">${esc(label)}</option>`).join("");
  if (sourceSelect.innerHTML !== options) sourceSelect.innerHTML = options;
  if (candidateSource && !sources.has(candidateSource)) candidateSource = "";
  sourceSelect.value = candidateSource;
  const rows = (data.recommendations || []).filter(row => {
    if (!fileMap.get(row.file)?.exists || (candidateTier && row.tier !== candidateTier)) return false;
    if (candidateSource && (row.source_id || "gdc2026") !== candidateSource) return false;
    const text = [fileMap.get(row.file)?.name, row.source_label, row.note, row.original, ...row.item_ids.map(id => targetName(itemById(id)))].join(" ");
    return !candidateQuery || normalize(text).includes(normalize(candidateQuery));
  });
  $("#candidate-count-label").textContent = `${rows.length} nahrávek · doporučení podle názvů a metadat, zvukovou shodu posuď poslechem.`;
  $("#candidate-list").innerHTML = rows.length ? rows.map(row => recommendationCard(row, true)).join("") : '<div class="empty-state">Žádní kandidáti pro tento filtr. Zkus jiný název nebo skupinu.</div>';
}

function openCandidateLibrary(source = "") {
  if (!data) return;
  flushNote(); candidateQuery = ""; candidateTier = ""; candidateSource = typeof source === "string" ? source : "";
  $("#candidate-search").value = ""; $("#candidate-tier").value = "";
  renderCandidateLibrary(); $("#candidate-dialog").showModal();
}

function focusItem(id) {
  const item = itemById(id); if (!item) return;
  flushNote(); selected = id; group = item.group; filter = "all"; query = "";
  if (item.optional) { includeOptional = true; $("#optional-toggle").checked = true; }
  $("#item-search").value = "";
  renderGroups(); renderList(); renderDetail();
  $("#task-list").querySelector(`[data-select="${CSS.escape(id)}"]`)?.scrollIntoView({block:"nearest"});
  $("#detail").scrollTop = 0;
  if (window.innerWidth < 960) $("#detail").scrollIntoView({behavior:"smooth", block:"start"});
}

function renderDetail() {
  const item = itemById(selected);
  if (!item) return;
  const value = entry(item), current = status(item);
  const canComplete = value.files.length > 0 && value.files.every(id => fileMap.get(id)?.exists);
  const recommendations = recommendedFor(item).filter(row => !value.files.includes(row.file));
  $("#detail").innerHTML = `<div class="detail-meta"><span>${esc(item.group)}${item.section ? " / " + esc(item.section) : ""}</span><span class="priority ${item.optional ? "optional" : ""}">${item.optional ? "Volitelný doplněk" : item.priority === 1 ? "Začni tady" : item.group === "Už ve hře" ? "Už máme" : "Hlavní seznam"}</span></div>
    <h2>${esc(item.title)}</h2><p class="detail-description">${esc(item.description)}</p>
    <div class="selection-confirmation"><label class="done-label"><input id="detail-done" type="checkbox" ${current === "done" ? "checked" : ""} ${canComplete ? "" : "disabled"}><span>Hotovo — výběr pro tuto položku mám<small>${canComplete ? "Potvrď, až máš všechny potřebné varianty nebo vrstvy. Ukládá se automaticky." : "Nejdřív přehraj doporučení níže a klikni na Přiřadit."}</small></span></label></div>
    ${value.files.length ? `<div class="attachments-heading"><h3>Tvůj výběr <span>(${value.files.length})</span></h3></div><div id="attachments">${value.files.map(attachment).join("")}</div>` : '<div id="attachments"></div>'}
    <div class="recommendations-heading"><h3>Doporučené zvuky <span>(${recommendations.length})</span></h3><p>▶ Přehraj si ukázku. Když se hodí, klikni na Přiřadit. Můžeš vybrat víc variant.</p></div>
    <div id="recommended-files">${recommendations.length ? recommendations.map(row => recommendationCard(row)).join("") : `<div class="no-recommendations">${recommendedFor(item).length ? "Všechna doporučení už máš přiřazená výše. Můžeš potvrdit Hotovo, nebo přidat jiný soubor." : "Pro tuto položku zatím nemáme doporučení z knihoven. Vlastní nahrávku přidej tlačítkem níže."}</div>`}</div>
    <div class="manual-attach"><button id="assign-button" class="button tiny">＋ Přidat jiný soubor</button><small>Pro vlastní stažené nahrávky a další varianty.</small></div>
    <details class="naming-details"><summary>Jak soubor pojmenovat?</summary><div class="filename-hint"><div><small>DOPORUČENÝ NÁZEV · NENÍ NUTNÉ PŘEJMENOVÁVAT</small><code>${esc(item.filename)}</code></div><button id="copy-filename" class="icon-button" aria-label="Zkopírovat doporučený název" title="Zkopírovat název">⧉</button></div></details>
    <label class="note-field" for="item-note"><span class="note-label">Tvoje poznámka <small>ukládá se automaticky</small></span><textarea id="item-note" maxlength="8000" placeholder="Např. tři dobré varianty, stáhnout i dopad…">${esc(noteDrafts.get(item.id) ?? value.note)}</textarea></label>
    <div class="detail-bottom"><button id="skip-button" class="skip-button">${value.skipped ? "Vrátit do seznamu" : "Teď nepotřebuji · přeskočit"}</button></div>`;
}

function formatSize(size) { return size >= 1048576 ? (size / 1048576).toFixed(1) + " MB" : Math.max(1, Math.round(size / 1024)) + " kB"; }
function suggestion(file, item) {
  if (!item) return 0;
  if (recommendationMap.get(file.id)?.item_ids.includes(item.id)) return 100;
  const name = normalize(file.name).replace(/[^a-z0-9]+/g, " ");
  const tokens = normalize(item.filename + " " + item.search).replace(/[^a-z0-9]+/g, " ").split(" ").filter(t => t.length > 2 && !["wav", "mp3", "ogg", "loop", "single"].includes(t));
  return [...new Set(tokens)].reduce((sum, token) => sum + (name.includes(token) ? 1 : 0), 0);
}
function renderFiles() {
  const item = itemById(selected), used = new Set(data.catalog.items.flatMap(task => entry(task).files));
  const assigned = new Set(entry(item).files);
  const files = data.files.filter(file => {
    if (!file.exists) return false;
    if (fileFilter === "existing" ? file.inbox : !file.inbox) return false;
    if (fileFilter === "unassigned" && used.has(file.id)) return false;
    return !fileQuery || normalize(file.relative).includes(normalize(fileQuery));
  }).sort((a,b) => suggestion(b,item) - suggestion(a,item) || b.modified - a.modified || a.name.localeCompare(b.name));
  $("#file-dialog-subtitle").textContent = `Přiřazení k položce: ${item.title}`;
  $("#file-list").innerHTML = files.length ? files.map((file, index) => {
    const score = suggestion(file, item), inItem = assigned.has(file.id), recommendation = recommendationMap.get(file.id);
    return `<div class="file-option ${fileSelection.has(file.id) ? "selected" : ""}"><input id="pick-${index}" type="checkbox" data-file="${esc(file.id)}" ${fileSelection.has(file.id) ? "checked" : ""} ${inItem ? "disabled" : ""} aria-label="Vybrat ${esc(file.name)}"><div class="file-option-body"><label for="pick-${index}">${esc(file.name)}${score === 100 ? '<span class="suggested">Doporučeno pro tuto položku</span>' : score >= 2 ? '<span class="suggested">Podobný název</span>' : ""}</label><small>${esc(file.format)} · ${formatSize(file.size)} · ${inItem ? "U této položky už přiřazeno" : used.has(file.id) ? "Používá se i jinde — můžeš přiřadit znovu" : "Zatím nepřiřazeno"}${file.relative !== file.name ? " · " + esc(file.relative) : ""}</small>${recommendation ? `<p class="file-recommendation-note">${esc(recommendation.note)}</p>` : ""}${audioElement(file)}</div></div>`;
  }).join("") : `<div class="empty-state"><div class="empty-graphic" aria-hidden="true">▱</div><strong>${fileQuery ? "Žádný odpovídající soubor" : fileFilter === "unassigned" ? "Všechny nahrávky už mají místo" : "Složka je zatím prázdná"}</strong>${fileQuery ? "Zkus kratší název nebo jiný filtr." : "Stáhni WAV, OGG, MP3 nebo jiný zvuk do sounds_dir.<br>Balíčky ZIP nejdřív rozbal. Pak klikni na Obnovit."}</div>`;
  renderSelectionCount();
}
function renderSelectionCount() {
  $("#selection-count").textContent = `${fileSelection.size} vybraných`;
  $("#attach-button").disabled = !fileSelection.size;
}
function openFiles() {
  flushNote(); fileSelection.clear(); fileQuery = ""; $("#file-search").value = "";
  fileFilter = "inbox"; $("#file-filter").value = "inbox";
  renderFiles(); $("#file-dialog").showModal();
}

$("#groups").addEventListener("click", event => {
  const button = event.target.closest("[data-group]"); if (!button || !data) return;
  flushNote(); group = button.dataset.group; filter = group === "Už ve hře" ? "all" : "remaining";
  query = ""; $("#item-search").value = "";
  const first = visibleItems()[0]; if (first) selected = first.id;
  renderGroups(); renderList(); renderDetail(); $("#task-list").scrollTop = 0;
});
$("#task-list").addEventListener("click", event => {
  const button = event.target.closest("[data-select]"); if (!button) return;
  flushNote(); selected = button.dataset.select; renderList(); renderDetail();
  if (window.innerWidth < 960) $("#detail").scrollIntoView({behavior:"smooth", block:"start"});
});
$("#task-list").addEventListener("change", event => {
  if (event.target.dataset.check) saveItem(event.target.dataset.check, {done:event.target.checked, skipped:false});
});
$("#status-filters").addEventListener("click", event => {
  const button = event.target.closest("[data-filter]"); if (!button || !data) return;
  filter = button.dataset.filter; renderList();
});
$("#item-search").addEventListener("input", event => { if (data) { query = event.target.value; renderList(); } });
$("#optional-toggle").addEventListener("change", event => { if (data) { includeOptional = event.target.checked; renderGroups(); renderList(); } });
$("#start-example").addEventListener("click", () => { if (data) focusItem("pova_weapon"); });
$("#browse-candidates").addEventListener("click", openCandidateLibrary);
$("#gdc-metric").addEventListener("click", openCandidateLibrary);
$("#candidate-search").addEventListener("input", event => { candidateQuery = event.target.value; renderCandidateLibrary(); });
$("#candidate-tier").addEventListener("change", event => { candidateTier = event.target.value; renderCandidateLibrary(); });
$("#candidate-source").addEventListener("change", event => { candidateSource = event.target.value; renderCandidateLibrary(); });
document.addEventListener("click", async event => {
  if (!data) return;
  const open = event.target.closest("[data-open-item]");
  if (open) { $("#candidate-dialog").close(); focusItem(open.dataset.openItem); return; }
  const assign = event.target.closest("[data-assign-file]");
  if (!assign) return;
  const id = assign.dataset.assignItem, file = assign.dataset.assignFile;
  assign.disabled = true;
  await flushNote();
  const fromLibrary = $("#candidate-dialog").open;
  const ok = await save("assign", {id, file}, {detail:false});
  if (ok) {
    if (fromLibrary) { $("#candidate-dialog").close(); focusItem(id); }
    else renderDetail();
    toast(`Přiřazeno: ${targetName(itemById(id))}. Až máš výběr kompletní, potvrď Hotovo.`);
  } else assign.disabled = false;
});
$("#detail").addEventListener("click", async event => {
  if (!data) return;
  if (event.target.closest("#assign-button")) openFiles();
  if (event.target.closest("#copy-filename")) {
    try { await navigator.clipboard.writeText(itemById(selected).filename.split(" / ")[0]); toast("Doporučený název zkopírován."); }
    catch { toast("Název můžeš označit a zkopírovat ručně."); }
  }
  const remove = event.target.closest("[data-detach]");
  if (remove) { const value = entry(itemById(selected)); await saveItem(selected, {files:value.files.filter(id => id !== remove.dataset.detach), done:false}); toast("Přiřazení odebráno. Soubor zůstal ve složce."); }
  if (event.target.closest("#skip-button")) { const value = entry(itemById(selected)); saveItem(selected, {skipped:!value.skipped, done:false}); }
});
$("#detail").addEventListener("change", event => {
  if (event.target.id === "detail-done") saveItem(selected, {done:event.target.checked, skipped:false});
});
$("#detail").addEventListener("input", event => {
  if (event.target.id !== "item-note") return;
  pendingNote = {id:selected, text:event.target.value}; clearTimeout(noteTimer);
  noteDrafts.set(selected, event.target.value);
  renderSaveStatus(); noteTimer = setTimeout(flushNote, 600);
});
$("#detail").addEventListener("submit", async event => {
  const form = event.target.closest("[data-source]"); if (!form) return;
  event.preventDefault();
  const metadata = Object.fromEntries(new FormData(form));
  const ok = await save("file", {id:form.dataset.source, metadata}, {detail:false});
  if (ok) toast("Zdroj a licence uložené. Platí i u dalších použití stejného souboru.");
});
$("#library-button").addEventListener("click", () => { if (data) openFiles(); });
$("#file-search").addEventListener("input", event => { fileQuery = event.target.value; renderFiles(); });
$("#file-filter").addEventListener("change", event => { fileFilter = event.target.value; renderFiles(); });
$("#file-list").addEventListener("change", event => {
  const id = event.target.dataset.file; if (!id) return;
  if (event.target.checked) fileSelection.add(id); else fileSelection.delete(id);
  event.target.closest(".file-option").classList.toggle("selected", event.target.checked); renderSelectionCount();
});
$("#rescan-button").addEventListener("click", () => refresh(true));
$("#attach-button").addEventListener("click", async () => {
  const current = entry(itemById(selected));
  const ok = await saveItem(selected, {files:[...new Set([...current.files, ...fileSelection])], done:false, skipped:false});
  if (ok) { $("#file-dialog").close(); toast("Přiřazeno. Až si vybereš, zaškrtni Hotovo."); }
});
$("#backup-button").addEventListener("click", async () => {
  if (!data) return; await flushNote(); await saveChain;
  if (saveError) { toast("Nejdřív vyřeš chybu uložení, aby záloha obsahovala poslední úpravy."); return; }
  const link = document.createElement("a"); link.href = "/api/export"; link.download = "bros-zvuky-zaloha.json"; link.click();
});
$("#folder-button").addEventListener("click", async () => {
  if (!data) return;
  try { await request("/api/open-folder", {method:"POST", headers:{"Content-Type":"application/json", "X-Collector-Token":data.token}, body:"{}"}); }
  catch (error) { showError(error.message); }
});
$("#help-button").addEventListener("click", () => $("#help-dialog").showModal());
$("#restore-button").addEventListener("click", () => $("#restore-input").click());
$("#restore-input").addEventListener("change", async event => {
  const file = event.target.files[0]; if (!file) return;
  try {
    if (file.size > 8_000_000) throw new Error("Záloha je příliš velká.");
    const state = JSON.parse(await file.text());
    if (state.version !== 1 || !state.items || !state.files) throw new Error("Tohle není záloha zvukové sbírky.");
    if (!confirm(`Obnovit přehled ze souboru „${file.name}“? Nahradí současná přiřazení, fajfky a poznámky. Zvukové soubory zůstanou beze změny. Současný přehled se uloží jako collection.json.bak.`)) return;
    await flushNote();
    const ok = await save("restore", {state});
    if (ok) { $("#help-dialog").close(); await refresh(true); toast("Přehled obnovený ze zálohy."); }
  } catch (error) { showError(error.message); }
  finally { event.target.value = ""; }
});
document.addEventListener("click", event => {
  const close = event.target.closest("[data-close]"); if (close) $("#" + close.dataset.close).close();
});
document.addEventListener("play", event => { if (event.target.tagName === "AUDIO") $$("audio").forEach(audio => { if (audio !== event.target) audio.pause(); }); }, true);
document.addEventListener("error", event => {
  if (event.target.tagName === "AUDIO") toast("Prohlížeč tento zvuk nepřehraje. Zkus WAV/MP3/OGG nebo zkontroluj stažený soubor; přiřazení můžeš uložit i tak.");
}, true);
document.addEventListener("keydown", event => {
  if (event.key === "/" && !["INPUT","TEXTAREA","SELECT"].includes(document.activeElement.tagName) && !$$("dialog").some(dialog => dialog.open)) { event.preventDefault(); $("#item-search").focus(); }
});
window.addEventListener("beforeunload", event => { if (pendingSaves || pendingNote || noteDrafts.size) { event.preventDefault(); event.returnValue = ""; } });
refresh(true);
setInterval(() => refresh(), 5000);
