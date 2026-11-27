// Il calendario della nota dentro al WebView di DuckNote (WKWebView sul Mac, WebView2 su
// Windows). Tutto quello che la finestra chiede passa da window.dc; quello che la pagina dice
// alla finestra passa da send(): i cambiamenti come op (la finestra tiene il calendario vero e
// lo rimanda con dc.data), un host da vedere in Rete, un apparato da vedere sulla mappa.

import { h, icon, fill, typingIn, modKey } from '../common/dom.js';
import { ui as uiIcons } from '../common/icons.js';
import { Popups } from '../common/popups.js';
import { show as appear, hide as vanish } from '../common/motion.js';
import { send, takeOutbox, activity, windowKey, applyTheme } from '../common/bridge.js';
import {
  normalizeDoc, applyOp, writeItem, normalizeItem, localToday, parseDay, addDays, addMonths, partsOf,
  makeDay, daysIn, MONTHS, newId, allItems, occurrences, shortDay, dates, nextDate,
} from './model.js';
import { Month } from './month.js';
import { editor, linkPicker, DEFAULT_REMIND } from './editor.js';
import { dayView, searchView, certView, certSettings, detailView } from './panel.js';

// ------------------------------------------------------------------ lo stato

const firstToday = localToday();
const state = {
  doc: normalizeDoc(null),
  today: firstToday,
  fixedToday: false,
  month: firstToday.slice(0, 7),
  selected: firstToday,
  readonly: false,
  hosts: [],
  devices: [],
  query: '',
  /** day, search, detail, edit, cert */
  view: 'day',
  editing: null,
  cert: '',
  /** La volta mostrata nella scheda ("id@giorno"). */
  detail: '',
  /** Lo stato dell'host legato (up, down, wait, unknown), se DuckNote lo conosce. */
  hostState(link) {
    const key = String(link?.host ?? '').toLowerCase();
    if (!key) return null;
    const host = state.hosts.find((one) => [one.key, one.ip, one.name].some((value) => String(value ?? '').toLowerCase() === key));
    if (!host) return null;
    return ['up', 'down', 'wait'].includes(host.state) ? host.state : 'unknown';
  },
};

// ------------------------------------------------------------------ la pagina

const app = h('div', { class: 'dm-app dc-app' });
const popupLayer = h('div', { class: 'dm-popups' });
const toastLayer = h('div', { class: 'dc-toasts', 'aria-live': 'polite' });
document.body.append(app, popupLayer, toastLayer);
const popups = new Popups(popupLayer);

const tool = (path, title, onclick) => h('button', { class: 'dm-tool dm-press', type: 'button', title, 'aria-label': title, onclick }, icon(path, 16));
const prevButton = tool(uiIcons.left, 'Mese prima (Pag su)', () => goMonth(-1));
const nextButton = tool(uiIcons.right, 'Mese dopo (Pag giu)', () => goMonth(1));
const todayButton = h('button', { class: 'dm-tool wide dm-press', type: 'button', title: 'Torna a oggi (T)', onclick: () => goToday() }, h('span', {}, 'Oggi'));
const title = h('h1', { class: 'dc-title' });
const search = h('input', { class: 'dm-field dm-search', type: 'search', placeholder: 'Cerca', spellcheck: 'false', 'aria-label': 'Cerca nel calendario' });
const searchBox = h('label', { class: 'dm-search-box' }, icon(uiIcons.search, 13), search);
const certButton = h('button', { class: 'dm-tool wide dm-press', type: 'button', title: 'Le scadenze dei certificati viste dalle analisi', onclick: () => toggleCertSettings() },
  icon(uiIcons.lock, 15), h('span', {}, 'Certificati'));
certButton.dataset.menuAlign = 'right';
const newButton = h('button', { class: 'dm-button primary dm-press dc-new', type: 'button', title: 'Una voce nuova nel giorno scelto (N)', onclick: () => create(state.selected) },
  icon(uiIcons.add, 13), h('span', {}, 'Nuovo'));
const readonlyChip = h('span', { class: 'dm-chip tone-Orange', hidden: true }, 'Sola lettura');

const toolbar = h('header', { class: 'dm-toolbar dc-toolbar' },
  prevButton, nextButton, todayButton, title, readonlyChip, h('span', { class: 'dm-spacer' }), searchBox, certButton, newButton);

const panel = h('aside', { class: 'dc-panel', 'aria-label': 'Giorno' });

const actions = {
  pick: (day) => pick(day),
  open: (occurrence) => open(occurrence),
  create: (day) => create(day),
  move: (item, day) => {
    const moved = { ...writeItem(item), date: day, done: item.done.length ? [day] : [] };
    if (change({ op: 'put', item: moved })) {
      toast(`«${item.title || 'Senza titolo'}» spostato al ${shortDay(day, partsOf(state.today)[0])}`, () => change({ op: 'put', item: writeItem(item) }));
    }
  },
  done: (occurrence, on) => change({ op: 'done', id: occurrence.item.id, date: occurrence.date, on }),
  edit: (occurrence) => editOccurrence(occurrence),
  reveal: (link) => reveal(link),
  ignore: (host) => {
    if (change({ op: 'ignore', host, on: true })) {
      back();
      toast(`I certificati di ${host} non compaiono piu'`, () => change({ op: 'ignore', host, on: false }));
    }
  },
  unignore: (host) => {
    change({ op: 'ignore', host, on: false });
    refreshCertSettings();
  },
  certs: (patch) => {
    change({ op: 'certs', ...patch });
    refreshCertSettings();
  },
  remind: (link, seen) => {
    const before = addDays(seen.date, -7);
    compose({ kind: 'todo', title: `Rinnovare il certificato di ${seen.host}`, date: before >= state.today ? before : state.today, link });
  },
  back: () => back(),
};

const month = new Month(state, actions);
app.append(toolbar, h('div', { class: 'dm-main dc-body' }, month.el, panel));

// ------------------------------------------------------------------ disegno

function render() {
  const [y, m] = partsOf(`${state.month}-01`);
  const name = `${MONTHS[m - 1]} ${y}`;
  title.textContent = name.charAt(0).toUpperCase() + name.slice(1);
  readonlyChip.hidden = !state.readonly;
  newButton.disabled = state.readonly;
  month.render();
  renderPanel();
}

function renderPanel() {
  if (state.view === 'edit' && state.editing) return;
  if (state.view === 'detail') {
    const occurrence = findOccurrence(state.detail);
    if (occurrence && !occurrence.item.cert) {
      fill(panel, detailView(state, occurrence, actions));
      return;
    }
    state.view = 'day';
  }
  if (state.view === 'cert') {
    const occurrence = findOccurrence(state.cert);
    if (occurrence?.item.cert) {
      fill(panel, certView(state, occurrence, actions));
      return;
    }
    state.view = 'day';
  }
  if (state.view === 'search' && state.query.trim()) {
    fill(panel, searchView(state, actions));
    return;
  }
  state.view = 'day';
  fill(panel, dayView(state, actions));
}

/** La volta "id@giorno" com'e' adesso nel calendario. */
function findOccurrence(key) {
  const at = key.lastIndexOf('@');
  const id = key.slice(0, at);
  const date = key.slice(at + 1);
  const item = allItems(state.doc).find((one) => one.id === id);
  if (!item || !parseDay(date)) return null;
  return occurrences(state.doc, date, date, [item])[0] ?? null;
}

// ------------------------------------------------------------------ dove si guarda

function pick(day) {
  if (!parseDay(day)) return;
  state.selected = day;
  if (state.view === 'search' || state.view === 'cert' || state.view === 'detail') state.view = 'day';
  if (day.slice(0, 7) === state.month && month.select(day)) {
    renderPanel();
    return;
  }
  state.month = day.slice(0, 7);
  render();
}

function goMonth(delta) {
  state.month = addMonths(`${state.month}-01`, delta).slice(0, 7);
  // il giorno scelto segue il mese, se e' fuori: lo stesso numero, o l'ultimo del mese
  if (state.selected.slice(0, 7) !== state.month) {
    const [y, m] = partsOf(`${state.month}-01`);
    state.selected = makeDay(y, m, Math.min(partsOf(state.selected)[2], daysIn(y, m)));
  }
  render();
}

function goToday() {
  state.month = state.today.slice(0, 7);
  pick(state.today);
}

function back() {
  state.view = state.query.trim() ? 'search' : 'day';
  state.editing = null;
  state.cert = '';
  state.detail = '';
  render();
}

// ------------------------------------------------------------------ voci

function open(occurrence) {
  state.selected = occurrence.date;
  state.month = occurrence.date.slice(0, 7);
  if (occurrence.item.cert) {
    state.view = 'cert';
    state.cert = occurrence.key;
    state.editing = null;
    render();
    return;
  }
  showDetail(occurrence.item.id, occurrence.date);
}

/** La scheda della volta "id@giorno"; se non c'e' piu', il giorno. */
function showDetail(id, date) {
  const occurrence = findOccurrence(`${id}@${date}`);
  if (!occurrence || occurrence.item.cert) {
    back();
    return;
  }
  popups.close();
  state.view = 'detail';
  state.detail = occurrence.key;
  state.editing = null;
  state.cert = '';
  state.selected = occurrence.date;
  state.month = occurrence.date.slice(0, 7);
  render();
  panel.scrollTop = 0;
  appear(panel.firstElementChild, { dy: 6, ms: 200 });
}

/** La matita: la voce si apre per essere modificata. */
function editOccurrence(occurrence) {
  if (state.readonly) return;
  const item = state.doc.items.find((one) => one.id === occurrence.item.id);
  if (item) edit(structuredClone(item), false, occurrence.date);
}

/** Dopo una modifica: la volta che si guardava, se c'e' ancora, altrimenti la prossima. */
function dateAfterEdit(item, date) {
  if (!item.repeat) return item.date;
  return dates(item, date, date).length ? date : nextDate(item, state.today);
}

function blank(day, seed = {}) {
  const kind = ['event', 'todo', 'deadline', 'note'].includes(seed.kind) ? seed.kind : 'event';
  return {
    id: newId(),
    kind,
    title: seed.title ?? '',
    date: parseDay(seed.date) ?? day,
    time: kind === 'event' ? '09:00' : '',
    notes: seed.notes ?? '',
    remind: [...DEFAULT_REMIND[kind]],
    repeat: '',
    every: 1,
    until: '',
    done: [],
    skip: [],
    link: seed.link ?? null,
  };
}

function create(day, seed) {
  if (state.readonly) return;
  const draft = blank(parseDay(day) ?? state.selected, seed);
  state.selected = draft.date;
  state.month = draft.date.slice(0, 7);
  edit(draft, true, draft.date);
}

function compose(seed = {}) {
  create(parseDay(seed.date) ?? state.selected, seed);
}

function edit(draft, isNew, date) {
  popups.close();
  const form = editor(draft, {
    isNew,
    readonly: state.readonly,
    pickLink: (anchor, choose) => linkPicker(popups, anchor, state, choose),
    reveal: (link) => reveal(link),
    // una voce gia' scritta torna alla sua scheda; una nuova al giorno
    cancel: () => (isNew ? back() : showDetail(draft.id, date)),
    save: (saved) => {
      const item = normalizeItem(writeItem(saved));
      if (!item) return;
      if (change({ op: 'put', item: writeItem(item) }) || !isNew) {
        if (isNew) {
          state.selected = item.date;
          state.month = item.date.slice(0, 7);
          back();
        } else {
          showDetail(item.id, dateAfterEdit(item, date));
        }
      }
    },
    remove: (anchor) => remove(draft.id, date, anchor),
  });
  state.view = 'edit';
  state.editing = { id: draft.id, isNew };
  state.cert = '';
  render();
  fill(panel, form.el);
  panel.scrollTop = 0;
  appear(form.el, { dy: 6, ms: 220 });
  form.focus();
}

function remove(id, date, anchor) {
  const item = state.doc.items.find((one) => one.id === id);
  if (!item) {
    back();
    return;
  }
  const old = structuredClone(item);
  const name = item.title || 'Senza titolo';
  const all = () => {
    if (change({ op: 'remove', id })) {
      back();
      toast(`Eliminato «${name}»`, () => change({ op: 'put', item: writeItem(old) }));
    }
  };
  if (!item.repeat) {
    all();
    return;
  }
  popups.menu(anchor, [
    { label: `Solo la volta del ${shortDay(date, partsOf(state.today)[0])}`, action: () => {
      if (change({ op: 'skip', id, date })) {
        back();
        toast(`Tolta la volta del ${shortDay(date, partsOf(state.today)[0])}`, () => change({ op: 'put', item: writeItem(old) }));
      }
    } },
    { label: 'Tutte le volte', danger: true, icon: uiIcons.trash, action: all },
  ], { width: 240 });
}

/** Un cambiamento: si applica qui subito e si manda alla finestra, che lo applica al calendario vero. */
function change(op) {
  if (state.readonly) return false;
  if (!applyOp(state.doc, op)) return false;
  send({ t: 'op', op });
  activity();
  render();
  return true;
}

function reveal(link) {
  if (!link) return;
  if (link.kind === 'node') send({ t: 'node', map: link.map, node: link.node, host: link.host });
  else send({ t: 'reveal', host: link.host });
}

// ------------------------------------------------------------------ certificati

let certPop = null;

function toggleCertSettings() {
  if (popups.current?.anchor === certButton) {
    popups.close();
    return;
  }
  certPop = certSettings(state, actions);
  popups.open(certPop, certButton);
}

function refreshCertSettings() {
  if (!certPop || popups.current?.el !== certPop) return;
  const fresh = certSettings(state, actions);
  certPop.replaceChildren(...fresh.childNodes);
}

// ------------------------------------------------------------------ avvisi della pagina

function toast(text, undo) {
  const close = () => vanish(el, { dy: 8, ms: 160, done: () => el.remove() });
  const el = h('div', { class: 'dc-toast', role: 'status' },
    h('span', { class: 'dc-toast-text' }, text),
    undo ? h('button', { class: 'dm-link-button', type: 'button', onclick: () => { undo(); close(); } }, 'Annulla') : null);
  toastLayer.replaceChildren(el);
  appear(el, { dy: 10, ms: 240 });
  setTimeout(close, 6000);
}

// ------------------------------------------------------------------ ricerca e tastiera

search.addEventListener('input', () => {
  state.query = search.value;
  if (state.view !== 'edit') state.view = state.query.trim() ? 'search' : 'day';
  renderPanel();
});
search.addEventListener('keydown', (event) => {
  if (event.key === 'Escape') {
    event.preventDefault();
    search.value = '';
    state.query = '';
    if (state.view === 'search') state.view = 'day';
    renderPanel();
    search.blur();
  }
});

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
  if (typingIn(event.target) || event.target?.closest?.('.dc-editor')) return;
  if (mod || event.altKey) return;

  const moveBy = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 }[event.key];
  if (moveBy) {
    event.preventDefault();
    pick(addDays(state.selected, moveBy));
  } else if (event.key === 'PageUp' || event.key === 'PageDown') {
    event.preventDefault();
    goMonth(event.key === 'PageUp' ? -1 : 1);
  } else if (event.key === 't' || event.key === 'T') {
    event.preventDefault();
    goToday();
  } else if ((event.key === 'n' || event.key === 'N') && !state.readonly) {
    event.preventDefault();
    create(state.selected);
  } else if ((event.key === 'e' || event.key === 'E' || event.key === 'Enter') && state.view === 'detail' && !state.readonly) {
    event.preventDefault();
    const occurrence = findOccurrence(state.detail);
    if (occurrence) editOccurrence(occurrence);
  } else if (event.key === 'Escape' && state.view !== 'day') {
    event.preventDefault();
    back();
  }
});

document.addEventListener('pointerdown', () => activity(), true);

// a mezzanotte "oggi" cambia anche se la pagina resta aperta
setInterval(() => {
  if (state.fixedToday) return;
  const today = localToday();
  if (today !== state.today) {
    const followed = state.selected === state.today;
    state.today = today;
    if (followed) {
      state.selected = today;
      state.month = today.slice(0, 7);
    }
    if (state.view !== 'edit') render();
    else month.render();
  }
}, 30000);

// ------------------------------------------------------------------ cosa chiede la finestra

/** Il calendario come arriva (testo JSON o oggetto); un testo rovinato e' un calendario vuoto. */
function parse(doc) {
  if (typeof doc !== 'string') return doc;
  try {
    return JSON.parse(doc || 'null');
  } catch {
    return null;
  }
}

window.dc = {
  /** Il calendario della nota (il JSON di calendario.json) e quello che serve per i legami. */
  load(doc, options = {}) {
    popups.close();
    state.doc = normalizeDoc(parse(doc));
    state.readonly = !!options.readonly;
    if (Array.isArray(options.hosts)) state.hosts = options.hosts;
    if (Array.isArray(options.devices)) state.devices = options.devices;
    const today = parseDay(options.today);
    if (today) {
      state.today = today;
      state.fixedToday = true;
    }
    state.month = state.today.slice(0, 7);
    state.selected = state.today;
    state.view = 'day';
    state.editing = null;
    state.cert = '';
    state.query = '';
    search.value = '';
    render();
    return true;
  },

  /** Il calendario aggiornato dalla finestra (dopo un cambiamento, o i certificati di un'analisi). */
  data(doc) {
    state.doc = normalizeDoc(parse(doc));
    if (state.view === 'edit' && state.editing && !state.editing.isNew && !state.doc.items.some((item) => item.id === state.editing.id)) back();
    else render();
    refreshCertSettings();
    return true;
  },

  /** Gli host che DuckNote conosce: [{ key, ip, name, state }]. */
  hosts(list) {
    state.hosts = Array.isArray(list) ? list : [];
    renderPanel();
  },

  /** Gli apparati delle mappe: [{ map, mapName, node, name, host, kind }]. */
  devices(list) {
    state.devices = Array.isArray(list) ? list : [];
  },

  theme(vars, dark) {
    applyTheme(vars, dark);
  },

  readonly(on) {
    state.readonly = !!on;
    if (state.view === 'edit') back();
    else render();
  },

  /** Una voce nuova gia' avviata (da Rete o dalla mappa): { kind, title, date, link, notes }. */
  compose(seed) {
    compose(seed ?? {});
    return true;
  },

  /** Un giorno, e se c'e' la voce "id@giorno" aperta. */
  show(day, key) {
    if (key && findOccurrence(String(key))) open(findOccurrence(String(key)));
    else if (parseDay(day)) pick(day);
    return true;
  },

  /** Via tutto: la nota si chiude a chiave. */
  clear() {
    popups.close();
    state.doc = normalizeDoc(null);
    state.hosts = [];
    state.devices = [];
    state.view = 'day';
    state.editing = null;
    state.query = '';
    search.value = '';
    toastLayer.replaceChildren();
    render();
    return true;
  },

  focus() {
    if (!typingIn(document.activeElement)) month.grid.focus({ preventScroll: true });
  },

  /** Le voci del menu Modifica del Mac, dentro ai campi. */
  edit(action) {
    const field = document.activeElement;
    if (!typingIn(field)) return false;
    if (action === 'undo' || action === 'redo') document.execCommand(action);
    else if (action === 'selectAll') field.select?.();
    else return false;
    return true;
  },

  /** Incolla un testo nel campo che ha il cursore (quando il sistema non manda l'evento degli appunti). */
  paste(text) {
    if (!typingIn(document.activeElement)) return false;
    document.execCommand('insertText', false, String(text ?? ''));
    return true;
  },

  // per le prove
  outbox() { return takeOutbox(); },
  get state() { return state; },
  get doc() { return JSON.stringify({ v: 1, items: state.doc.items.map(writeItem), certs: state.doc.certs }); },
};

month.grid.tabIndex = 0;
render();
send({ t: 'ready' });
