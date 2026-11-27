// La barra in alto e la libreria a sinistra: gli apparati da trascinare sulla mappa e gli host
// che la scansione di DuckNote ha gia' trovato.

import { h, icon, fill, shortcut } from '../common/dom.js';
import { ui as uiIcons, kinds as kindIcons } from '../common/icons.js';
import { KINDS, NODE_W, NODE_H, hostKey, kindFromScan, nameFromScan } from './model.js';
import { segments } from '../common/segments.js';

// ------------------------------------------------------------------ pulsanti

export function toolButton(path, title, onclick, extra = {}) {
  return h('button', { class: 'dm-tool dm-press', type: 'button', title, 'aria-label': title, onclick, ...extra }, icon(path, 16));
}

export function textButton(path, label, title, onclick, extra = {}) {
  return h('button', { class: 'dm-tool wide dm-press', type: 'button', title, onclick, ...extra }, path ? icon(path, 15) : null, h('span', {}, label));
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

/** I gruppi che ci sono sempre: tutti gli host, quelli della nota, quelli fuori da tutto. */
const ALL = '*';
const NOTE = 'nota';
const NONE = '-';

/** L'host sta nel gruppo? (host.groups: gli identificativi, con "nota" per quelli scritti nella nota) */
export function inGroup(host, group) {
  const groups = Array.isArray(host.groups) ? host.groups : [];
  if (group === ALL) return true;
  if (group === NONE) return groups.length === 0;
  return groups.includes(group);
}

export class Library {
  /**
   * actions: { addKind(kind, clientX?, clientY?), addHost(host, clientX?, clientY?), addAll(hosts),
   *            reveal(nodeId), openNetwork() }
   */
  constructor(store, canvas, actions, popups) {
    this.store = store;
    this.canvas = canvas;
    this.actions = actions;
    this.popups = popups;
    this.query = '';
    this.group = ALL;

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
    this.allButton = h('button', { class: 'dm-link-button', type: 'button', onclick: () => actions.addAll(this.groupHosts()), title: 'Mette sulla mappa gli host del gruppo che non ci sono ancora' }, 'Aggiungi tutti');
    // il gruppo di scansione: tutti, la nota, un gruppo fatto nella pagina Rete, o chi non sta in nessuno
    this.groupLabel = h('span', { class: 'dm-group-label' }, 'Tutti i gruppi');
    this.groupButton = h('button', {
      class: 'dm-group-pick dm-press',
      type: 'button',
      'aria-label': 'Gruppo da mostrare',
      onclick: () => (this.popups?.current?.anchor === this.groupButton ? this.popups.close() : this.groupMenu()),
    }, icon(uiIcons.group, 13), this.groupLabel, icon(uiIcons.down, 11));
    this.search = h('input', { class: 'dm-field small', type: 'search', placeholder: 'Filtra gli host', spellcheck: 'false', 'aria-label': 'Filtra gli host trovati' });
    this.search.addEventListener('input', () => {
      this.query = this.search.value.trim().toLowerCase();
      this.renderHosts();
    });
    this.list = h('div', { class: 'dm-hosts', role: 'list' });
    this.emptyText = h('div', {}, 'Nessun host trovato finora.');
    this.empty = h('div', { class: 'dm-hosts-empty', hidden: true },
      this.emptyText,
      h('button', { class: 'dm-button small dm-press', type: 'button', onclick: () => actions.openNetwork() }, icon(uiIcons.network, 13), h('span', {}, 'Analizza la rete')));

    this.el = h('aside', { class: 'dm-library', 'aria-label': 'Libreria' },
      h('div', { class: 'dm-section' }, h('div', { class: 'dm-section-title' }, 'Apparati'), kinds),
      h('div', { class: 'dm-section grow' },
        h('div', { class: 'dm-section-title' }, h('span', {}, 'Dalla rete'), this.count, h('span', { class: 'dm-spacer' }), this.allButton),
        this.groupButton,
        this.search,
        this.list,
        this.empty));
  }

  /** Gli host del gruppo scelto. */
  groupHosts() {
    return this.store.hosts.filter((host) => inGroup(host, this.group));
  }

  /** Il nome del gruppo scelto; un gruppo che non c'e' piu' torna a "Tutti i gruppi". */
  groupName() {
    if (this.group === ALL) return 'Tutti i gruppi';
    if (this.group === NOTE) return 'Nota';
    if (this.group === NONE) return 'Ricerca';
    const group = this.store.groups.find((g) => g.id === this.group);
    if (group) return group.name;
    this.group = ALL;
    return 'Tutti i gruppi';
  }

  pickGroup(id) {
    this.group = id;
    this.renderHosts();
    this.list.scrollTop = 0;
  }

  groupMenu() {
    const hosts = this.store.hosts;
    const count = (id) => String(hosts.filter((host) => inGroup(host, id)).length);
    const entry = (id, label, title) => ({ label, hint: count(id), checked: this.group === id, action: () => this.pickGroup(id), title });
    const groups = this.store.groups;
    this.popups.menu(this.groupButton, [
      entry(ALL, 'Tutti i gruppi'),
      entry(NOTE, 'Nota'),
      groups.length ? 'sep' : null,
      ...groups.map((group) => ({ ...entry(group.id, group.name), icon: group.monitor ? uiIcons.pulse : null })),
      'sep',
      entry(NONE, 'Ricerca'),
      'sep',
      { label: groups.length ? 'Modifica i gruppi in Rete' : 'Crea i gruppi in Rete', icon: uiIcons.network, action: () => this.actions.openNetwork() },
    ], { width: 250 });
  }

  renderHosts() {
    const placed = this.store.placedHosts();
    const all = this.store.hosts;
    this.groupLabel.textContent = this.groupName();
    const hosts = this.groupHosts();
    const q = this.query;
    const shown = hosts.filter((host) => !q || `${host.key} ${host.ip} ${host.name} ${host.vendor} ${host.type} ${host.mac}`.toLowerCase().includes(q));
    // prima quelli che mancano dalla mappa, poi in ordine di indirizzo
    const order = (host) => (placed.has(hostKey(host.key || host.ip)) ? 1 : 0);
    shown.sort((a, b) => order(a) - order(b) || compareHosts(a, b));
    const missing = hosts.filter((host) => !placed.has(hostKey(host.key || host.ip))).length;
    this.count.textContent = hosts.length ? String(hosts.length) : '';
    this.allButton.hidden = missing === 0 || this.store.readonly;
    this.groupButton.hidden = all.length === 0 && this.store.groups.length === 0;
    this.groupButton.classList.toggle('chosen', this.group !== ALL);
    this.empty.hidden = hosts.length > 0;
    this.emptyText.textContent = all.length === 0 ? 'Nessun host trovato finora.' : 'Nessun host in questo gruppo, per ora: analizzalo dalla pagina Rete.';
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

export { shortcut, segments };
