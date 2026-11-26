// La barra in alto e la libreria a sinistra: gli apparati da trascinare sulla mappa e gli host
// che la scansione di DuckNote ha gia' trovato.

import { h, icon, fill, shortcut } from './dom.js';
import { ui as uiIcons, kinds as kindIcons } from './icons.js';
import { KINDS, NODE_W, NODE_H, hostKey, kindFromScan, nameFromScan } from './model.js';
import { tween, Curves, stop } from './motion.js';

// ------------------------------------------------------------------ pulsanti

export function toolButton(path, title, onclick, extra = {}) {
  return h('button', { class: 'dm-tool dm-press', type: 'button', title, 'aria-label': title, onclick, ...extra }, icon(path, 16));
}

export function textButton(path, label, title, onclick, extra = {}) {
  return h('button', { class: 'dm-tool wide dm-press', type: 'button', title, onclick, ...extra }, path ? icon(path, 15) : null, h('span', {}, label));
}

/**
 * Le linguette con la pillola: la pillola si sposta sotto la voce scelta con la molla delle app
 * (520 ms in uscita elastica, la larghezza in 380 ms esponenziale).
 */
export function segments(items, onChange) {
  const pill = h('span', { class: 'dm-segment-pill', 'aria-hidden': 'true' });
  const wrap = h('div', { class: 'dm-segment', role: 'radiogroup' }, pill);
  const buttons = new Map();
  let value = null;
  let x = 0;
  let width = 0;
  for (const item of items) {
    const button = h('button', {
      class: 'dm-segment-item',
      type: 'button',
      role: 'radio',
      title: item.title,
      onclick: () => {
        if (value !== item.id) {
          set(item.id);
          onChange(item.id);
        }
      },
    }, item.icon ? icon(item.icon, 14) : null, h('span', {}, item.label));
    buttons.set(item.id, button);
    wrap.append(button);
  }
  const place = (immediate) => {
    const button = buttons.get(value);
    if (!button || !button.offsetWidth) return;
    const toX = button.offsetLeft;
    const toW = button.offsetWidth;
    if (immediate || !width) {
      stop(pill, 'x');
      stop(pill, 'w');
      x = toX;
      width = toW;
      pill.style.transform = `translateX(${x}px)`;
      pill.style.width = `${width}px`;
      return;
    }
    tween(pill, 'x', x, toX, 520, (v) => {
      x = v;
      pill.style.transform = `translateX(${v}px)`;
    }, null, Curves.elasticOut(1, 8));
    tween(pill, 'w', width, toW, 380, (v) => {
      width = v;
      pill.style.width = `${Math.max(0, v)}px`;
    }, null, Curves.expoOut(4));
  };
  const set = (id, immediate = false) => {
    value = id;
    for (const [key, button] of buttons) {
      button.classList.toggle('on', key === id);
      button.setAttribute('aria-checked', String(key === id));
    }
    place(immediate);
  };
  new ResizeObserver(() => place(true)).observe(wrap);
  return { el: wrap, set, get value() { return value; } };
}

// ------------------------------------------------------------------ trascinare dalla libreria

/**
 * Un elemento della libreria preso col mouse: un clic lo mette al centro della vista, un
 * trascinamento lo posa dove si lascia (se e' sopra alla mappa).
 */
export function dragOut(event, { label, kind, canvas, onClick, onDrop }) {
  if (event.button !== 0) return;
  event.preventDefault();
  const start = { x: event.clientX, y: event.clientY };
  let ghost = null;
  const over = (e) => {
    const r = canvas.getBoundingClientRect();
    return e.clientX >= r.left && e.clientX <= r.right && e.clientY >= r.top && e.clientY <= r.bottom;
  };
  const move = (e) => {
    if (!ghost && Math.hypot(e.clientX - start.x, e.clientY - start.y) < 4) return;
    if (!ghost) {
      const tone = KINDS.find((k) => k.id === kind)?.tone ?? 'Gray';
      ghost = h('div', { class: 'dm-ghost', style: { '--tone': `var(--c-${tone})`, '--tone-soft': `var(--t-${tone}-soft)` } },
        h('span', { class: 'dm-ghost-tile' }, icon(kindIcons[kind] ?? kindIcons.other, 20, 24)), h('span', { class: 'dm-ghost-label' }, label));
      document.body.append(ghost);
      document.body.classList.add('dm-dragging-out');
    }
    ghost.style.transform = `translate(${e.clientX - NODE_W / 2}px, ${e.clientY - NODE_H / 2}px)`;
    canvas.classList.toggle('drop', over(e));
  };
  const up = (e) => {
    window.removeEventListener('pointermove', move, true);
    window.removeEventListener('pointerup', up, true);
    window.removeEventListener('pointercancel', cancel, true);
    canvas.classList.remove('drop');
    document.body.classList.remove('dm-dragging-out');
    if (!ghost) {
      onClick();
      return;
    }
    ghost.remove();
    if (over(e)) onDrop(e.clientX, e.clientY);
  };
  const cancel = () => {
    window.removeEventListener('pointermove', move, true);
    window.removeEventListener('pointerup', up, true);
    window.removeEventListener('pointercancel', cancel, true);
    canvas.classList.remove('drop');
    document.body.classList.remove('dm-dragging-out');
    ghost?.remove();
  };
  window.addEventListener('pointermove', move, true);
  window.addEventListener('pointerup', up, true);
  window.addEventListener('pointercancel', cancel, true);
}

// ------------------------------------------------------------------ la libreria

const STATE_LABEL = { up: 'attivo', down: 'spento', wait: 'da verificare', unknown: 'mai controllato' };

export class Library {
  /**
   * actions: { addKind(kind, clientX?, clientY?), addHost(host, clientX?, clientY?), addAll(),
   *            reveal(nodeId), openNetwork() }
   */
  constructor(store, canvas, actions) {
    this.store = store;
    this.canvas = canvas;
    this.actions = actions;
    this.query = '';

    const kinds = h('div', { class: 'dm-kinds' });
    for (const kind of KINDS) {
      const tile = h('button', {
        class: 'dm-kind-tile dm-press',
        type: 'button',
        title: `${kind.label}: trascinalo sulla mappa, o clic per metterlo al centro`,
        style: { '--tone': `var(--c-${kind.tone})`, '--tone-soft': `var(--t-${kind.tone}-soft)` },
      }, h('span', { class: 'dm-kind-tile-icon' }, icon(kindIcons[kind.id], 20, 24)), h('span', { class: 'dm-kind-tile-label' }, kind.label));
      tile.addEventListener('pointerdown', (event) => {
        if (this.store.readonly) return;
        dragOut(event, {
          label: kind.label,
          kind: kind.id,
          canvas,
          onClick: () => actions.addKind(kind.id),
          onDrop: (x, y) => actions.addKind(kind.id, x, y),
        });
      });
      kinds.append(tile);
    }

    this.count = h('span', { class: 'dm-count' });
    this.allButton = h('button', { class: 'dm-link-button', type: 'button', onclick: () => actions.addAll(), title: 'Mette sulla mappa tutti gli host trovati che non ci sono ancora' }, 'Aggiungi tutti');
    this.search = h('input', { class: 'dm-field small', type: 'search', placeholder: 'Filtra gli host', spellcheck: 'false', 'aria-label': 'Filtra gli host trovati' });
    this.search.addEventListener('input', () => {
      this.query = this.search.value.trim().toLowerCase();
      this.renderHosts();
    });
    this.list = h('div', { class: 'dm-hosts', role: 'list' });
    this.empty = h('div', { class: 'dm-hosts-empty', hidden: true },
      h('div', {}, 'Nessun host trovato finora.'),
      h('button', { class: 'dm-button small dm-press', type: 'button', onclick: () => actions.openNetwork() }, icon(uiIcons.network, 13), h('span', {}, 'Analizza la rete')));

    this.el = h('aside', { class: 'dm-library', 'aria-label': 'Libreria' },
      h('div', { class: 'dm-section' }, h('div', { class: 'dm-section-title' }, 'Apparati'), kinds),
      h('div', { class: 'dm-section grow' },
        h('div', { class: 'dm-section-title' }, h('span', {}, 'Dalla rete'), this.count, h('span', { class: 'dm-spacer' }), this.allButton),
        this.search,
        this.list,
        this.empty));
  }

  renderHosts() {
    const placed = this.store.placedHosts();
    const hosts = this.store.hosts;
    const q = this.query;
    const shown = hosts.filter((host) => !q || `${host.key} ${host.ip} ${host.name} ${host.vendor} ${host.type} ${host.mac}`.toLowerCase().includes(q));
    // prima quelli che mancano dalla mappa, poi in ordine di indirizzo
    const order = (host) => (placed.has(hostKey(host.key || host.ip)) ? 1 : 0);
    shown.sort((a, b) => order(a) - order(b) || compareHosts(a, b));
    const missing = hosts.filter((host) => !placed.has(hostKey(host.key || host.ip))).length;
    this.count.textContent = hosts.length ? String(hosts.length) : '';
    this.allButton.hidden = missing === 0 || this.store.readonly;
    this.empty.hidden = hosts.length > 0;
    this.search.hidden = hosts.length < 8;

    const rows = shown.slice(0, 400).map((host) => {
      const key = hostKey(host.key || host.ip);
      const node = placed.get(key);
      const state = host.state ?? 'unknown';
      const title = host.name || host.key || host.ip;
      const sub = [host.name && host.ip !== title ? host.ip : '', host.type || host.vendor].filter(Boolean).join(' · ');
      const row = h('div', {
        class: `dm-host${node ? ' placed' : ''}`,
        role: 'listitem',
        title: node ? `${title}: e' gia' sulla mappa (clic per trovarlo)` : `${title} (${STATE_LABEL[state] ?? state}): trascinalo sulla mappa`,
      },
      h('span', { class: `dm-dot st-${state}` }),
      h('span', { class: 'dm-host-text' }, h('span', { class: 'dm-host-name' }, title), sub ? h('span', { class: 'dm-host-sub' }, sub) : null),
      node ? h('span', { class: 'dm-host-check' }, icon(uiIcons.check, 13)) : null);
      row.addEventListener('pointerdown', (event) => {
        if (node) {
          if (event.button === 0) this.actions.reveal(node.id);
          return;
        }
        if (this.store.readonly) return;
        dragOut(event, {
          label: nameFromScan(host) || title,
          kind: kindFromScan(host),
          canvas: this.canvas,
          onClick: () => this.actions.addHost(host),
          onDrop: (x, y) => this.actions.addHost(host, x, y),
        });
      });
      return row;
    });
    fill(this.list, ...rows);
  }
}

const ipValue = (value) => (/^\d+\.\d+\.\d+\.\d+$/.test(value ?? '') ? value.split('.').reduce((acc, part) => acc * 256 + Number(part), 0) : null);

function compareHosts(a, b) {
  const x = ipValue(a.ip);
  const y = ipValue(b.ip);
  if (x != null && y != null) return x - y;
  if (x != null) return -1;
  if (y != null) return 1;
  return String(a.key).localeCompare(String(b.key), 'it', { numeric: true });
}

export { shortcut };
