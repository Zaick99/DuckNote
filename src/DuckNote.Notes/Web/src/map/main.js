// La mappa della rete dentro al WebView di DuckNote (WKWebView sul Mac, WebView2 su Windows).
// Tutto quello che la finestra chiede passa da window.dm (InvokeScript / ExecuteScriptAsync);
// tutto quello che la mappa dice alla finestra passa da send(): un messaggio JSON per volta.

import { h, icon, fill, typingIn, modKey, isMac, shortcut } from '../common/dom.js';
import { ui as uiIcons } from '../common/icons.js';
import { NODE_W, NODE_H, GRID, parseVlans, boundsOf, centerOf, normalizeDoc } from './model.js';
import { Store, snap } from './store.js';
import { Scene } from './scene.js';
import { Pointer } from './interact.js';
import { Popups } from '../common/popups.js';
import { kindPicker } from './picker.js';
import { Library, segments, toolButton, textButton } from './panels.js';
import { Inspector } from './inspector.js';
import { arrange } from './layout.js';
import { mapPng, mapSvg, devicesTable, linksTable, fileName } from './exporter.js';
import { pressable, stop } from '../common/motion.js';
import { send, takeOutbox, activity, windowKey, applyTheme } from '../common/bridge.js';

// ------------------------------------------------------------------ la pagina

const store = new Store();
const app = h('div', { class: 'dm-app' });
const popupLayer = h('div', { class: 'dm-popups' });
const toolbar = h('header', { class: 'dm-toolbar' });
const stage = h('div', { class: 'dm-stage' });
const canvas = h('div', { class: 'dm-canvas', tabindex: '-1' });
const sink = h('textarea', { class: 'dm-sink', 'aria-label': 'Mappa della rete', tabindex: '0', spellcheck: 'false', autocomplete: 'off' });
stage.append(canvas);
document.body.append(app, popupLayer, sink);

const scene = new Scene(canvas, store);
const popups = new Popups(popupLayer);

let mode = 'move';
let clip = null;
let pasteCount = 0;

/** La tastiera alla mappa (e agli appunti): un campo nascosto, con un carattere selezionato. */
function focusSink() {
  if (document.activeElement !== sink) sink.focus({ preventScroll: true });
  sink.value = ' ';
  sink.select();
}

const pointer = new Pointer(scene, store, {
  mode: () => mode,
  focusSink,
  closePopups: () => popups.close(),
  kindPicker: (x, y, choose, title) => kindPicker(popups, x, y, choose, title),
  contextMenu: (x, y, hit, world) => contextMenu(x, y, hit, world),
  inspect: (field) => inspector.focus(field),
  changedView: () => viewChanged(),
});

// ------------------------------------------------------------------ barra in alto

const libraryToggle = toolButton(uiIcons.library, 'Mostra o nascondi la libreria', () => setLibrary(app.classList.contains('no-library')));
const mapButton = h('button', { class: 'dm-tool wide dm-map-button dm-press', type: 'button', title: 'Le mappe della nota' },
  icon(uiIcons.map, 15), h('span', { class: 'dm-map-name' }), icon(uiIcons.down, 12));
mapButton.addEventListener('click', () => (popups.current?.anchor === mapButton ? popups.close() : mapMenu()));

const modes = segments([
  { id: 'move', label: 'Sposta', icon: uiIcons.pointer, title: 'Seleziona e sposta (V)' },
  { id: 'connect', label: 'Collega', icon: uiIcons.link, title: 'Tira un cavo da un apparato all\'altro (C)' },
], (value) => setMode(value));

const zoneButton = textButton(uiIcons.zone, 'Zona', 'Una zona: attorno agli apparati selezionati, o una nuova al centro (Z)', () => addZone());
const undoButton = toolButton(uiIcons.undo, `Annulla (${shortcut('Z')})`, () => undo());
const redoButton = toolButton(uiIcons.redo, `Ripeti (${isMac() ? '⇧⌘Z' : 'Ctrl+Y'})`, () => redo());
const vlanButton = textButton(null, 'VLAN', 'Mostra solo una VLAN', () => (popups.current?.anchor === vlanButton ? popups.close() : vlanMenu()));
vlanButton.dataset.menuAlign = 'right';
const search = h('input', { class: 'dm-field dm-search', type: 'search', placeholder: 'Cerca', spellcheck: 'false', 'aria-label': 'Cerca un apparato' });
const searchBox = h('label', { class: 'dm-search-box' }, icon(uiIcons.search, 13), search);
const layoutButton = textButton(uiIcons.layout, 'Disponi', 'Mette in ordine la mappa: dall\'alto (Internet) verso il basso (gli apparati finali)', () => arrangeNow());
const exportButton = textButton(uiIcons.export, 'Esporta', 'Immagine, disegno o inventario', () => (popups.current?.anchor === exportButton ? popups.close() : exportMenu()));
exportButton.dataset.menuAlign = 'right';
const readonlyChip = h('span', { class: 'dm-chip tone-Orange', hidden: true }, 'Sola lettura');

toolbar.append(
  libraryToggle, mapButton, h('span', { class: 'dm-sep' }),
  modes.el, zoneButton, h('span', { class: 'dm-sep' }),
  undoButton, redoButton, readonlyChip,
  h('span', { class: 'dm-spacer' }),
  vlanButton, searchBox, layoutButton, exportButton);

// ------------------------------------------------------------------ libreria, scheda, zoom

const library = new Library(store, canvas, {
  addKind: (kind, x, y) => addKind(kind, x, y),
  addHost: (host, x, y) => addHost(host, x, y),
  addAll: (hosts) => addHosts(hosts ?? store.hosts),
  reveal: (id) => reveal(id),
  openNetwork: () => send({ t: 'network' }),
}, popups);

const inspector = new Inspector(store, {
  focusMap: focusSink,
  check: (host) => send({ t: 'check', host }),
  reveal: (host) => send({ t: 'reveal', host }),
  duplicate: () => duplicate(),
  remove: () => removeSelection(),
  align: (how) => align(how),
  spread: (axis) => spread(axis),
  arrange: () => arrangeNow(true),
  zoneAround: () => addZone(),
  connectSelected: () => connectSelected(),
  addHosts: (list) => addHosts(list),
  remind: (node) => {
    if (node) send({ t: 'remind', map: store.map.id, node: node.id, name: node.name, host: node.host });
  },
});

const zoomLabel = h('button', { class: 'dm-zoom-value', type: 'button', title: 'Zoom al 100%', onclick: () => zoomTo(1) });
const zoom = h('div', { class: 'dm-zoom' },
  toolButton(uiIcons.minus, `Riduci (${shortcut('-')})`, () => zoomBy(1 / 1.25)),
  zoomLabel,
  toolButton(uiIcons.add, `Ingrandisci (${shortcut('+')})`, () => zoomBy(1.25)),
  h('span', { class: 'dm-sep' }),
  toolButton(uiIcons.fit, `Adatta alla finestra (${shortcut('0')})`, () => fitAll()));

const statusLine = h('div', { class: 'dm-statusline' });

const emptyAdd = h('button', { class: 'dm-button primary dm-press', type: 'button', onclick: () => addHosts(store.hosts) });
const emptyScan = h('button', { class: 'dm-button dm-press', type: 'button', onclick: () => send({ t: 'network' }) }, icon(uiIcons.network, 14), h('span', {}, 'Analizza la rete'));
const empty = h('div', { class: 'dm-empty', hidden: true },
  h('div', { class: 'dm-empty-art' }),
  h('div', { class: 'dm-empty-title' }, 'Disegna la tua rete'),
  h('div', { class: 'dm-empty-text' }, 'Trascina un apparato dalla libreria, oppure parti dagli host che DuckNote ha gia\' trovato. Per collegare due apparati tira un cavo dal bordo dell\'uno all\'altro.'),
  h('div', { class: 'dm-empty-buttons' }, emptyAdd, emptyScan));

stage.append(empty, inspector.el, zoom, statusLine);
app.append(toolbar, h('div', { class: 'dm-main' }, library.el, stage));
pressable(document.body);

// ------------------------------------------------------------------ chi ascolta

let saveTimer = 0;
function scheduleSave() {
  clearTimeout(saveTimer);
  saveTimer = setTimeout(flush, 350);
}

function flush() {
  clearTimeout(saveTimer);
  saveTimer = 0;
  rememberView();
  send({ t: 'changed', doc: store.doc });
}

function rememberView() {
  const { x, y, k } = scene.view;
  store.map.view = { x: Math.round(x), y: Math.round(y), k: Math.round(k * 1000) / 1000 };
}

let viewTimer = 0;
function viewChanged() {
  // la vista si ricorda col prossimo salvataggio: scorrere da sola non riscrive la nota
  clearTimeout(viewTimer);
  viewTimer = setTimeout(rememberView, 400);
}

scene.onView((view) => {
  zoomLabel.textContent = `${Math.round(view.k * 100)}%`;
});

store.on((what) => {
  if (what === 'map') {
    scene.invalidate();
    scheduleSave();
    showChrome();
  } else if (what === 'select') {
    scene.invalidate();
  } else if (what === 'hosts') {
    scene.invalidate();
  } else if (what === 'doc') {
    showChrome();
  }
  inspector.refresh(what);
  if (what !== 'select') library.renderHosts();
  else showStatus();
});

function showChrome() {
  const map = store.map;
  mapButton.querySelector('.dm-map-name').textContent = map.name;
  undoButton.disabled = !store.canUndo;
  redoButton.disabled = !store.canRedo;
  const vlans = vlanList();
  vlanButton.hidden = vlans.length === 0;
  if (scene.filter.vlan != null && !vlans.includes(scene.filter.vlan)) setVlan(null);
  readonlyChip.hidden = !store.readonly;
  for (const el of [zoneButton, layoutButton]) el.disabled = store.readonly;
  modes.el.classList.toggle('disabled', store.readonly);
  showEmpty();
  showStatus();
}

function showEmpty() {
  const blank = store.map.nodes.length === 0 && store.map.zones.length === 0;
  const count = store.hosts.length;
  fill(emptyAdd, icon(uiIcons.add, 14), h('span', {}, count === 1 ? 'Aggiungi l\'host trovato' : `Aggiungi i ${count} host trovati`));
  emptyAdd.hidden = count === 0 || store.readonly;
  emptyScan.hidden = count > 0;
  empty.hidden = !blank;
}

function showStatus() {
  const map = store.map;
  const plural = (n, one, many) => `${n} ${n === 1 ? one : many}`;
  const parts = [plural(map.nodes.length, 'apparato', 'apparati'), plural(map.links.length, 'cavo', 'cavi')];
  if (map.zones.length) parts.push(plural(map.zones.length, 'zona', 'zone'));
  const down = map.nodes.filter((node) => store.stateOf(node) === 'down').length;
  const sel = store.sel.size;
  fill(statusLine, 
    h('span', {}, parts.join(' · ')),
    down ? h('span', { class: 'dm-status-down' }, h('span', { class: 'dm-dot st-down' }), plural(down, 'spento', 'spenti')) : null,
    sel > 1 ? h('span', {}, `${sel} selezionati`) : null,
    mode === 'connect' ? h('span', { class: 'dm-status-hint' }, 'Trascina da un apparato all\'altro per collegarli. Esc per tornare a Sposta.') : null);
}

// ------------------------------------------------------------------ modi, libreria, zoom

function setMode(value) {
  mode = value === 'connect' && !store.readonly ? 'connect' : 'move';
  modes.set(mode);
  app.classList.toggle('connecting', mode === 'connect');
  showStatus();
}

function setLibrary(shown) {
  app.classList.toggle('no-library', !shown);
  libraryToggle.classList.toggle('on', shown);
  try {
    localStorage.setItem('dm-library', shown ? '1' : '0');
  } catch { /* senza memoria del browser: si ricomincia con la libreria aperta */ }
}

function zoomBy(factor) {
  const { left, top, w, h: height } = scene.size;
  scene.zoomAt(left + w / 2, top + height / 2, factor);
  viewChanged();
}

function zoomTo(k) {
  const middle = scene.middle();
  const { w, h: height } = scene.size;
  scene.animateTo(w / 2 - middle.x * k, height / 2 - middle.y * k, k);
  viewChanged();
}

function fitAll(animate = true) {
  const box = boundsOf(store.map);
  if (box) scene.frameRect(box, { animate });
  else {
    const { w, h: height } = scene.size;
    scene.setView(w / 2, height / 3, 1);
  }
  viewChanged();
}

/**
 * Mostra la mappa com'era: la vista salvata, oppure tutto inquadrato. Una pagina nascosta non
 * ha ancora misure: l'inquadratura aspetta che la tela ne abbia.
 */
let needsFit = false;
function showMap() {
  stop(scene, 'view');
  scene.forget();
  scene.render();
  const view = store.map.view;
  needsFit = false;
  if (view) scene.setView(view.x, view.y, view.k);
  else if (scene.size.w < 10 || scene.size.h < 10) needsFit = true;
  else fitAll(false);
}

new ResizeObserver(() => {
  if (needsFit && scene.size.w >= 10 && scene.size.h >= 10) {
    needsFit = false;
    fitAll(false);
  }
}).observe(canvas);

// ------------------------------------------------------------------ aggiungere

/** Dove posare qualcosa: il punto lasciato, oppure il centro della vista (a scalare se occupato). */
function spot(clientX, clientY) {
  if (clientX != null) {
    const at = scene.toWorld(clientX, clientY);
    return { x: at.x - NODE_W / 2, y: at.y - NODE_H / 2 };
  }
  const middle = scene.middle();
  let x = snap(middle.x - NODE_W / 2);
  let y = snap(middle.y - NODE_H / 2);
  while (store.map.nodes.some((node) => Math.abs(node.x - x) < 12 && Math.abs(node.y - y) < 12)) {
    x += 24;
    y += 24;
  }
  return { x, y };
}

function added(node) {
  if (!node) return;
  scene.pulse.add(node.id);
  store.select([node.id]);
  focusSink();
}

function addKind(kind, clientX, clientY) {
  const at = spot(clientX, clientY);
  added(store.addNode(kind, at.x, at.y));
}

function addHost(host, clientX, clientY) {
  const at = spot(clientX, clientY);
  added(store.nodeFromHost(host, at.x, at.y));
}

function addHosts(list) {
  const ids = store.addHosts(list, scene.middle());
  if (ids.length === 0) return;
  for (const id of ids) scene.pulse.add(id);
  scene.render();
  const box = boundsOf(store.map);
  if (box) scene.frameRect(box);
}

function addZone() {
  if (store.readonly) return;
  const nodes = store.selected('node').map((node) => node.id);
  let zone;
  if (nodes.length) zone = store.zoneAround(nodes);
  else {
    const middle = scene.middle();
    zone = store.addZone({ x: middle.x - 210, y: middle.y - 130, w: 420, h: 260 });
  }
  if (zone) {
    store.select([zone.id]);
    inspector.focus('name');
  }
}

function connectSelected() {
  const nodes = store.selected('node');
  if (nodes.length !== 2) return;
  const link = store.addLink(nodes[0].id, nodes[1].id);
  if (link) store.select([link.id]);
}

function reveal(id) {
  const item = store.find(id);
  if (!item) return;
  store.select([id]);
  scene.centerOn(store.typeOf(id) === 'node' ? centerOf(item) : { x: item.x + item.w / 2, y: item.y + item.h / 2 });
  viewChanged();
}

// ------------------------------------------------------------------ modificare

function removeSelection() {
  if (store.readonly || store.sel.size === 0) return;
  store.remove();
  focusSink();
}

function duplicate() {
  const fragment = store.fragment();
  if (!fragment || store.readonly) return;
  store.paste(fragment, 24, 24);
}

function undo() {
  if (store.undo()) focusSink();
}

function redo() {
  if (store.redo()) focusSink();
}

function align(how) {
  const nodes = store.selected('node');
  if (nodes.length < 2 || !store.begin()) return;
  const xs = nodes.map((n) => n.x);
  const ys = nodes.map((n) => n.y);
  const left = Math.min(...xs);
  const right = Math.max(...xs);
  const top = Math.min(...ys);
  const bottom = Math.max(...ys);
  const positions = new Map();
  for (const node of nodes) {
    const p = { x: node.x, y: node.y };
    if (how === 'left') p.x = left;
    if (how === 'right') p.x = right;
    if (how === 'center') p.x = snap((left + right) / 2);
    if (how === 'top') p.y = top;
    if (how === 'bottom') p.y = bottom;
    if (how === 'middle') p.y = snap((top + bottom) / 2);
    positions.set(node.id, p);
  }
  store.moveTo(positions);
  store.cut();
}

function spread(axis) {
  const nodes = store.selected('node').sort((a, b) => a[axis] - b[axis]);
  if (nodes.length < 3 || !store.begin()) return;
  const first = nodes[0][axis];
  const step = (nodes[nodes.length - 1][axis] - first) / (nodes.length - 1);
  const positions = new Map();
  nodes.forEach((node, index) => positions.set(node.id, { ...node, [axis]: snap(first + step * index) }));
  store.moveTo(positions);
  store.cut();
}

/** Disponi: tutta la mappa, o solo gli apparati selezionati (restano dov'erano, in ordine). */
function arrangeNow(selectionOnly = false) {
  if (store.readonly) return;
  const map = store.map;
  const chosen = store.selected('node').map((node) => node.id);
  const only = selectionOnly || chosen.length >= 2 ? new Set(chosen) : null;
  const positions = arrange(map, only);
  if (!positions || !store.begin()) return;

  // le zone seguono i loro apparati: la zona si rifa' attorno a dove sono andati
  const zones = map.zones.map((zone) => ({ zone, members: map.nodes.filter((n) => positions.has(n.id) && centerOf(n).x >= zone.x && centerOf(n).x <= zone.x + zone.w && centerOf(n).y >= zone.y && centerOf(n).y <= zone.y + zone.h).map((n) => n.id) }));

  let dx = 0;
  let dy = 0;
  const before = boundsOf(map, only ?? new Set(map.nodes.map((n) => n.id)));
  const xs = [...positions.values()].map((p) => p.x);
  const ys = [...positions.values()].map((p) => p.y);
  if (before) {
    dx = snap(before.x - Math.min(...xs));
    dy = snap(before.y - Math.min(...ys));
  }
  const moved = new Map();
  for (const [id, p] of positions) moved.set(id, { x: snap(p.x + dx), y: snap(p.y + dy) });
  store.moveTo(moved);
  for (const { zone, members } of zones) {
    if (members.length === 0) continue;
    const box = boundsOf(map, new Set(members));
    Object.assign(zone, { x: box.x - 32, y: box.y - 56, w: box.w + 64, h: box.h + 88 });
  }
  map.zones.sort((p, q) => q.w * q.h - p.w * p.h);
  store.emit('map');
  store.cut();
  const box = boundsOf(map, only);
  if (box) scene.frameRect(box);
}

// ------------------------------------------------------------------ appunti

function copy(cut = false) {
  const fragment = store.fragment();
  if (!fragment) return null;
  clip = fragment;
  pasteCount = 0;
  if (cut) store.remove();
  return JSON.stringify(fragment);
}

/** Incolla: un pezzo di mappa copiato, oppure un elenco di indirizzi (uno per apparato). */
function pasteText(textValue) {
  if (store.readonly) return;
  let fragment = null;
  try {
    const parsed = JSON.parse(textValue);
    if (parsed?.ducknoteMap === 1) fragment = parsed;
  } catch { /* testo semplice */ }
  if (!fragment && clip && !textValue) fragment = clip;
  if (fragment) {
    pasteFragment(fragment);
    return;
  }
  const hosts = [...new Set((textValue.match(/\b(?:\d{1,3}\.){3}\d{1,3}\b|\b(?:[a-z0-9-]+\.)+[a-z]{2,}\b/gi) ?? []).map((v) => v.toLowerCase()))].slice(0, 200);
  if (hosts.length === 0) return;
  addHosts(hosts.map((host) => store.hosts.find((h2) => [h2.key, h2.ip, h2.name].some((v) => String(v ?? '').toLowerCase() === host)) ?? { key: host, ip: /^\d/.test(host) ? host : '', name: '' }));
}

function pasteFragment(fragment) {
  const box = boundsOf({ nodes: fragment.nodes ?? [], zones: fragment.zones ?? [] });
  if (!box) return;
  pasteCount++;
  const view = scene.middle();
  const visible = scene.toScreen(box.x, box.y);
  const { left, top, w, h: height } = scene.size;
  const onScreen = visible.x > left && visible.y > top && visible.x < left + w - 40 && visible.y < top + height - 40;
  const dx = onScreen ? 24 * pasteCount : snap(view.x - box.x - box.w / 2);
  const dy = onScreen ? 24 * pasteCount : snap(view.y - box.y - box.h / 2);
  store.paste(fragment, dx, dy);
}

document.addEventListener('copy', (event) => {
  if (typingIn(event.target)) return;
  const textValue = copy();
  if (textValue == null) return;
  event.preventDefault();
  event.clipboardData?.setData('text/plain', textValue);
});

document.addEventListener('cut', (event) => {
  if (typingIn(event.target) || store.readonly) return;
  const textValue = copy(true);
  if (textValue == null) return;
  event.preventDefault();
  event.clipboardData?.setData('text/plain', textValue);
});

document.addEventListener('paste', (event) => {
  if (typingIn(event.target)) return;
  event.preventDefault();
  pasteText(event.clipboardData?.getData('text/plain') ?? '');
  focusSink();
});

// ------------------------------------------------------------------ tastiera

let space = false;
let lastEdit = { action: '', at: 0 };

document.addEventListener('keydown', (event) => {
  activity();
  if (windowKey(event)) return;
  if (popups.isOpen) return;
  const mod = modKey(event);

  if (mod && !event.shiftKey && (event.key === 'f' || event.key === 'F')) {
    event.preventDefault();
    search.focus();
    search.select();
    return;
  }
  if (typingIn(event.target)) return;

  const key = event.key;
  const handled = (action) => {
    event.preventDefault();
    lastEdit = { action, at: Date.now() };
  };
  if (mod && (key === 'z' || key === 'Z')) {
    handled(event.shiftKey ? 'redo' : 'undo');
    if (event.shiftKey) redo();
    else undo();
  } else if (mod && (key === 'y' || key === 'Y') && !isMac()) {
    handled('redo');
    redo();
  } else if (mod && (key === 'a' || key === 'A')) {
    handled('selectAll');
    store.selectAll();
  } else if (mod && (key === 'd' || key === 'D')) {
    event.preventDefault();
    duplicate();
  } else if (mod && (key === '0' || key === ')')) {
    event.preventDefault();
    fitAll();
  } else if (mod && (key === '+' || key === '=')) {
    event.preventDefault();
    zoomBy(1.25);
  } else if (mod && (key === '-' || key === '_')) {
    event.preventDefault();
    zoomBy(1 / 1.25);
  } else if (mod) {
    // Cmd/Ctrl+C, X, V: li fa il sistema (sul Mac passando dal menu Modifica), qui arrivano
    // come eventi degli appunti
  } else if (key === 'Delete' || key === 'Backspace') {
    event.preventDefault();
    removeSelection();
  } else if (key === 'Escape') {
    if (mode === 'connect') setMode('move');
    else if (scene.filter.text || scene.filter.vlan != null) clearFilters();
    else store.deselect();
  } else if (key.startsWith('Arrow')) {
    event.preventDefault();
    const step = event.shiftKey ? GRID * 2 : 1;
    store.nudge(key === 'ArrowLeft' ? -step : key === 'ArrowRight' ? step : 0, key === 'ArrowUp' ? -step : key === 'ArrowDown' ? step : 0);
  } else if (key === 'F2' || key === 'Enter') {
    if (store.sel.size === 1) {
      event.preventDefault();
      inspector.focus(store.typeOf([...store.sel][0]) === 'link' ? 'pa' : 'name');
    }
  } else if (key === ' ' && !event.repeat) {
    event.preventDefault();
    space = true;
    pointer.space = true;
    canvas.classList.add('grab');
  } else if (!event.altKey && !event.repeat) {
    // la lettera non deve finire nel campo che il comando apre (Z apre il nome della zona)
    const lower = key.toLowerCase();
    const run = { v: () => setMode('move'), c: () => setMode(mode === 'connect' ? 'move' : 'connect'), z: () => addZone(), '+': () => zoomBy(1.25), '=': () => zoomBy(1.25), '-': () => zoomBy(1 / 1.25) }[lower];
    if (run) {
      event.preventDefault();
      run();
    }
  }
});

document.addEventListener('keyup', (event) => {
  if (event.key === ' ' && space) {
    space = false;
    pointer.space = false;
    canvas.classList.remove('grab');
  }
});

window.addEventListener('pointerdown', activity, true);
sink.addEventListener('input', () => {
  // quello che si scrive nel campo nascosto non resta: e' solo il fuoco della mappa
  sink.value = ' ';
  sink.select();
});

// ------------------------------------------------------------------ cercare e filtrare

function clearFilters() {
  search.value = '';
  scene.filter.text = '';
  setVlan(null);
  scene.invalidate();
}

let hits = [];
let hitIndex = -1;
search.addEventListener('input', () => {
  scene.filter.text = search.value;
  hits = [];
  hitIndex = -1;
  scene.invalidate();
});
search.addEventListener('keydown', (event) => {
  if (event.key === 'Enter') {
    event.preventDefault();
    const lit = scene.litSet(store.map);
    hits = lit ? store.map.nodes.filter((node) => lit.has(node.id)) : [];
    if (hits.length === 0) return;
    hitIndex = (hitIndex + (event.shiftKey ? -1 : 1) + hits.length) % hits.length;
    reveal(hits[hitIndex].id);
  } else if (event.key === 'Escape') {
    event.preventDefault();
    clearFilters();
    focusSink();
  }
});

function vlanList() {
  const ids = new Set();
  for (const link of store.map.links) for (const id of parseVlans(link.vlan).ids) ids.add(id);
  for (const zone of store.map.zones) for (const id of parseVlans(zone.vlan).ids) ids.add(id);
  return [...ids].sort((a, b) => a - b).slice(0, 200);
}

function setVlan(vlan) {
  scene.filter.vlan = vlan;
  vlanButton.classList.toggle('on', vlan != null);
  vlanButton.querySelector('span').textContent = vlan == null ? 'VLAN' : `VLAN ${vlan}`;
  scene.invalidate();
}

function vlanMenu() {
  const zones = new Map();
  for (const zone of store.map.zones) for (const id of parseVlans(zone.vlan).ids) zones.set(id, zone.name);
  popups.menu(vlanButton, [
    { header: 'Mostra solo una VLAN' },
    { label: 'Tutte', checked: scene.filter.vlan == null, action: () => setVlan(null) },
    'sep',
    ...vlanList().map((vlan) => ({ label: `VLAN ${vlan}`, hint: zones.get(vlan) ?? '', checked: scene.filter.vlan === vlan, action: () => setVlan(vlan) })),
  ], { width: 220 });
}

// ------------------------------------------------------------------ menu

function mapMenu() {
  const map = store.map;
  popups.menu(mapButton, [
    { header: 'Le mappe della nota' },
    ...store.doc.maps.map((item) => ({ label: item.name, hint: String(item.nodes.length), checked: item.id === map.id, action: () => switchMap(item.id) })),
    'sep',
    { label: 'Nuova mappa', icon: uiIcons.add, disabled: store.readonly, action: () => newMap() },
    { label: 'Rinomina...', icon: uiIcons.pen, disabled: store.readonly, action: () => renameMap() },
    { label: 'Duplica', icon: uiIcons.duplicate, disabled: store.readonly, action: () => duplicateMap() },
    'sep',
    { label: 'Elimina la mappa', icon: uiIcons.trash, danger: true, disabled: store.readonly || (store.doc.maps.length === 1 && map.nodes.length === 0 && map.zones.length === 0), action: () => deleteMap() },
  ], { width: 240 });
}

function switchMap(id) {
  rememberView();
  clearFilters();
  store.switchTo(id);
  showMap();
}

async function newMap() {
  const name = await popups.prompt(mapButton, { title: 'Nuova mappa', value: store.freeName('Mappa'), ok: 'Crea' });
  if (!name) return;
  rememberView();
  store.addMap(name);
  showMap();
}

async function renameMap() {
  const name = await popups.prompt(mapButton, { title: 'Nome della mappa', value: store.map.name, ok: 'Rinomina' });
  if (name) store.renameMap(name);
}

function duplicateMap() {
  rememberView();
  store.duplicateMap();
  showMap();
}

async function deleteMap() {
  const map = store.map;
  const count = map.nodes.length;
  const yes = await popups.confirm(mapButton, {
    title: `Eliminare «${map.name}»?`,
    text: count ? `La mappa e i suoi ${count === 1 ? 'un apparato' : `${count} apparati`} spariscono dalla nota. Non si puo' annullare.` : 'La mappa sparisce dalla nota.',
    ok: 'Elimina',
  });
  if (!yes) return;
  store.removeMap();
  showMap();
}

function exportMenu() {
  const map = store.map;
  const blank = map.nodes.length === 0 && map.zones.length === 0;
  popups.menu(exportButton, [
    { header: 'Esporta la mappa' },
    { label: 'Immagine PNG', disabled: blank, action: () => exportPng() },
    { label: 'Disegno SVG', disabled: blank, action: () => exportSvg() },
    'sep',
    { label: 'Inventario apparati (CSV)', disabled: map.nodes.length === 0, action: () => send({ t: 'export', format: 'csv', name: fileName(map, 'apparati', 'csv'), ...devicesTable(map, store) }) },
    { label: 'Elenco cavi (CSV)', disabled: map.links.length === 0, action: () => send({ t: 'export', format: 'csv', name: fileName(map, 'cavi', 'csv'), ...linksTable(map) }) },
  ], { width: 240 });
}

async function exportPng() {
  try {
    send({ t: 'export', format: 'png', name: fileName(store.map, '', 'png'), data: await mapPng(store.map, store) });
  } catch (problem) {
    send({ t: 'error', text: `L'immagine non si e' potuta creare: ${problem?.message ?? problem}` });
  }
}

function exportSvg() {
  send({ t: 'export', format: 'svg', name: fileName(store.map, '', 'svg'), text: mapSvg(store.map, store).svg });
}

function contextMenu(x, y, hit, world) {
  const ro = store.readonly;
  let items;
  if (hit.type === 'node') {
    const node = store.find(hit.id);
    items = [
      { label: 'Rinomina', icon: uiIcons.pen, disabled: ro, hint: 'F2', action: () => inspector.focus('name') },
      { label: 'Duplica', icon: uiIcons.duplicate, disabled: ro, hint: shortcut('D'), action: () => duplicate() },
      node?.host ? 'sep' : null,
      node?.host ? { label: 'Controlla adesso', icon: uiIcons.pulse, action: () => send({ t: 'check', host: node.host }) } : null,
      node?.host ? { label: 'Apri nella pagina Rete', icon: uiIcons.open, action: () => send({ t: 'reveal', host: node.host }) } : null,
      'sep',
      store.selected('node').length >= 1 ? { label: 'Raggruppa in una zona', icon: uiIcons.zone, disabled: ro, action: () => addZone() } : null,
      { label: 'Elimina', icon: uiIcons.trash, danger: true, disabled: ro, action: () => removeSelection() },
    ];
  } else if (hit.type === 'link') {
    items = [
      { label: 'Scambia i capi', icon: uiIcons.swap, disabled: ro, action: () => store.swapEnds(hit.id) },
      'sep',
      { label: 'Elimina il cavo', icon: uiIcons.trash, danger: true, disabled: ro, action: () => removeSelection() },
    ];
  } else if (hit.type === 'zone') {
    const zone = store.find(hit.id);
    items = [
      { label: 'Rinomina', icon: uiIcons.pen, disabled: ro, action: () => inspector.focus('name') },
      { label: 'Seleziona gli apparati', icon: uiIcons.pointer, action: () => store.select(store.map.nodes.filter((n) => centerOf(n).x >= zone.x && centerOf(n).x <= zone.x + zone.w && centerOf(n).y >= zone.y && centerOf(n).y <= zone.y + zone.h).map((n) => n.id)) },
      'sep',
      { label: 'Elimina la zona', icon: uiIcons.trash, danger: true, disabled: ro, action: () => removeSelection() },
    ];
  } else {
    items = [
      { label: 'Aggiungi un apparato...', icon: uiIcons.add, disabled: ro, action: () => kindPicker(popups, x, y, (kind) => {
        const node = store.addNode(kind, world.x - NODE_W / 2, world.y - NODE_H / 2);
        added(node);
      }) },
      { label: 'Incolla', icon: uiIcons.copy, disabled: ro || !clip, hint: shortcut('V'), action: () => pasteFragment(clip) },
      'sep',
      { label: 'Seleziona tutto', hint: shortcut('A'), action: () => store.selectAll() },
      { label: 'Adatta alla finestra', icon: uiIcons.fit, hint: shortcut('0'), action: () => fitAll() },
      { label: 'Disponi', icon: uiIcons.layout, disabled: ro, action: () => arrangeNow() },
    ];
  }
  popups.menu({ x, y }, items, { width: 230 });
}

// ------------------------------------------------------------------ cosa chiede la finestra

window.dm = {
  /** Le mappe della nota (il JSON di mappa.json, o null per una nota senza mappe). */
  load(doc, options = {}) {
    const raw = typeof doc === 'string' ? JSON.parse(doc || 'null') : doc;
    clearTimeout(saveTimer);
    saveTimer = 0;
    popups.close();
    pointer.cancel();
    store.readonly = !!options.readonly;
    if (options.hosts) store.setHosts(options.hosts, options.groups);
    store.load(raw);
    clearFilters();
    setMode('move');
    showChrome();
    showMap();
    library.renderHosts();
    if (options.focus) focusSink();
    return true;
  },

  /** Le mappe adesso, subito, con la vista di quella aperta (per salvare prima di chiudere). */
  doc() {
    clearTimeout(saveTimer);
    saveTimer = 0;
    rememberView();
    return store.json();
  },

  /**
   * Gli host che la finestra conosce, col loro stato: [{ key, ip, name, mac, vendor, type, os,
   * state, rtt, groups }], e i gruppi di scansione della nota: [{ id, name, monitor }].
   */
  hosts(list, groups) {
    store.setHosts(list, groups);
    showEmpty();
    showStatus();
  },

  readonly(on) {
    store.readonly = !!on;
    if (on) setMode('move');
    showChrome();
    inspector.refresh('doc');
  },

  /** Via tutto: la nota si chiude a chiave. */
  clear() {
    clearTimeout(saveTimer);
    saveTimer = 0;
    popups.close();
    pointer.cancel();
    clip = null;
    store.clear();
    clearFilters();
    scene.forget();
    showChrome();
    library.renderHosts();
    return true;
  },

  theme(vars, dark) {
    applyTheme(vars, dark);
    scene.invalidate();
  },

  /** Le voci del menu Modifica del Mac (Annulla, Ripeti, Seleziona tutto). */
  edit(action) {
    // se il tasto e' gia' arrivato alla pagina, il menu non lo rifa'
    const now = Date.now();
    if (lastEdit.action === action && now - lastEdit.at < 250) return true;
    lastEdit = { action, at: now };
    const typing = typingIn(document.activeElement);
    switch (action) {
      case 'undo':
      case 'redo':
        if (typing) document.execCommand(action);
        else if (action === 'undo') undo();
        else redo();
        return true;
      case 'selectAll':
        if (typing) document.activeElement.select?.();
        else store.selectAll();
        return true;
      case 'copy':
      case 'cut':
        if (!typing) copy(action === 'cut');
        return !typing;
      case 'delete':
        removeSelection();
        return true;
      default:
        return false;
    }
  },

  /** Incolla un testo (quando il sistema non manda l'evento degli appunti). */
  paste(textValue) {
    if (typingIn(document.activeElement)) return false;
    pasteText(textValue ?? '');
    return true;
  },

  focus() {
    focusSink();
  },

  /** Un apparato (per id o per indirizzo) al centro, selezionato. */
  /** Un apparato (per id o per indirizzo) al centro e scelto; se sta in un'altra mappa, si passa a quella. */
  reveal(what) {
    const key = String(what ?? '').toLowerCase();
    const match = (n) => n.id === what || n.host.toLowerCase() === key;
    const map = store.map.nodes.some(match) ? store.map : store.doc.maps.find((m) => m.nodes.some(match));
    if (!map) return false;
    if (map !== store.map) switchMap(map.id);
    reveal(store.map.nodes.find(match).id);
    return true;
  },

  // per le prove: i messaggi che sarebbero andati alla finestra, e lo stato
  outbox() { return takeOutbox(); },
  get store() { return store; },
  get scene() { return scene; },
  normalize: normalizeDoc,
};

// la libreria aperta o chiusa come la si era lasciata (finche' il WebView se lo ricorda)
try {
  setLibrary(localStorage.getItem('dm-library') !== '0');
} catch {
  setLibrary(true);
}
setMode('move');
showChrome();
showMap();
library.renderHosts();
send({ t: 'ready' });
