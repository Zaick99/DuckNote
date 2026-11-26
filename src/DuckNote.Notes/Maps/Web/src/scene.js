// Il disegno della mappa in SVG: zone sotto, cavi in mezzo, apparati sopra. Ogni oggetto del
// modello ha il suo elemento, creato una volta e poi solo aggiornato (render si chiama spesso:
// a ogni fotogramma di un trascinamento).

import { s, attr, text, toggleClass, fit, measure } from './dom.js';
import { NODE_W, NODE_H, KIND, MIN_ZOOM, MAX_ZOOM, PORTED, carriesVlan, parseVlans } from './model.js';
import { kinds as kindIcons } from './icons.js';
import { bundles, linkPath } from './geometry.js';
import { tween, Curves } from './motion.js';
import { placeLabels } from './labels.js';

export const FONT = '-apple-system, BlinkMacSystemFont, "SF Pro Text", "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif';
export const MONO = '"SF Mono", ui-monospace, Menlo, "Cascadia Mono", Consolas, monospace';
export const NAME_FONT = `600 12.5px ${FONT}`;
export const SUB_FONT = `11px ${MONO}`;
export const SUB_TEXT_FONT = `11px ${FONT}`;
export const CHIP_FONT = `10.5px ${MONO}`;
export const ZONE_FONT = `600 12px ${FONT}`;
export const ZONE_SUB_FONT = `11px ${MONO}`;

const TEXT_X = 54;
const TEXT_MAX = NODE_W - TEXT_X - 12;

/** La seconda riga di un apparato: l'indirizzo, oppure il tipo (e le porte). */
export function subtitleOf(node) {
  if (node.host) return { text: node.host, mono: true };
  const kind = KIND[node.kind]?.label ?? 'Apparato';
  return { text: PORTED.has(node.kind) && node.ports ? `${kind} · ${node.ports} porte` : kind, mono: false };
}

/** L'etichetta in mezzo al cavo: velocita' e VLAN. */
export function linkLabel(link) {
  const parts = [];
  if (link.speed) parts.push(link.speed);
  if (link.vlan) parts.push(/^\d+$/.test(link.vlan) ? `VLAN ${link.vlan}` : `VLAN ${link.vlan}`);
  return parts.join(' · ');
}

export function zoneLabel(zone) {
  const parts = [];
  if (zone.cidr) parts.push(zone.cidr);
  if (zone.vlan) parts.push(`VLAN ${zone.vlan}`);
  return parts.join(' · ');
}

/** Spessore del tratto dalla velocita' del cavo. */
export function strokeOf(link) {
  const speed = link.speed;
  if (!speed) return 1.6;
  const n = Number.parseFloat(speed) * (speed.endsWith('G') ? 1000 : 1);
  if (n <= 100) return 1.25;
  if (n <= 1000) return 1.75;
  if (n <= 5000) return 2.25;
  if (n <= 10000) return 2.75;
  return 3.4;
}

/** La testata di una zona: nome e rete, tagliati perche' stiano nella zona, e la sua larghezza. */
export function zoneHead(zone) {
  const name = fit(zone.name || 'Zona', ZONE_FONT, Math.max(40, zone.w - 70));
  const nameWidth = Math.ceil(measure(name, ZONE_FONT));
  const label = zoneLabel(zone);
  const sub = label ? fit(label, ZONE_SUB_FONT, Math.max(0, zone.w - nameWidth - 84)) : '';
  const subWidth = sub && sub !== '…' ? Math.ceil(measure(sub, ZONE_SUB_FONT)) + 10 : 0;
  return { name, nameWidth, sub: subWidth ? sub : '', subWidth, width: 22 + nameWidth + subWidth + 12 };
}

export const zoneHeadRect = (zone) => ({ x: zone.x + 12, y: zone.y + 10, w: zoneHead(zone).width, h: 24 });

/**
 * I cavi da disegnare: la strada di ognuno, i testi delle sue etichette e il posto dove vanno,
 * scelto perche' nessuna copra le altre (vedi labels.js). Lo usano lo schermo e l'esportazione.
 */
export function linkLayout(map, quick = false) {
  const nodes = new Map(map.nodes.map((n) => [n.id, n]));
  const place = bundles(map.links);
  const entries = [];
  for (const link of map.links) {
    const a = nodes.get(link.a);
    const b = nodes.get(link.b);
    if (!a || !b) continue;
    const { index, count } = place.get(link.id);
    entries.push({
      link,
      a,
      b,
      path: linkPath(a, b, index, count),
      pa: fit(link.pa, CHIP_FONT, 120),
      pb: fit(link.pb, CHIP_FONT, 120),
      mid: fit(linkLabel(link), CHIP_FONT, 170),
    });
  }
  const spots = placeLabels(map.nodes, entries, map.zones.map(zoneHeadRect), CHIP_FONT, { quick });
  for (const entry of entries) entry.spots = spots.get(entry.link.id);
  return entries;
}

const toneVars = (tone) => `--tone: var(--c-${tone}); --tone-soft: var(--t-${tone}-soft); --tone-border: var(--t-${tone}-border); --tone-zone: var(--t-${tone}-zone)`;

export class Scene {
  constructor(host, store) {
    this.host = host;
    this.store = store;
    this.view = { x: 0, y: 0, k: 1 };
    this.filter = { text: '', vlan: null };
    this.els = { nodes: new Map(), links: new Map(), labels: new Map(), zones: new Map() };
    this.frame = 0;
    // durante un trascinamento le etichette si sistemano alla buona, a fine gesto per bene
    this.quick = false;
    this.listeners = new Set();
    this.pulse = new Set();

    this.svg = s('svg', { class: 'dm-svg', role: 'img', 'aria-label': 'Mappa della rete' });
    this.world = s('g', { class: 'dm-world' });
    this.layers = {
      zones: s('g', { class: 'dm-zones' }),
      links: s('g', { class: 'dm-links' }),
      labels: s('g', { class: 'dm-labels' }),
      nodes: s('g', { class: 'dm-nodes' }),
      over: s('g', { class: 'dm-over' }),
    };
    this.world.append(this.layers.zones, this.layers.links, this.layers.labels, this.layers.nodes, this.layers.over);
    this.svg.append(this.world);
    host.append(this.svg);
  }

  // ------------------------------------------------------------------ vista

  get size() {
    const rect = this.host.getBoundingClientRect();
    return { w: rect.width, h: rect.height, left: rect.left, top: rect.top };
  }

  /** Da coordinate dello schermo (clientX/Y) a coordinate della mappa. */
  toWorld(clientX, clientY) {
    const { left, top } = this.size;
    return { x: (clientX - left - this.view.x) / this.view.k, y: (clientY - top - this.view.y) / this.view.k };
  }

  toScreen(x, y) {
    const { left, top } = this.size;
    return { x: left + this.view.x + x * this.view.k, y: top + this.view.y + y * this.view.k };
  }

  setView(x, y, k) {
    this.view = { x, y, k: Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, k)) };
    this.applyView();
  }

  applyView() {
    const { x, y, k } = this.view;
    attr(this.world, 'transform', `translate(${x.toFixed(2)} ${y.toFixed(2)}) scale(${k.toFixed(4)})`);
    // la griglia di puntini segue la mappa; da lontano i puntini si diradano
    const step = 16 * k * (k < 0.5 ? 4 : 1);
    this.host.style.backgroundSize = `${step}px ${step}px`;
    this.host.style.backgroundPosition = `${x}px ${y}px`;
    toggleClass(this.svg, 'lod-far', k < 0.5);
    toggleClass(this.svg, 'lod-tiny', k < 0.3);
    for (const listener of this.listeners) listener(this.view);
  }

  onView(listener) {
    this.listeners.add(listener);
  }

  /** Zoom attorno a un punto dello schermo. */
  zoomAt(clientX, clientY, factor) {
    const { left, top } = this.size;
    const k = Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, this.view.k * factor));
    const px = clientX - left;
    const py = clientY - top;
    const x = px - (px - this.view.x) * (k / this.view.k);
    const y = py - (py - this.view.y) * (k / this.view.k);
    this.setView(x, y, k);
  }

  /** La vista che va a (x, y, k) in 420 ms, come uno scroll guidato. */
  animateTo(x, y, k, ms = 420) {
    const from = { ...this.view };
    tween(this, 'view', 0, 1, ms, (t) => {
      // lo zoom si interpola in scala logaritmica: il movimento sembra uniforme
      const kt = Math.exp(Math.log(from.k) + (Math.log(k) - Math.log(from.k)) * t);
      this.setView(from.x + (x - from.x) * t, from.y + (y - from.y) * t, kt);
    }, null, Curves.quintOut);
  }

  /** Inquadra il riquadro dato (in coordinate della mappa), con un margine. */
  frameRect(rect, { animate = true, maxZoom = 1.15, pad = 56 } = {}) {
    const { w, h } = this.size;
    if (!rect || w < 10 || h < 10) return;
    const k = Math.min(maxZoom, Math.max(MIN_ZOOM, Math.min((w - pad * 2) / Math.max(rect.w, 1), (h - pad * 2) / Math.max(rect.h, 1))));
    const x = w / 2 - (rect.x + rect.w / 2) * k;
    const y = h / 2 - (rect.y + rect.h / 2) * k;
    if (animate) this.animateTo(x, y, k);
    else this.setView(x, y, k);
  }

  /** Porta un punto della mappa al centro, senza cambiare zoom. */
  centerOn(point, animate = true) {
    const { w, h } = this.size;
    const k = this.view.k;
    const x = w / 2 - point.x * k;
    const y = h / 2 - point.y * k;
    if (animate) this.animateTo(x, y, k);
    else this.setView(x, y, k);
  }

  /** Il centro dello schermo in coordinate della mappa. */
  middle() {
    const { w, h, left, top } = this.size;
    return this.toWorld(left + w / 2, top + h / 2);
  }

  // ------------------------------------------------------------------ disegno

  invalidate() {
    if (this.frame) return;
    this.frame = requestAnimationFrame(() => {
      this.frame = 0;
      this.render();
    });
  }

  render() {
    const map = this.store.map;
    const lit = this.litSet(map);
    this.renderZones(map, lit);
    this.renderLinks(map, lit);
    this.renderNodes(map, lit);
  }

  /** Quello che la ricerca o il filtro VLAN lasciano acceso; null = tutto. */
  litSet(map) {
    const q = this.filter.text.trim().toLowerCase();
    const vlan = this.filter.vlan;
    if (!q && vlan == null) return null;
    const lit = new Set();
    if (q) {
      for (const node of map.nodes) {
        const hay = `${node.name} ${node.host} ${node.mac} ${node.model} ${node.place} ${node.notes} ${KIND[node.kind]?.label ?? ''}`.toLowerCase();
        if (hay.includes(q)) lit.add(node.id);
      }
      for (const zone of map.zones) {
        if (`${zone.name} ${zone.cidr} ${zone.vlan}`.toLowerCase().includes(q)) lit.add(zone.id);
      }
      for (const link of map.links) {
        if (`${link.pa} ${link.pb} ${link.vlan} ${link.notes}`.toLowerCase().includes(q) || (lit.has(link.a) && lit.has(link.b))) lit.add(link.id);
      }
    }
    if (vlan != null) {
      const byVlan = new Set();
      for (const link of map.links) {
        if (carriesVlan(link.vlan, vlan)) {
          byVlan.add(link.id);
          byVlan.add(link.a);
          byVlan.add(link.b);
        }
      }
      for (const zone of map.zones) if (parseVlans(zone.vlan).ids.has(vlan)) byVlan.add(zone.id);
      if (q) {
        for (const id of [...lit]) if (!byVlan.has(id)) lit.delete(id);
      } else {
        for (const id of byVlan) lit.add(id);
      }
    }
    return lit;
  }

  renderZones(map, lit) {
    const keep = new Set();
    const layer = this.layers.zones;
    map.zones.forEach((zone, index) => {
      keep.add(zone.id);
      let g = this.els.zones.get(zone.id);
      if (!g) {
        g = this.zoneElement(zone);
        this.els.zones.set(zone.id, g);
      }
      if (layer.children[index] !== g) layer.insertBefore(g, layer.children[index] ?? null);
      this.updateZone(g, zone, lit);
    });
    this.prune(this.els.zones, keep);
  }

  zoneElement(zone) {
    const g = s('g', { class: 'dm-zone', 'data-type': 'zone', 'data-id': zone.id });
    g.innerHTML = `<rect class="dm-zone-box" rx="16"/><rect class="dm-zone-edge" rx="16"/>`
      + `<g class="dm-zone-head" data-part="head"><rect class="dm-zone-chip" x="12" y="10" height="24" rx="7"/>`
      + `<circle class="dm-zone-dot" cx="25" cy="22" r="3.5"/><text class="dm-zone-name" x="34" y="26"></text><text class="dm-zone-sub" y="26"></text></g>`
      + `<g class="dm-grips">${['nw', 'ne', 'se', 'sw'].map((c) => `<rect class="dm-grip" data-part="grip" data-corner="${c}" width="10" height="10" rx="3"/>`).join('')}</g>`;
    return g;
  }

  updateZone(g, zone, lit) {
    const sel = this.store.sel.has(zone.id);
    attr(g, 'transform', `translate(${zone.x} ${zone.y})`);
    attr(g, 'style', toneVars(zone.tone));
    toggleClass(g, 'selected', sel);
    toggleClass(g, 'dim', lit && !lit.has(zone.id));
    for (const rect of g.querySelectorAll('.dm-zone-box, .dm-zone-edge')) {
      attr(rect, 'width', zone.w);
      attr(rect, 'height', zone.h);
    }
    const head = zoneHead(zone);
    text(g.querySelector('.dm-zone-name'), head.name);
    const subEl = g.querySelector('.dm-zone-sub');
    text(subEl, head.sub);
    attr(subEl, 'x', 34 + head.nameWidth + 10);
    attr(g.querySelector('.dm-zone-chip'), 'width', head.width);
    const grips = g.querySelectorAll('.dm-grip');
    const at = { nw: [-5, -5], ne: [zone.w - 5, -5], se: [zone.w - 5, zone.h - 5], sw: [-5, zone.h - 5] };
    for (const grip of grips) {
      const [x, y] = at[grip.dataset.corner];
      attr(grip, 'x', x);
      attr(grip, 'y', y);
    }
  }

  renderLinks(map, lit) {
    const keep = new Set();
    // strade ed etichette si ricalcolano solo se la mappa e' cambiata (non per una selezione)
    const revision = `${this.store.revision}:${map.id}:${this.quick ? 'q' : ''}`;
    if (this.layout?.revision !== revision) this.layout = { revision, entries: linkLayout(map, this.quick) };
    for (const entry of this.layout.entries) {
      const { link } = entry;
      keep.add(link.id);
      let line = this.els.links.get(link.id);
      if (!line) {
        line = s('g', { class: 'dm-link', 'data-type': 'link', 'data-id': link.id });
        line.innerHTML = '<path class="dm-link-hit"/><path class="dm-link-line"/>';
        this.els.links.set(link.id, line);
        this.layers.links.append(line);
      }
      // le etichette in uno strato loro, sopra a tutti i cavi: nessuna linea le attraversa
      let tags = this.els.labels.get(link.id);
      if (!tags) {
        tags = s('g', { class: 'dm-link-labels', 'data-type': 'link', 'data-id': link.id });
        tags.innerHTML = '<g class="dm-chip dm-link-chip"><line class="dm-leader"/><rect height="18" rx="6"/><text y="12.5"></text></g>'
          + '<g class="dm-chip dm-port dm-port-a"><line class="dm-leader"/><rect height="16" rx="5"/><text y="11.5"></text></g>'
          + '<g class="dm-chip dm-port dm-port-b"><line class="dm-leader"/><rect height="16" rx="5"/><text y="11.5"></text></g>';
        this.els.labels.set(link.id, tags);
        this.layers.labels.append(tags);
      }
      this.updateLink(line, tags, entry, lit);
    }
    this.prune(this.els.links, keep);
    this.prune(this.els.labels, keep);
  }

  updateLink(line, tags, entry, lit) {
    const { link, a, b, path, spots } = entry;
    const sel = this.store.sel.has(link.id);
    const down = this.store.stateOf(a) === 'down' || this.store.stateOf(b) === 'down';
    const dim = lit && !lit.has(link.id);
    attr(line, 'class', `dm-link k-${link.kind}${sel ? ' selected' : ''}${down ? ' down' : ''}${dim ? ' dim' : ''}`);
    attr(tags, 'class', `dm-link-labels${sel ? ' selected' : ''}${dim ? ' dim' : ''}`);
    if (!path) {
      attr(line, 'display', 'none');
      attr(tags, 'display', 'none');
      return;
    }
    attr(line, 'display', null);
    attr(tags, 'display', null);
    const d = `M${path.p.x.toFixed(1)} ${path.p.y.toFixed(1)} L${path.q.x.toFixed(1)} ${path.q.y.toFixed(1)}`;
    attr(line.children[0], 'd', d);
    attr(line.children[1], 'd', d);
    attr(line.children[1], 'stroke-width', (strokeOf(link) + (sel ? 0.6 : 0)).toFixed(2));
    this.chip(tags.children[0], entry.mid, spots?.mid);
    this.chip(tags.children[1], entry.pa, spots?.a);
    this.chip(tags.children[2], entry.pb, spots?.b);
  }

  /**
   * Un'etichetta a pillola, centrata sul posto scelto; se e' dovuta stare lontana dal suo cavo,
   * un filo sottile ve la lega. Nascosta se non ha testo.
   */
  chip(g, label, spot) {
    if (!label || !spot) {
      attr(g, 'display', 'none');
      return;
    }
    attr(g, 'display', null);
    const width = Math.ceil(measure(label, CHIP_FONT)) + 12;
    const rect = g.querySelector('rect');
    const height = Number(rect.getAttribute('height'));
    const left = spot.at.x - width / 2;
    const top = spot.at.y - height / 2;
    attr(g, 'transform', `translate(${left.toFixed(1)} ${top.toFixed(1)})`);
    attr(rect, 'width', width);
    const leader = g.querySelector('.dm-leader');
    if (spot.lead) {
      attr(leader, 'display', null);
      attr(leader, 'x1', (spot.anchor.x - left).toFixed(1));
      attr(leader, 'y1', (spot.anchor.y - top).toFixed(1));
      attr(leader, 'x2', (width / 2).toFixed(1));
      attr(leader, 'y2', (height / 2).toFixed(1));
    } else {
      attr(leader, 'display', 'none');
    }
    const t = g.querySelector('text');
    attr(t, 'x', 6);
    text(t, label);
  }

  renderNodes(map, lit) {
    const keep = new Set();
    const layer = this.layers.nodes;
    map.nodes.forEach((node, index) => {
      keep.add(node.id);
      let g = this.els.nodes.get(node.id);
      if (!g) {
        g = this.nodeElement(node);
        this.els.nodes.set(node.id, g);
        if (this.pulse.has(node.id)) this.pop(g);
      }
      if (layer.children[index] !== g) layer.insertBefore(g, layer.children[index] ?? null);
      this.updateNode(g, node, lit);
    });
    this.pulse.clear();
    this.prune(this.els.nodes, keep);
  }

  nodeElement(node) {
    const g = s('g', { class: 'dm-node', 'data-type': 'node', 'data-id': node.id });
    g.innerHTML = `<g class="dm-node-body"><rect class="dm-node-ring" x="-4" y="-4" width="${NODE_W + 8}" height="${NODE_H + 8}" rx="15"/>`
      + `<rect class="dm-node-card" width="${NODE_W}" height="${NODE_H}" rx="12"/>`
      + `<rect class="dm-node-tile" x="8" y="8" width="36" height="36" rx="9"/>`
      + `<path class="dm-node-icon" transform="translate(14 14)"/>`
      + `<text class="dm-node-name" x="${TEXT_X}" y="23"></text><text class="dm-node-sub" x="${TEXT_X}" y="39"></text>`
      + `<circle class="dm-node-dot" cx="43" cy="9" r="4.5"/></g>`
      + `<g class="dm-handles">${[['t', NODE_W / 2, 0], ['r', NODE_W, NODE_H / 2], ['b', NODE_W / 2, NODE_H], ['l', 0, NODE_H / 2]]
        .map(([side, x, y]) => `<circle class="dm-handle" data-part="handle" data-side="${side}" cx="${x}" cy="${y}" r="5"/>`).join('')}</g>`;
    return g;
  }

  updateNode(g, node, lit) {
    const sel = this.store.sel.has(node.id);
    const state = this.store.stateOf(node);
    const kind = KIND[node.kind] ?? KIND.other;
    attr(g, 'transform', `translate(${node.x} ${node.y})`);
    attr(g, 'class', `dm-node st-${state}${sel ? ' selected' : ''}${lit && !lit.has(node.id) ? ' dim' : ''}${this.hover === node.id ? ' target' : ''}`);
    attr(g, 'style', toneVars(kind.tone));
    attr(g.querySelector('.dm-node-icon'), 'd', kindIcons[node.kind] ?? kindIcons.other);
    const nameEl = g.querySelector('.dm-node-name');
    text(nameEl, fit(node.name || kind.label, NAME_FONT, TEXT_MAX));
    const sub = subtitleOf(node);
    // un apparato chiamato come il suo tipo, senza indirizzo, ha una riga sola, centrata
    const single = !sub.mono && sub.text === (node.name || kind.label);
    attr(nameEl, 'y', single ? 31 : 23);
    const subEl = g.querySelector('.dm-node-sub');
    toggleClass(subEl, 'mono', sub.mono);
    text(subEl, single ? '' : fit(sub.text, sub.mono ? SUB_FONT : SUB_TEXT_FONT, TEXT_MAX));
  }

  /** Un apparato appena arrivato si posa con un piccolo rimbalzo. */
  pop(g) {
    const body = g.querySelector('.dm-node-body');
    tween(g, 'pop', 0.86, 1, 300, (v) => {
      body.setAttribute('transform', `translate(${NODE_W / 2} ${NODE_H / 2}) scale(${v}) translate(${-NODE_W / 2} ${-NODE_H / 2})`);
      g.style.opacity = String(Math.min(1, 0.35 + (v - 0.86) / 0.14));
    }, () => {
      body.removeAttribute('transform');
      g.style.opacity = '';
    }, Curves.backOut(0.35));
  }

  prune(els, keep) {
    for (const [id, el] of els) {
      if (!keep.has(id)) {
        el.remove();
        els.delete(id);
      }
    }
  }

  forget() {
    this.layout = null;
    for (const els of Object.values(this.els)) {
      for (const el of els.values()) el.remove();
      els.clear();
    }
    this.layers.over.replaceChildren();
  }

  // ------------------------------------------------------------------ sopra la mappa

  /** Il rettangolo della selezione col trascinamento (in coordinate della mappa). */
  marquee(rect) {
    let el = this.layers.over.querySelector('.dm-marquee');
    if (!rect) {
      el?.remove();
      return;
    }
    if (!el) {
      el = s('rect', { class: 'dm-marquee', rx: 4 });
      this.layers.over.append(el);
    }
    attr(el, 'x', rect.x);
    attr(el, 'y', rect.y);
    attr(el, 'width', rect.w);
    attr(el, 'height', rect.h);
    attr(el, 'stroke-width', (1 / this.view.k).toFixed(3));
  }

  /** Il cavo che si sta tirando: dal bordo dell'apparato fino al puntatore. */
  wire(from, to) {
    let el = this.layers.over.querySelector('.dm-wire');
    if (!from) {
      el?.remove();
      return;
    }
    if (!el) {
      el = s('path', { class: 'dm-wire' });
      this.layers.over.append(el);
    }
    attr(el, 'd', `M${from.x} ${from.y} L${to.x} ${to.y}`);
  }

  /** Le guide di allineamento mentre si trascina (linee verticali e orizzontali). */
  guides(lines) {
    const over = this.layers.over;
    for (const old of over.querySelectorAll('.dm-guide')) old.remove();
    for (const line of lines ?? []) {
      over.append(s('line', { class: 'dm-guide', x1: line.x1, y1: line.y1, x2: line.x2, y2: line.y2, 'stroke-width': (1 / this.view.k).toFixed(3) }));
    }
  }

  /** L'apparato sotto il puntatore mentre si tira un cavo. */
  setTarget(id) {
    if (this.hover === id) return;
    this.hover = id;
    this.invalidate();
  }

  elementOf(id) {
    return this.els.nodes.get(id) ?? this.els.links.get(id) ?? this.els.zones.get(id) ?? null;
  }
}
