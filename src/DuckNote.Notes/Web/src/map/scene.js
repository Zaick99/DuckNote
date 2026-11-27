// Il disegno della mappa in SVG: zone sotto, cavi in mezzo, apparati sopra. Le testate delle
// zone (nome, rete, VLAN) stanno sopra ai cavi, come le etichette: un cavo che entra nella zona
// ci passa sotto. Ogni oggetto del modello ha il suo elemento, creato una volta e poi solo
// aggiornato (render si chiama spesso: a ogni fotogramma di un trascinamento).

import { s, attr, text, toggleClass, fit, measure, shown } from '../common/dom.js';
import {
  KIND, MIN_ZOOM, MAX_ZOOM, PORTED, TONES, TAB_H, carriesVlan, parseVlans, sizeOf, panelOf, portCell, boundsOf,
} from './model.js';
import { kinds as kindIcons } from '../common/icons.js';
import { cableRoutes } from './geometry.js';
import { tween, Curves, still } from '../common/motion.js';
import { placeLabels } from './labels.js';

export const FONT = '-apple-system, BlinkMacSystemFont, "SF Pro Text", "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif';
export const MONO = '"SF Mono", ui-monospace, Menlo, "Cascadia Mono", Consolas, monospace';
export const NAME_FONT = `600 12.5px ${FONT}`;
export const SUB_FONT = `11px ${MONO}`;
export const SUB_TEXT_FONT = `11px ${FONT}`;
export const CHIP_FONT = `10.5px ${MONO}`;
export const ZONE_FONT = `600 12px ${FONT}`;
export const ZONE_SUB_FONT = `11px ${MONO}`;
export const TAB_FONT = `600 11.5px ${FONT}`;
export const BADGE_FONT = `700 9px ${FONT}`;
export const TITLE_FONT = `600 13px ${FONT}`;
export const IP_FONT = `11px ${MONO}`;
export const INFO_FONT = `10.5px ${FONT}`;

/** Dove comincia il testo nel corpo della scheda (dopo la tessera con l'icona). */
export const TEXT_X = 54;

/** Il distintivo dello stato nella linguetta: UP verde, DOWN rosso, ? arancio (da verificare). */
export const BADGES = { up: 'UP', down: 'DOWN', wait: '?' };

/** La linguetta di un apparato: il nome, il distintivo dello stato e fin dove arriva. */
export function tabOf(node, state) {
  const size = sizeOf(node);
  const badge = BADGES[state] ?? '';
  const badgeWidth = badge ? Math.ceil(measure(badge, BADGE_FONT)) + 8 : 0;
  const room = Math.max(40, size.w - 20 - 20 - badgeWidth);
  const name = fit(node.name || KIND[node.kind]?.label || 'Apparato', TAB_FONT, room);
  const nameWidth = Math.ceil(measure(name, TAB_FONT));
  return { name, badge, nameWidth, width: 10 + nameWidth + (badge ? badgeWidth : 0) + 10 };
}

/** Le righe del corpo della scheda: il modello (o il tipo), l'indirizzo, quello che si sa adesso. */
export function cardLines(node, host, state) {
  const size = sizeOf(node);
  const room = size.w - TEXT_X - 12;
  const kind = KIND[node.kind]?.label ?? 'Apparato';
  const title = fit(node.model || (PORTED.has(node.kind) && node.ports ? `${kind} · ${node.ports} porte` : kind), TITLE_FONT, room);
  const ip = node.host ? fit(node.host, IP_FONT, room) : '';
  const facts = [];
  if (state === 'up') facts.push(host?.rtt ? `risponde in ${host.rtt}` : 'risponde');
  else if (state === 'down') facts.push('non risponde');
  else if (state === 'wait') facts.push('da verificare');
  else if (state === 'unknown') facts.push('mai controllato');
  if (state === 'up' && host?.os) facts.push(host.os);
  if (node.place) facts.push(node.place);
  return { title, ip, info: fit(facts.join(' · ') || (node.host ? '' : 'senza indirizzo'), INFO_FONT, room) };
}

/**
 * La nuvola di Internet in un riquadro w x h: quattro gobbe sopra una base piatta. Le stesse
 * proporzioni a ogni misura, cosi' resta una nuvola anche nell'esportazione.
 */
export function cloudPath(w, h) {
  const x = (v) => (v * w).toFixed(1);
  const y = (v) => (v * h).toFixed(1);
  return `M${x(0.2)} ${y(0.88)} L${x(0.8)} ${y(0.88)}`
    + ` C${x(0.95)} ${y(0.88)} ${x(0.99)} ${y(0.62)} ${x(0.86)} ${y(0.5)}`
    + ` C${x(0.9)} ${y(0.25)} ${x(0.7)} ${y(0.1)} ${x(0.56)} ${y(0.2)}`
    + ` C${x(0.48)} ${y(0.02)} ${x(0.26)} ${y(0.06)} ${x(0.26)} ${y(0.3)}`
    + ` C${x(0.08)} ${y(0.28)} ${x(0.02)} ${y(0.5)} ${x(0.1)} ${y(0.64)}`
    + ` C${x(0.01)} ${y(0.76)} ${x(0.07)} ${y(0.88)} ${x(0.2)} ${y(0.88)} Z`;
}

/** Il colore di ogni VLAN: quello della zona che la porta, oppure uno fisso dal suo numero. */
export function vlanTones(map) {
  const tones = new Map();
  for (const zone of map.zones) {
    for (const id of parseVlans(zone.vlan).ids) if (!tones.has(id)) tones.set(id, zone.tone);
  }
  return tones;
}

const FREE_TONES = TONES.filter((tone) => tone !== 'Red' && tone !== 'Gray');

export const vlanTone = (tones, id) => tones.get(id) ?? FREE_TONES[id % FREE_TONES.length];

/**
 * Come si disegna un cavo: rosso tratteggiato se un capo e' giu', punteggiato per Internet e
 * VPN, tratteggiato viola per il Wi-Fi; con una VLAN sola (porta access) nel colore della VLAN,
 * con piu' VLAN (trunk) chiaro e piu' spesso; altrimenti verde se risponde, grigio se non si sa.
 * style: la classe; tone: il colore (null per quello di serie).
 */
export function wireOf(link, stateA, stateB, tones) {
  if (stateA === 'down' || stateB === 'down') return { style: 'down', tone: 'Red' };
  if (link.kind === 'vpn' || link.kind === 'wan') return { style: 'net', tone: 'Blue' };
  if (link.kind === 'wifi') return { style: 'wifi', tone: 'Purple' };
  const vlans = parseVlans(link.vlan);
  if (vlans.all || vlans.ids.size > 1) return { style: 'trunk', tone: null };
  if (vlans.ids.size === 1) return { style: 'access', tone: vlanTone(tones, [...vlans.ids][0]) };
  if (link.kind === 'fiber') return { style: 'fiber', tone: 'Teal' };
  if (stateA === 'up' || stateB === 'up') return { style: 'up', tone: 'Green' };
  return { style: 'plain', tone: null };
}

/** La seconda riga di un apparato nelle liste (libreria, inventario): l'indirizzo, oppure il tipo (e le porte). */
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
export function linkLayout(map, quick = false, stateOf = () => 'none') {
  const nodes = new Map(map.nodes.map((n) => [n.id, n]));
  const routes = cableRoutes(map, { quick, tabEnd: (node) => tabOf(node, stateOf(node)).width + 10 });
  const tones = vlanTones(map);
  const entries = [];
  for (const link of map.links) {
    const a = nodes.get(link.a);
    const b = nodes.get(link.b);
    if (!a || !b) continue;
    const route = routes.get(link.id) ?? null;
    const segs = route?.segs ?? [];
    const longest = segs.reduce((best, seg) => (!best || seg.length > best.length ? seg : best), null);
    // un cavo attaccato alla porta di uno switch la accende sul pannello: il nome li' non serve
    const onPanel = (end) => !!route?.ends[end].anchored;
    entries.push({
      link,
      a,
      b,
      route,
      points: route?.points ?? null,
      pathA: segs[0] ?? null,
      pathB: segs[segs.length - 1] ?? null,
      pathMid: longest,
      wire: wireOf(link, stateOf(a), stateOf(b), tones),
      pa: onPanel('a') ? '' : fit(link.pa, CHIP_FONT, 120),
      pb: onPanel('b') ? '' : fit(link.pb, CHIP_FONT, 120),
      mid: fit(linkLabel(link), CHIP_FONT, 170),
    });
  }
  const spots = placeLabels(map.nodes, entries, map.zones.map(zoneHeadRect), CHIP_FONT, { quick });
  for (const entry of entries) entry.spots = spots.get(entry.link.id);
  return entries;
}

/** Le porte usate di ogni apparato: numero -> { tone, title }, dal cavo che ci arriva. */
export function usedPorts(entries) {
  const used = new Map();
  for (const entry of entries) {
    for (const [end, node, other] of [['a', entry.a, entry.b], ['b', entry.b, entry.a]]) {
      const port = entry.route?.ends[end].port;
      if (!port) continue;
      if (!used.has(node.id)) used.set(node.id, new Map());
      const label = end === 'a' ? entry.link.pa : entry.link.pb;
      const vlan = entry.link.vlan ? ` · VLAN ${entry.link.vlan}` : '';
      used.get(node.id).set(port.number, { tone: entry.wire.tone, style: entry.wire.style, title: `${label} → ${other.name || 'apparato'}${entry.link.speed ? ` · ${entry.link.speed}` : ''}${vlan}` });
    }
  }
  return used;
}

const esc = (value) => String(value).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);

/** La linguetta: un rettangolo con gli angoli di sopra arrotondati, appoggiato sul corpo. */
export function tabPath(width) {
  const r = 6;
  const h = TAB_H + 1;
  return `M10 ${h} V${r} Q10 0 ${10 + r} 0 H${10 + width - r} Q${10 + width} 0 ${10 + width} ${r} V${h} Z`;
}

const PART_CLASS = {
  ring: 'dm-node-ring', tab: 'dm-node-tab', tabname: 'dm-node-tabname', badge: 'dm-node-badge', card: 'dm-node-card',
  accent: 'dm-node-accent', tile: 'dm-node-tile', icon: 'dm-node-icon', title: 'dm-node-title', ip: 'dm-node-ip',
  info: 'dm-node-info', cloud: 'dm-node-cloud', dot: 'dm-node-dot', bay: 'dm-jack-bay',
};

/**
 * La pittura dello schermo: ogni pezzo della scheda e' una classe, i colori li mette map.css
 * (secondo il tema, lo stato e la selezione). L'esportazione ne usa una sua, coi colori per esteso.
 */
export function screenPaint(part, info = {}) {
  if (part !== 'jack') return `class="${PART_CLASS[part]}"`;
  const used = info.used;
  const tone = used ? (used.tone ? `var(--c-${used.tone})` : 'var(--c-LabelSecondary)') : null;
  return `class="dm-jack${used ? ` used w-${used.style}` : ''}" data-port="${info.n}"${tone ? ` style="--port: ${tone}"` : ''}`;
}

/** Il disegno di una scheda (o della nuvola di Internet), in coordinate dell'apparato. */
export function cardMarkup(node, state, host, ports, paint = screenPaint) {
  const size = sizeOf(node);
  const icon = kindIcons[node.kind] ?? kindIcons.other;
  if (node.kind === 'internet') {
    const name = fit(node.name || 'Internet', TITLE_FONT, size.w - 56);
    const sub = node.host ? fit(node.host, IP_FONT, size.w - 64) : '';
    return `<path ${paint('ring')} d="${cloudPath(size.w, size.h)}" transform="translate(-4 -4) scale(${((size.w + 8) / size.w).toFixed(4)} ${((size.h + 8) / size.h).toFixed(4)})"/>`
      + `<path ${paint('cloud')} d="${cloudPath(size.w, size.h)}"/>`
      + `<path ${paint('icon')} transform="translate(${(size.w / 2 - 9).toFixed(1)} ${(size.h * 0.24).toFixed(1)}) scale(0.75)" d="${icon}"/>`
      + `<text ${paint('title')} x="${size.w / 2}" y="${(size.h * 0.6).toFixed(1)}" text-anchor="middle">${esc(name)}</text>`
      + (sub ? `<text ${paint('ip')} x="${size.w / 2}" y="${(size.h * 0.6 + 14).toFixed(1)}" text-anchor="middle">${esc(sub)}</text>` : '')
      + (BADGES[state] ? `<circle ${paint('dot')} cx="${(size.w * 0.8).toFixed(1)}" cy="${(size.h * 0.3).toFixed(1)}" r="4.5"/>` : '');
  }
  const tab = tabOf(node, state);
  const lines = cardLines(node, host, state);
  const top = TAB_H - 1;
  const parts = [
    `<rect ${paint('ring')} x="-4" y="${top - 4}" width="${size.w + 8}" height="${size.h - top + 8}" rx="13"/>`,
    `<path ${paint('tab')} d="${tabPath(tab.width)}"/>`,
    `<text ${paint('tabname')} x="20" y="14">${esc(tab.name)}</text>`,
    tab.badge ? `<text ${paint('badge')} x="${20 + tab.nameWidth + 6}" y="14">${esc(tab.badge)}</text>` : '',
    `<rect ${paint('card')} y="${top}" width="${size.w}" height="${size.h - top}" rx="10"/>`,
    `<path ${paint('accent')} d="M10 ${top + 0.5} H${size.w - 10}"/>`,
    `<rect ${paint('tile')} x="12" y="${top + 12}" width="32" height="32" rx="8"/>`,
    `<path ${paint('icon')} transform="translate(16 ${top + 16})" d="${icon}"/>`,
    `<text ${paint('title')} x="${TEXT_X}" y="${top + 23}">${esc(lines.title)}</text>`,
    lines.ip ? `<text ${paint('ip')} x="${TEXT_X}" y="${top + 39}">${esc(lines.ip)}</text>` : '',
    lines.info ? `<text ${paint('info')} x="${TEXT_X}" y="${top + (lines.ip ? 55 : 39)}">${esc(lines.info)}</text>` : '',
  ];
  const panel = panelOf(node);
  if (panel) {
    const first = portCell(node, 1);
    parts.push(`<rect ${paint('bay')} x="${first.x - 5}" y="${first.y - 5}" width="${panel.w + 10}" height="${panel.h + 10}" rx="5"/>`);
    for (let n = 1; n <= node.ports; n++) {
      const cell = portCell(node, n);
      const used = ports?.get(n);
      parts.push(`<rect ${paint('jack', { n, used })} x="${cell.x}" y="${cell.y}" width="${cell.w}" height="${cell.h}" rx="2"><title>${esc(used ? `Porta ${n}: ${used.title}` : `Porta ${n}: libera`)}</title></rect>`);
    }
  }
  return parts.join('');
}

/** I pallini da cui si tira un cavo, a meta' di ogni lato del corpo. */
export function handlesMarkup(node) {
  const size = sizeOf(node);
  const top = node.kind === 'internet' ? 0 : TAB_H - 1;
  const middle = (top + size.h) / 2;
  return [['t', size.w / 2, top], ['r', size.w, middle], ['b', size.w / 2, size.h], ['l', 0, middle]]
    .map(([side, x, y]) => `<circle class="dm-handle" data-part="handle" data-side="${side}" cx="${x}" cy="${y}" r="5"/>`).join('');
}

const toneVars = (tone) => `--tone: var(--c-${tone}); --tone-soft: var(--t-${tone}-soft); --tone-border: var(--t-${tone}-border); --tone-zone: var(--t-${tone}-zone)`;

export class Scene {
  constructor(host, store) {
    this.host = host;
    this.store = store;
    this.view = { x: 0, y: 0, k: 1 };
    this.filter = { text: '', vlan: null };
    this.els = { nodes: new Map(), links: new Map(), labels: new Map(), zones: new Map(), heads: new Map() };
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
      heads: s('g', { class: 'dm-heads' }),
      labels: s('g', { class: 'dm-labels' }),
      nodes: s('g', { class: 'dm-nodes' }),
      over: s('g', { class: 'dm-over' }),
    };
    this.world.append(this.layers.zones, this.layers.links, this.layers.heads, this.layers.labels, this.layers.nodes, this.layers.over);
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
  /** left: pixel a sinistra occupati da altro (i riquadri della vista): la mappa sta nel resto. */
  frameRect(rect, { animate = true, maxZoom = 1.15, pad = 56, left = 0 } = {}) {
    const { w, h } = this.size;
    if (!rect || w < 10 || h < 10) return;
    const room = w - left > 360 ? left : 0;
    const k = Math.min(maxZoom, Math.max(MIN_ZOOM, Math.min((w - room - pad * 2) / Math.max(rect.w, 1), (h - pad * 2) / Math.max(rect.h, 1))));
    const x = room + (w - room) / 2 - (rect.x + rect.w / 2) * k;
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
        const top = this.zoneHeadElement(zone);
        this.els.heads.set(zone.id, top);
        this.layers.heads.append(top);
      }
      if (layer.children[index] !== g) layer.insertBefore(g, layer.children[index] ?? null);
      const top = this.els.heads.get(zone.id);
      if (this.layers.heads.children[index] !== top) this.layers.heads.insertBefore(top, this.layers.heads.children[index] ?? null);
      this.updateZone(g, zone, lit);
    });
    this.prune(this.els.zones, keep);
    this.prune(this.els.heads, keep);
  }

  zoneElement(zone) {
    const g = s('g', { class: 'dm-zone', 'data-type': 'zone', 'data-id': zone.id });
    g.innerHTML = `<rect class="dm-zone-box" rx="16"/><rect class="dm-zone-edge" rx="16"/>`
      + `<g class="dm-grips">${['nw', 'ne', 'se', 'sw'].map((c) => `<rect class="dm-grip" data-part="grip" data-corner="${c}" width="10" height="10" rx="3"/>`).join('')}</g>`;
    return g;
  }

  /** La testata della zona, nello strato sopra ai cavi: stesso tipo e stesso id della zona. */
  zoneHeadElement(zone) {
    const g = s('g', { class: 'dm-zone dm-zone-top', 'data-type': 'zone', 'data-id': zone.id });
    g.innerHTML = `<g class="dm-zone-head" data-part="head"><rect class="dm-zone-chip" x="12" y="10" height="24" rx="7"/>`
      + `<circle class="dm-zone-dot" cx="25" cy="22" r="3.5"/><text class="dm-zone-name" x="34" y="26"></text><text class="dm-zone-sub" y="26"></text></g>`;
    return g;
  }

  updateZone(g, zone, lit) {
    const sel = this.store.sel.has(zone.id);
    const top = this.els.heads.get(zone.id);
    for (const el of [g, top]) {
      if (!el) continue;
      attr(el, 'transform', `translate(${zone.x} ${zone.y})`);
      attr(el, 'style', toneVars(zone.tone));
      toggleClass(el, 'selected', sel);
      toggleClass(el, 'dim', lit && !lit.has(zone.id));
    }
    for (const rect of g.querySelectorAll('.dm-zone-box, .dm-zone-edge')) {
      attr(rect, 'width', zone.w);
      attr(rect, 'height', zone.h);
    }
    const head = zoneHead(zone);
    text(top.querySelector('.dm-zone-name'), head.name);
    const subEl = top.querySelector('.dm-zone-sub');
    text(subEl, head.sub);
    attr(subEl, 'x', 34 + head.nameWidth + 10);
    attr(top.querySelector('.dm-zone-chip'), 'width', head.width);
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
    const revision = `${this.store.revision}:${this.store.hostsRevision ?? 0}:${map.id}:${this.quick ? 'q' : ''}`;
    if (this.layout?.revision !== revision) {
      const entries = linkLayout(map, this.quick, (node) => this.store.stateOf(node));
      this.layout = { revision, entries, ports: usedPorts(entries) };
    }
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
        tags.innerHTML = '<g class="dm-tag dm-link-chip"><line class="dm-leader"/><rect height="18" rx="6"/><text y="12.5"></text></g>'
          + '<g class="dm-tag dm-port dm-port-a"><line class="dm-leader"/><rect height="16" rx="5"/><text y="11.5"></text></g>'
          + '<g class="dm-tag dm-port dm-port-b"><line class="dm-leader"/><rect height="16" rx="5"/><text y="11.5"></text></g>';
        this.els.labels.set(link.id, tags);
        this.layers.labels.append(tags);
      }
      this.updateLink(line, tags, entry, lit);
    }
    this.prune(this.els.links, keep);
    this.prune(this.els.labels, keep);
  }

  updateLink(line, tags, entry, lit) {
    const { link, points, spots, wire } = entry;
    const sel = this.store.sel.has(link.id);
    const dim = lit && !lit.has(link.id);
    attr(line, 'class', `dm-link k-${link.kind} w-${wire.style}${sel ? ' selected' : ''}${dim ? ' dim' : ''}`);
    attr(line, 'style', wire.tone ? `--wire: var(--c-${wire.tone})` : null);
    attr(tags, 'class', `dm-link-labels${sel ? ' selected' : ''}${dim ? ' dim' : ''}`);
    if (!points) {
      shown(line, false);
      shown(tags, false);
      return;
    }
    shown(line, true);
    shown(tags, true);
    const d = points.map((point, index) => `${index ? 'L' : 'M'}${point.x.toFixed(1)} ${point.y.toFixed(1)}`).join(' ');
    attr(line.children[0], 'd', d);
    attr(line.children[1], 'd', d);
    attr(line.children[1], 'stroke-width', (strokeOf(link) + (wire.style === 'trunk' ? 0.6 : 0) + (sel ? 0.6 : 0)).toFixed(2));
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
      shown(g, false);
      return;
    }
    shown(g, true);
    const width = Math.ceil(measure(label, CHIP_FONT)) + 12;
    const rect = g.querySelector('rect');
    const height = Number(rect.getAttribute('height'));
    const left = spot.at.x - width / 2;
    const top = spot.at.y - height / 2;
    attr(g, 'transform', `translate(${left.toFixed(1)} ${top.toFixed(1)})`);
    attr(rect, 'width', width);
    const leader = g.querySelector('.dm-leader');
    if (spot.lead) {
      shown(leader, true);
      attr(leader, 'x1', (spot.anchor.x - left).toFixed(1));
      attr(leader, 'y1', (spot.anchor.y - top).toFixed(1));
      attr(leader, 'x2', (width / 2).toFixed(1));
      attr(leader, 'y2', (height / 2).toFixed(1));
    } else {
      shown(leader, false);
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
        if (this.pulse.has(node.id)) this.pop(g, node);
      }
      if (layer.children[index] !== g) layer.insertBefore(g, layer.children[index] ?? null);
      this.updateNode(g, node, lit);
    });
    this.pulse.clear();
    this.prune(this.els.nodes, keep);
  }

  nodeElement(node) {
    const g = s('g', { class: 'dm-node', 'data-type': 'node', 'data-id': node.id });
    g.innerHTML = '<g class="dm-node-body"></g><g class="dm-handles"></g>';
    return g;
  }

  /**
   * La scheda di un apparato: si rifa' il suo disegno solo se cambia quello che mostra (nome,
   * stato, righe, porte, misura); selezione, ricerca e stato restano classi sul gruppo.
   */
  updateNode(g, node, lit) {
    const sel = this.store.sel.has(node.id);
    const state = this.store.stateOf(node);
    const kind = KIND[node.kind] ?? KIND.other;
    attr(g, 'transform', `translate(${node.x} ${node.y})`);
    attr(g, 'class', `dm-node k-${node.kind} st-${state}${sel ? ' selected' : ''}${lit && !lit.has(node.id) ? ' dim' : ''}${this.hover === node.id ? ' target' : ''}`);
    attr(g, 'style', toneVars(kind.tone));
    const ports = this.layout?.ports?.get(node.id) ?? null;
    const host = this.store.hostOf(node);
    const sig = JSON.stringify([node.kind, node.name, node.model, node.host, node.place, node.ports, state, host?.rtt ?? '', host?.os ?? '',
      ports ? [...ports].map(([n, p]) => `${n}:${p.tone}:${p.style}:${p.title}`) : null]);
    if (g.dataset.sig === sig) return;
    g.dataset.sig = sig;
    g.firstChild.innerHTML = cardMarkup(node, state, host, ports);
    g.lastChild.innerHTML = handlesMarkup(node);
  }

  /** Un apparato appena arrivato si posa con un piccolo rimbalzo. */
  pop(g, node) {
    const body = g.querySelector('.dm-node-body');
    const size = sizeOf(node ?? this.store.node(g.dataset.id));
    tween(g, 'pop', 0.86, 1, 300, (v) => {
      body.setAttribute('transform', `translate(${size.w / 2} ${size.h / 2}) scale(${v}) translate(${-size.w / 2} ${-size.h / 2})`);
      g.style.opacity = String(Math.min(1, 0.35 + (v - 0.86) / 0.14));
    }, () => {
      body.removeAttribute('transform');
      g.style.opacity = '';
    }, Curves.backOut(0.35));
  }

  /**
   * La mappa che arriva (aperta, o un'altra mappa della nota): le zone affiorano, gli apparati
   * scendono in onda dall'alto in basso (e un poco da sinistra a destra), i cavi e le loro
   * etichette arrivano dietro ai loro due apparati. Poco piu' di mezzo secondo in tutto; con
   * "riduci movimento" la mappa c'e' e basta.
   */
  enter() {
    this.settle();
    const map = this.store.map;
    const box = boundsOf(map);
    if (still() || !box) return;
    this.render();
    const items = [];
    const delays = new Map();
    const fade = (el, delay, ms) => {
      if (el) items.push({ el, delay, ms, node: null });
    };
    for (const zone of map.zones) {
      fade(this.els.zones.get(zone.id), 0, 260);
      fade(this.els.heads.get(zone.id), 60, 260);
    }
    for (const node of map.nodes) {
      const g = this.els.nodes.get(node.id);
      if (!g) continue;
      const delay = 40 + ((node.y - box.y) / Math.max(1, box.h)) * 300 + ((node.x - box.x) / Math.max(1, box.w)) * 90;
      delays.set(node.id, delay);
      items.push({ el: g.firstChild, delay, ms: 380, node, size: sizeOf(node) });
    }
    for (const link of map.links) {
      const delay = Math.max(delays.get(link.a) ?? 0, delays.get(link.b) ?? 0) + 140;
      fade(this.els.links.get(link.id), delay, 300);
      fade(this.els.labels.get(link.id), delay + 90, 260);
    }
    const bounce = Curves.backOut(0.5);
    const paint = (item, t) => {
      const shown = Curves.cubicOut(t);
      item.el.style.opacity = String(shown);
      if (!item.node) return;
      const { w, h } = item.size;
      const scale = 0.92 + 0.08 * bounce(t);
      const drop = (1 - shown) * -12;
      item.el.setAttribute('transform', `translate(${w / 2} ${h / 2 + drop}) scale(${scale.toFixed(4)}) translate(${-w / 2} ${-h / 2})`);
    };
    for (const item of items) paint(item, 0);
    const start = performance.now();
    const tick = (now) => {
      let busy = false;
      for (const item of items) {
        const t = Math.min(1, Math.max(0, (now - start - item.delay) / item.ms));
        if (t < 1) busy = true;
        paint(item, t);
      }
      if (busy) this.entering = { frame: requestAnimationFrame(tick), items };
      else this.settle();
    };
    this.entering = { frame: requestAnimationFrame(tick), items };
  }

  /** L'onda dell'apertura finisce subito: tutto al suo posto. */
  settle() {
    if (!this.entering) return;
    cancelAnimationFrame(this.entering.frame);
    for (const item of this.entering.items) {
      item.el.style.opacity = '';
      if (item.node) item.el.removeAttribute('transform');
    }
    this.entering = null;
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
    this.settle();
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
