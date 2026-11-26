// L'editor della nota dentro il WebView di DuckNote. Tutto quello che la finestra chiede passa
// da window.dn (chiamato dal C# con InvokeScript); tutto quello che l'editor dice alla finestra
// passa da send() (invokeCSharpAction del WebView, un messaggio JSON per volta).
import { EditorState, Plugin, NodeSelection, TextSelection } from 'prosemirror-state';
import { EditorView } from 'prosemirror-view';
import { keymap } from 'prosemirror-keymap';
import { baseKeymap } from 'prosemirror-commands';
import { history, undo, redo } from 'prosemirror-history';
import { dropCursor } from 'prosemirror-dropcursor';
import { gapCursor } from 'prosemirror-gapcursor';
import { tableEditing, isInTable } from 'prosemirror-tables';
import { Node as PMNode, Slice, Fragment } from 'prosemirror-model';
import { schema, isMac } from './schema.js';
import { hostsPlugin, hostKey, hostAt, typingHost, setSettleDelay } from './hosts.js';
import { codePlugin } from './code.js';
import { findPlugin, FindBar } from './find.js';
import { InsetView, setInsetData, forgetInsets } from './insets.js';
import {
  editorKeys, markdownRules, toolbarState, toggle, colour, link, linkRange, setLook, toggleCode, insertRule,
  clearFormatting, insertTable, tableCommands, toggleTodo, tidyPlugin, withTrailingLine, insetSpot,
} from './commands.js';

// ------------------------------------------------------------------ verso la finestra

const outbox = [];
function send(message) {
  const text = JSON.stringify(message);
  try {
    if (typeof window.invokeCSharpAction === 'function') {
      window.invokeCSharpAction(text);
      return;
    }
  } catch { /* nel browser dei test non c'e' la finestra */ }
  outbox.push(message);
}

// ------------------------------------------------------------------ la pagina

document.body.innerHTML = `
  <div class="dn-margin" aria-hidden="true"></div>
  <main class="dn-page">
    <header class="dn-header">
      <input class="dn-title" placeholder="Senza titolo" spellcheck="false" aria-label="Titolo della pagina" maxlength="120">
      <div class="dn-meta"></div>
    </header>
    <div id="dn-editor"></div>
  </main>
  <div class="dn-empty" hidden aria-hidden="true">
    <div class="dn-empty-art"></div>
    <div class="dn-empty-title">Una pagina bianca</div>
    <div class="dn-empty-text">Scrivi qui. Titoli, elenchi, spunte e tabelle sono nella barra; gli indirizzi che scrivi vengono controllati da soli.</div>
  </div>
  <div class="dn-overlay"></div>`;

const titleBox = document.querySelector('.dn-title');
const metaLine = document.querySelector('.dn-meta');
const emptyHint = document.querySelector('.dn-empty');
const mount = document.getElementById('dn-editor');

let readonly = false;
let view = null;
let findBar = null;

// ------------------------------------------------------------------ chi ascolta l'editor

let changeTimer = null;
let stateFrame = 0;
let lastState = '';
let lastActivity = 0;

function activity() {
  const now = Date.now();
  if (now - lastActivity > 4000) {
    lastActivity = now;
    send({ t: 'activity' });
  }
}

function reportState() {
  if (stateFrame) return;
  stateFrame = requestAnimationFrame(() => {
    stateFrame = 0;
    if (!view) return;
    const state = JSON.stringify(toolbarState(view.state));
    if (state !== lastState) {
      lastState = state;
      send({ t: 'state', ...JSON.parse(state) });
    }
  });
}

function isBlank(doc) {
  if (doc.childCount !== 1) return false;
  const only = doc.firstChild;
  return only.type === schema.nodes.paragraph && only.content.size === 0;
}

function showEmpty() {
  emptyHint.hidden = !(view && isBlank(view.state.doc) && !titleBox.value);
}

const watcher = new Plugin({
  view: () => ({
    update(editorView, previous) {
      if (editorView.state.doc !== previous.doc) {
        clearTimeout(changeTimer);
        changeTimer = setTimeout(flush, 250);
        showEmpty();
        activity();
      } else if (hostKey.getState(previous)?.typing != null && hostKey.getState(editorView.state)?.typing == null) {
        // la parola che si scriveva e' finita (ci si e' fermati, o il cursore se n'e' andato):
        // adesso e' un indirizzo anche per la finestra, che lo mette in tabella e lo controlla
        send({ t: 'settled' });
      }
      reportState();
    },
  }),
});

function flush() {
  clearTimeout(changeTimer);
  changeTimer = null;
  if (view) send({ t: 'changed', doc: view.state.doc.toJSON(), typing: typingHost(view.state) });
}

// ------------------------------------------------------------------ incollare

function pastePlain(text, editorView) {
  const { state } = editorView;
  const lines = text.replace(/\r\n?/g, '\n').split('\n');
  if (state.selection.$from.parent.type.spec.code || lines.length === 1) {
    editorView.dispatch(state.tr.insertText(text.replace(/\r\n?/g, '\n')).scrollIntoView());
    return;
  }
  const paragraphs = lines.map((line) => schema.nodes.paragraph.create(null, line ? schema.text(line) : null));
  editorView.dispatch(state.tr.replaceSelection(new Slice(Fragment.from(paragraphs), 1, 1)).scrollIntoView());
}

function readImage(file) {
  const reader = new FileReader();
  reader.onload = () => send({ t: 'pasteImage', dataUrl: reader.result, name: file.name || 'immagine' });
  reader.readAsDataURL(file);
}

// ------------------------------------------------------------------ l'editor

function plugins() {
  return [
    history({ depth: 400, newGroupDelay: 600 }),
    markdownRules(),
    keymap(editorKeys({ askLink })),
    keymap(baseKeymap),
    dropCursor({ color: 'var(--c-Accent)', width: 2 }),
    gapCursor(),
    tableEditing(),
    hostsPlugin(),
    codePlugin(),
    findPlugin(),
    tidyPlugin(),
    watcher,
  ];
}

function makeState(doc) {
  return EditorState.create({ schema, doc, plugins: plugins() });
}

function askLink() {
  if (!view) return;
  const { state } = view;
  const found = linkRange(state, state.selection.from);
  const text = found ? state.doc.textBetween(found.from, found.to) : state.doc.textBetween(state.selection.from, state.selection.to, ' ');
  send({ t: 'askLink', href: found?.href ?? '', text });
}

function create(doc) {
  view = new EditorView(mount, {
    state: makeState(doc),
    editable: () => !readonly,
    attributes: { spellcheck: 'false', autocorrect: 'off', autocapitalize: 'off', 'aria-label': 'Testo della pagina', 'aria-multiline': 'true' },
    nodeViews: { inset: (node, editorView, getPos) => new InsetView(node, editorView, getPos, send) },
    handlePaste(editorView, event) {
      const data = event.clipboardData;
      if (!data) return false;
      const image = [...data.files].find((file) => file.type.startsWith('image/'));
      if (image) {
        readImage(image);
        return true;
      }
      // copiato da DuckNote: tiene titoli, elenchi e segni; da fuori arriva testo semplice
      const html = data.getData('text/html');
      if (html && html.includes('data-pm-slice')) return false;
      const text = data.getData('text/plain');
      if (!text) return false;
      pastePlain(text, editorView);
      return true;
    },
    handleDrop(editorView, event) {
      const image = [...(event.dataTransfer?.files ?? [])].find((file) => file.type.startsWith('image/'));
      if (!image) return false;
      const at = editorView.posAtCoords({ left: event.clientX, top: event.clientY });
      if (at) editorView.dispatch(editorView.state.tr.setSelection(TextSelection.create(editorView.state.doc, at.pos)));
      readImage(image);
      return true;
    },
    handleClickOn(editorView, pos, node, nodePos, event, direct) {
      // il quadratino di una spunta
      if (node.type === schema.nodes.todo && direct) {
        const box = editorView.nodeDOM(nodePos)?.getBoundingClientRect();
        if (box && event.clientX - box.left < 22) {
          toggleTodo(nodePos)(editorView.state, editorView.dispatch);
          return true;
        }
      }
      return false;
    },
    handleDOMEvents: {
      click(editorView, event) {
        const anchor = event.target.closest?.('a[href]');
        if (!anchor) return false;
        event.preventDefault();
        if (isMac() ? event.metaKey : event.ctrlKey) send({ t: 'open', href: anchor.getAttribute('href') });
        return true;
      },
      contextmenu(editorView, event) {
        event.preventDefault();
        const at = editorView.posAtCoords({ left: event.clientX, top: event.clientY });
        const state = editorView.state;
        // il destro fuori dalla selezione sposta il cursore li', come in ogni editor
        if (at && (state.selection.empty || at.pos < state.selection.from || at.pos > state.selection.to)) {
          editorView.dispatch(state.tr.setSelection(TextSelection.create(state.doc, at.pos)));
        }
        const host = at ? hostAt(editorView.state, at.pos) : null;
        const block = at ? blockAt(editorView.state, at.inside >= 0 ? at.inside : at.pos) : null;
        send({
          t: 'context',
          x: event.clientX,
          y: event.clientY,
          host,
          link: at ? linkRange(editorView.state, at.pos)?.href ?? null : null,
          selection: !editorView.state.selection.empty,
          inTable: isInTable(editorView.state),
          block,
        });
        return true;
      },
      keydown(editorView, event) {
        activity();
        return appKey(event);
      },
      mousedown() { activity(); return false; },
    },
  });
  findBar = new FindBar(view, document.querySelector('.dn-overlay'));
  showEmpty();
  reportState();
}

// il blocco eliminabile col destro: tabella, separazione, codice, citazione
function blockAt(state, pos) {
  const $pos = state.doc.resolve(Math.min(Math.max(pos, 0), state.doc.content.size));
  for (let depth = $pos.depth; depth >= 0; depth--) {
    const node = $pos.node(depth);
    if (node.type === schema.nodes.table) return { kind: 'table', pos: $pos.before(depth) };
    if (node.type === schema.nodes.code_block) return { kind: 'code', pos: $pos.before(depth) };
    if (node.type === schema.nodes.quote) return { kind: 'quote', pos: $pos.before(depth) };
  }
  const after = $pos.nodeAfter;
  if (after?.type === schema.nodes.rule) return { kind: 'rule', pos: $pos.pos };
  return null;
}

// le scorciatoie della finestra: dal WebView i tasti non arrivano alla finestra, li si passa
function appKey(event) {
  const mod = isMac() ? event.metaKey : event.ctrlKey;
  const key = event.key;
  let name = null;
  if (key === 'F9' && !mod && !event.altKey) name = 'sidebar';
  else if (mod && (key === '\\' || event.code === 'Backslash' || event.code === 'IntlBackslash')) name = 'sidebar';
  else if (mod && !event.shiftKey && (key === 'l' || key === 'L')) name = 'lock';
  else if (mod && event.shiftKey && (key === 'f' || key === 'F')) name = 'searchSheets';
  else if (mod && !event.shiftKey && (key === 'f' || key === 'F')) { findBar.open(view.state.doc.textBetween(view.state.selection.from, view.state.selection.to, ' ')); event.preventDefault(); return true; }
  else if (key === 'F3') { if (findBar.isOpen) findBar.hop(event.shiftKey ? -1 : 1); else findBar.open(''); event.preventDefault(); return true; }
  else if (mod && (key === '+' || key === '=' )) name = 'zoomIn';
  else if (mod && (key === '-' || key === '_') && !event.shiftKey) name = 'zoomOut';
  else if (mod && key === '0' && !event.altKey) name = 'zoomReset';
  else if (mod && key === ',') name = 'settings';
  else if (event.altKey && key === 'F10') name = 'toolbar';
  if (!name) return false;
  event.preventDefault();
  send({ t: 'key', key: name });
  return true;
}

window.addEventListener('keydown', (event) => {
  // i tasti premuti fuori dall'editor (nel titolo, nella ricerca) valgono lo stesso
  if (view && !view.dom.contains(event.target)) {
    activity();
    if (event.target !== titleBox || !['Enter', 'Escape'].includes(event.key)) appKey(event);
  }
}, false);

window.addEventListener('pointerdown', activity, true);

// ------------------------------------------------------------------ il titolo

let titleTimer = null;
titleBox.addEventListener('input', () => {
  showEmpty();
  clearTimeout(titleTimer);
  titleTimer = setTimeout(() => send({ t: 'title', text: titleBox.value }), 400);
});
titleBox.addEventListener('keydown', (event) => {
  if (event.key === 'Enter' || event.key === 'ArrowDown') {
    event.preventDefault();
    send({ t: 'title', text: titleBox.value, done: true });
    view?.focus();
  } else if (event.key === 'Escape') {
    event.preventDefault();
    titleBox.value = titleBox.dataset.saved ?? '';
    view?.focus();
  }
});
titleBox.addEventListener('blur', () => {
  clearTimeout(titleTimer);
  if (titleBox.value !== (titleBox.dataset.saved ?? '')) send({ t: 'title', text: titleBox.value, done: true });
});

// ------------------------------------------------------------------ cosa chiede la finestra

const commands = {
  undo: () => undo(view.state, view.dispatch),
  redo: () => redo(view.state, view.dispatch),
  bold: () => toggle(schema.marks.bold)(view.state, view.dispatch),
  italic: () => toggle(schema.marks.italic)(view.state, view.dispatch),
  underline: () => toggle(schema.marks.underline)(view.state, view.dispatch),
  strike: () => toggle(schema.marks.strike)(view.state, view.dispatch),
  highlight: () => toggle(schema.marks.highlight)(view.state, view.dispatch),
  code: () => toggle(schema.marks.code)(view.state, view.dispatch),
  colour: (token) => colour(token)(view.state, view.dispatch),
  look: (look) => setLook(look)(view.state, view.dispatch),
  codeBlock: () => toggleCode(view.state, view.dispatch),
  rule: () => insertRule(view.state, view.dispatch),
  clear: () => clearFormatting(view.state, view.dispatch),
  link: (arg) => link(arg?.href, arg?.text)(view.state, view.dispatch),
  askLink: () => askLink(),
  table: () => insertTable(view.state, view.dispatch),
  addRow: () => tableCommands.addRowAfter(view.state, view.dispatch),
  removeRow: () => tableCommands.deleteRow(view.state, view.dispatch),
  addColumn: () => tableCommands.addColumnAfter(view.state, view.dispatch),
  removeColumn: () => tableCommands.deleteColumn(view.state, view.dispatch),
  find: () => (findBar.isOpen ? findBar.close() : findBar.open(view.state.doc.textBetween(view.state.selection.from, view.state.selection.to, ' '))),
  dropBlock: (pos) => {
    const node = view.state.doc.nodeAt(pos);
    if (!node) return false;
    view.dispatch(view.state.tr.delete(pos, pos + node.nodeSize).scrollIntoView());
    return true;
  },
  selectAll: () => view.dispatch(view.state.tr.setSelection(new TextSelection(view.state.doc.resolve(0), view.state.doc.resolve(view.state.doc.content.size)))),
  paste: (text) => pastePlain(text ?? '', view),
  copyHost: (host) => navigator.clipboard?.writeText(host),
};

window.dn = {
  // un foglio nuovo nell'editor: cronologia di Annulla nuova, cursore in cima
  load(doc, options = {}) {
    const node = withTrailingLine(PMNode.fromJSON(schema, typeof doc === 'string' ? JSON.parse(doc) : doc));
    clearTimeout(changeTimer);
    changeTimer = null;
    if (findBar?.isOpen) findBar.close();
    if (!view) create(node);
    else view.updateState(makeState(node));
    readonly = !!options.readonly;
    view.setProps({ editable: () => !readonly });
    titleBox.value = options.title ?? '';
    titleBox.dataset.saved = titleBox.value;
    titleBox.readOnly = readonly;
    metaLine.textContent = options.meta ?? '';
    if (options.hosts) this.hosts(options.hosts.states, options.hosts.hidden);
    if (options.settleMs) setSettleDelay(options.settleMs);
    window.scrollTo(0, 0);
    showEmpty();
    lastState = '';
    reportState();
    if (options.focus !== false && !readonly) {
      view.dispatch(view.state.tr.setSelection(TextSelection.atStart(view.state.doc)));
      view.focus();
    }
    return true;
  },

  // l'impostazione "Una parola diventa host dopo (ms)"
  settle(ms) { setSettleDelay(ms); },

  // il foglio adesso, subito (per salvare prima di bloccare o chiudere)
  doc() {
    if (!view) return null;
    clearTimeout(changeTimer);
    changeTimer = null;
    return JSON.stringify(view.state.doc.toJSON());
  },

  // via tutto dalla pagina: la nota si chiude a chiave
  clear() {
    forgetInsets();
    if (view) view.updateState(makeState(schema.nodes.doc.create(null, schema.nodes.paragraph.create())));
    titleBox.value = '';
    metaLine.textContent = '';
    return true;
  },

  meta(text) { metaLine.textContent = text ?? ''; },

  title(text) {
    if (document.activeElement !== titleBox) {
      titleBox.value = text ?? '';
      titleBox.dataset.saved = titleBox.value;
      showEmpty();
    }
  },

  focusTitle() { titleBox.focus(); titleBox.select(); },

  theme(vars, dark) {
    const root = document.documentElement;
    for (const [name, value] of Object.entries(vars)) root.style.setProperty(`--c-${name}`, value);
    root.dataset.theme = dark ? 'dark' : 'light';
  },

  zoom(percent) {
    document.documentElement.style.setProperty('--zoom', String(Math.max(0.5, Math.min(3, percent / 100))));
  },

  hosts(states, hidden) {
    if (view) view.dispatch(view.state.tr.setMeta(hostKey, { states: states ?? {}, hidden: hidden ?? [] }));
  },

  insetData(id, value) { setInsetData(id, value); },

  insertInset(attrs) {
    if (!view) return false;
    const node = schema.nodes.inset.create(attrs);
    const at = insetSpot(view.state);
    const tr = at === null ? view.state.tr.replaceSelectionWith(node, false) : view.state.tr.insert(at, node);
    view.dispatch(tr.scrollIntoView());
    view.focus();
    return true;
  },

  cmd(name, arg) {
    if (!view || (readonly && !['find', 'selectAll', 'copyHost'].includes(name))) return false;
    const done = commands[name]?.(arg);
    if (name !== 'find' && name !== 'askLink') view.focus();
    return done !== false;
  },

  focus() { view?.focus(); },

  // un titolo dell'indice: la pagina scorre fin li', con un po' d'aria sopra
  heading(index) {
    if (!view) return false;
    let seen = -1;
    let target = null;
    view.state.doc.descendants((node, pos) => {
      if (target != null) return false;
      if (node.type === schema.nodes.heading && ++seen === index) target = pos;
      return true;
    });
    if (target == null) return false;
    view.dispatch(view.state.tr.setSelection(TextSelection.create(view.state.doc, target + 1)));
    const top = view.coordsAtPos(target + 1).top + window.scrollY - 60;
    window.scrollTo({ top: top < 120 ? 0 : top, behavior: 'smooth' });
    view.focus();
    return true;
  },

  // per i test: i messaggi che sarebbero andati alla finestra
  outbox() { return outbox.splice(0); },

  get view() { return view; },
};

create(schema.nodes.doc.create(null, schema.nodes.paragraph.create()));
send({ t: 'ready' });
