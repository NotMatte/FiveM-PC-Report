"use strict";

// Talks to the C# host (Ui/MainForm.cs) through WebView2 messages. Every text from the host goes through
// esc() before reaching innerHTML.

const host = window.chrome && window.chrome.webview;
const send = (msg) => host && host.postMessage(msg);
const $ = (sel, root = document) => root.querySelector(sel);
const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));

const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) =>
  ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

const ICON_CHECK = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12.5l4.5 4.5L19 7.5"/></svg>';
const ICON_X = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round"><path d="M7 7l10 10M17 7L7 17"/></svg>';
const ICON_DASH = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round"><path d="M7 12h10"/></svg>';
const CIRC = 326.73;

const state = {
  running: false,
  live: false,
  seconds: 30,
  steps: [],
  stepState: {},
  liveTotal: 30,
};

// ---------- views ----------
function show(view) {
  $$(".view").forEach((v) => (v.hidden = v.dataset.view !== view));
  const order = ["start", "scan", "result"];
  const idx = order.indexOf(view);
  $$(".nav-step").forEach((n) => {
    const i = order.indexOf(n.dataset.step);
    n.classList.toggle("on", i === idx);
    n.classList.toggle("done", i < idx);
  });
  $(".main").scrollTop = 0;
}

function toast(text, bad = false) {
  const t = $("#toast");
  t.textContent = text;
  t.classList.toggle("bad", bad);
  t.classList.add("show");
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => t.classList.remove("show"), 2600);
}

// ---------- start view ----------
function setFivem(running) {
  state.running = running;
  const st = $("#fivem-status");
  st.classList.toggle("on", running);
  $(".status-text", st).textContent = running ? "FiveM aperto" : "FiveM chiuso";

  const toggle = $("#live-toggle");
  toggle.disabled = !running;
  if (!running && state.live) setLive(false);
  if (running && !setFivem.autoOn) { setFivem.autoOn = true; setLive(true); }
  $("#live-hint").textContent = running
    ? "FiveM è aperto: misuro cosa limita gli FPS mentre giochi, processore o scheda video."
    : "Apri FiveM ed entra nel server per misurare mentre giochi. Senza, controllo solo il PC.";
  updateStartNote();
}

function setLive(on) {
  state.live = on;
  $("#live-toggle").setAttribute("aria-checked", String(on));
  $("#live-seconds").classList.toggle("off", !on);
  updateStartNote();
}

function updateStartNote() {
  $("#start-note").textContent = state.live
    ? `Circa ${state.seconds + 10} secondi: dopo aver premuto, torna subito in gioco.`
    : "Ci vogliono circa 15 secondi.";
}

$("#live-toggle").addEventListener("click", () => setLive(!state.live));
$$("#live-seconds button").forEach((b) =>
  b.addEventListener("click", () => {
    $$("#live-seconds button").forEach((x) => x.classList.toggle("on", x === b));
    state.seconds = Number(b.dataset.s);
    updateStartNote();
  }));

$("#start").addEventListener("click", startScan);
$("#open-root").addEventListener("click", () => send({ type: "openFolder" }));

function startScan() {
  state.stepState = {};
  state.liveTotal = state.seconds;
  renderChecklist();
  $("#bar").style.width = "2%";
  $("#countdown").hidden = true;
  $("#scan-title").textContent = "Sto controllando il tuo PC";
  $("#scan-lead").textContent = state.live
    ? "Tra pochi secondi parte la misura dal vivo: torna in gioco e resta dove fai pochi FPS."
    : "Non chiudere la finestra.";
  show("scan");
  send({
    type: "scan",
    name: $("#f-name").value,
    problem: $("#f-problem").value,
    fps: $("#f-fps").value,
    live: state.live && state.running,
    seconds: state.seconds,
  });
}

// ---------- scan view ----------
function renderChecklist() {
  const list = $("#checklist");
  list.innerHTML = state.steps
    .filter((s) => s.id !== "live" || state.live)
    .map((s) => `<li data-id="${esc(s.id)}"><span class="check-ico"></span><span>${esc(s.label)}</span></li>`)
    .join("");
}

function onStep(id, st) {
  state.stepState[id] = st;
  const li = $(`#checklist li[data-id="${CSS.escape(id)}"]`);
  if (li) {
    li.className = st;
    $(".check-ico", li).innerHTML = st === "done" ? ICON_CHECK : st === "error" ? ICON_X : st === "skipped" ? ICON_DASH : "";
  }
  const visible = state.steps.filter((s) => s.id !== "live" || state.live).length;
  const finished = Object.values(state.stepState).filter((x) => x !== "running").length;
  $("#bar").style.width = Math.max(2, Math.round((finished / visible) * 100)) + "%";

  if (id === "live" && st === "running") {
    $("#countdown").hidden = false;
    $("#scan-title").textContent = "Misura dal vivo";
    $("#scan-lead").textContent = "Torna in gioco adesso. Resta nel punto dove gli FPS sono bassi, senza menu aperti.";
    setCountdown(state.liveTotal);
  }
  if (id === "live" && st !== "running") $("#countdown").hidden = true;
}

function setCountdown(left) {
  $("#cd-num").textContent = left;
  $("#cd-ring").style.strokeDashoffset = String(CIRC * (1 - left / state.liveTotal));
}

// ---------- result view ----------
const SEV = {
  critical: { label: "Grave", group: "Problemi gravi" },
  warning: { label: "Da sistemare", group: "Da sistemare" },
  info: { label: "Da sapere", group: "Da sapere" },
  good: { label: "A posto", group: "Controllato, a posto" },
};

function onDone(msg) {
  const a = msg.analysis;
  // score ring
  const score = a.score;
  const ring = $("#score-ring");
  ring.style.strokeDashoffset = String(CIRC);
  ring.style.stroke = score >= 75 ? "var(--ok)" : score >= 50 ? "var(--warn)" : "var(--bad)";
  requestAnimationFrame(() => requestAnimationFrame(() => {
    ring.style.strokeDashoffset = String(CIRC * (1 - score / 100));
  }));
  animateNumber($("#score-num"), score);

  const v = a.verdict;
  const vEl = $("#verdict");
  vEl.className = "card verdict k-" + v.kind;
  $("#verdict-kind").textContent = msg.live ? "Verdetto dalla misura dal vivo" : "Verdetto";
  $("#verdict-title").textContent = v.title;
  $("#verdict-text").textContent = v.text;

  const s = msg.summary;
  const chips = [
    ["Processore", s.cpu], ["Scheda video", s.gpu + (s.vram && s.vram !== "n/d" ? ` · ${s.vram}` : "")],
    ["RAM", s.ram], ["Schermo", s.display], ["Sistema", s.os],
  ].filter(([, val]) => val);
  $("#chips").innerHTML = chips.map(([k, val]) => `<span class="chip">${esc(k)} <b>${esc(val)}</b></span>`).join("");
  $("#folder-note").textContent = "Salvato in " + msg.folder;

  renderFindings(a.findings);
  renderData(msg.sections);
  selectTab("findings");
  show("result");
}

function animateNumber(el, to) {
  const start = performance.now();
  const dur = 900;
  const step = (t) => {
    const p = Math.min(1, (t - start) / dur);
    el.textContent = Math.round(to * (1 - Math.pow(1 - p, 3)));
    if (p < 1) requestAnimationFrame(step);
  };
  requestAnimationFrame(step);
}

function renderFindings(findings) {
  const problems = findings.filter((f) => f.severity !== "good").length;
  $("#count-findings").textContent = problems;
  const out = [];
  for (const sev of ["critical", "warning", "info", "good"]) {
    const items = findings.filter((f) => f.severity === sev);
    if (!items.length) continue;
    const cards = items.map((f) => `
      <article class="finding sev-${sev}">
        <div class="stripe"></div>
        <div>
          <div class="f-meta"><span class="badge">${esc(SEV[sev].label)}</span><span class="area">${esc(f.area)}</span></div>
          <div class="f-title">${esc(f.title)}</div>
          <p class="f-detail">${esc(f.detail)}</p>
          ${f.fix ? `<div class="f-fix"><b>Cosa fare</b>${esc(f.fix)}</div>` : ""}
        </div>
      </article>`).join("");
    const head = `<span class="dot sev-${sev}"></span>${esc(SEV[sev].group)} · ${items.length}`;
    out.push(sev === "good"
      ? `<details class="group"><summary class="group-head">${head}<span class="chev">›</span></summary><div class="findings">${cards}</div></details>`
      : `<section class="group"><div class="group-head">${head}</div><div class="findings">${cards}</div></section>`);
  }
  $("#findings").innerHTML = problems === 0 && !findings.length
    ? `<div class="card empty">Non ho trovato problemi.</div>`
    : out.join("") || `<div class="card empty">Non ho trovato problemi.</div>`;
}

function renderData(sections) {
  $("#data").innerHTML = `<div class="data-grid">${sections.map((s) => {
    const rows = s.rows.length
      ? `<dl class="kv">${s.rows.map((r) => `<dt>${esc(r[0])}</dt><dd>${esc(r[1] ?? "")}</dd>`).join("")}</dl>`
      : "";
    const tables = s.tables.filter((t) => t.rows.length).map((t) => `
      ${t.title ? `<div class="tbl-title">${esc(t.title)}</div>` : ""}
      <div class="tbl-wrap"><table>
        <thead><tr>${t.headers.map((h) => `<th>${esc(h)}</th>`).join("")}</tr></thead>
        <tbody>${t.rows.map((r) => `<tr>${r.map((c) => `<td>${esc(c)}</td>`).join("")}</tr>`).join("")}</tbody>
      </table></div>`).join("");
    return `<section class="card data-sec"><h3>${esc(s.title)}</h3>${rows}${tables}</section>`;
  }).join("")}</div>`;
}

function selectTab(tab) {
  $$(".tab").forEach((t) => t.classList.toggle("on", t.dataset.tab === tab));
  $$(".tab-panel").forEach((p) => (p.hidden = p.dataset.panel !== tab));
}
$$(".tab").forEach((t) => t.addEventListener("click", () => selectTab(t.dataset.tab)));

$("#copy").addEventListener("click", () => send({ type: "copyPrompt" }));
$("#open-report").addEventListener("click", () => send({ type: "openReport" }));
$("#open-folder").addEventListener("click", () => send({ type: "openFolder" }));
$("#again").addEventListener("click", () => show("start"));

// ---------- host messages ----------
if (host) {
  host.addEventListener("message", (e) => {
    const msg = e.data;
    switch (msg.type) {
      case "hello":
        $("#version").textContent = "versione " + msg.version;
        state.steps = msg.steps;
        setFivem(msg.running);
        break;
      case "fivem":
        setFivem(msg.running);
        break;
      case "step":
        onStep(msg.id, msg.state);
        break;
      case "tick":
        setCountdown(msg.left);
        break;
      case "done":
        onDone(msg);
        break;
      case "copied":
        toast("Prompt copiato: incollalo in Claude");
        break;
      case "error":
        toast(msg.message || "Qualcosa è andato storto", true);
        if (!$(".view[data-view='scan']").hidden) show("start");
        break;
    }
  });
  send({ type: "ready" });
} else {
  // Opened in a normal browser (design preview): show the start page with sample state.
  state.steps = [{ id: "machine", label: "Computer e scheda madre" }, { id: "live", label: "Analisi dal vivo del gioco" }];
  setFivem(false);
}
show("start");
