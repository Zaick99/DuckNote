// I riquadri della vista, in alto a destra sulla mappa come in Vantage: le reti (le zone con la
// loro VLAN e la sottorete, piu' le VLAN dei cavi che non hanno una zona) e la legenda, solo con
// quello che la mappa usa davvero: le velocita', i tratti, le porte access e trunk, i trattini
// delle VLAN sulle porte. Un clic su una rete accende solo quella (il filtro VLAN), un altro clic
// la spegne; una zona senza VLAN si inquadra. Ogni riquadro si chiude sulla sua testata.

import { h, fill, icon } from '../common/dom.js';
import { ui as uiIcons } from '../common/icons.js';
import { parseVlans, PORTED } from './model.js';
import { vlanTones, vlanTone, wireOf, strokeOf, toneVar, DASH } from './scene.js';
import { VPNS, vpnColor, logoSvg } from './vpn.js';

/** Le velocita' nell'ordine della legenda. */
const SPEED_ORDER = ['10M', '100M', '1G', '2.5G', '5G', '10G', '25G', '40G', '100G'];
/** I tratti: pieno (cavo), punteggiato (via Internet), tratteggiato (Wi-Fi). */
const STYLE_NAME = { solid: 'Cavo', dotted: 'Via Internet (VPN, WAN)', dashed: 'Wi-Fi' };

function remembered() {
  try {
    return JSON.parse(localStorage.getItem('dm-overview') ?? '{}') ?? {};
  } catch {
    return {};
  }
}

/** Le reti della mappa: una riga per zona con rete o VLAN, poi le VLAN dei cavi rimaste fuori. */
export function networksOf(map) {
  const tones = vlanTones(map);
  const rows = [];
  const covered = new Set();
  for (const zone of map.zones) {
    const ids = [...parseVlans(zone.vlan).ids];
    if (!zone.cidr && ids.length === 0) continue;
    for (const id of ids) covered.add(id);
    rows.push({ key: zone.id, vlan: ids[0] ?? null, vlanText: zone.vlan, name: zone.name || 'Zona', cidr: zone.cidr, tone: zone.tone, zone });
  }
  for (const link of map.links) {
    for (const id of parseVlans(link.vlan).ids) {
      if (covered.has(id)) continue;
      covered.add(id);
      rows.push({ key: `vlan${id}`, vlan: id, vlanText: String(id), name: '', cidr: '', tone: vlanTone(tones, id), zone: null });
    }
  }
  return rows.sort((a, b) => (a.vlan ?? 1e9) - (b.vlan ?? 1e9) || a.name.localeCompare(b.name, 'it', { numeric: true }));
}

/** Quello che la legenda deve spiegare: velocita', tratti, porte access e trunk, trattini delle porte. */
export function legendOf(map, stateOf) {
  const nodes = new Map(map.nodes.map((node) => [node.id, node]));
  const tones = vlanTones(map);
  const speeds = SPEED_ORDER.filter((speed) => map.links.some((link) => link.speed === speed));
  const styles = new Set();
  const access = [];
  const vpns = new Set();
  let trunk = false;
  let portVlans = false;
  for (const link of map.links) {
    const a = nodes.get(link.a);
    const b = nodes.get(link.b);
    if (!a || !b) continue;
    const wire = wireOf(link, stateOf(a), stateOf(b), tones);
    if (wire.brand) vpns.add(wire.brand.id);
    else styles.add(wire.style);
    if (wire.access && !access.includes(wire.tone)) access.push(wire.tone);
    if (wire.trunk) trunk = true;
    if (link.vlan && (PORTED.has(a.kind) || PORTED.has(b.kind)) && (link.pa || link.pb)) portVlans = true;
  }
  // il tratto pieno merita una riga solo se ce n'e' un altro da distinguere
  const used = ['solid', 'dotted', 'dashed'].filter((style) => styles.has(style));
  return {
    speeds,
    styles: used.length > 1 || (used.length && vpns.size) ? used : used.filter((style) => style !== 'solid'),
    access,
    trunk,
    portVlans,
    vpns: VPNS.filter((vpn) => vpns.has(vpn.id)),
  };
}

export class Overview {
  /** actions: { vlan(): numero acceso o null, pickVlan(id), frameZone(zone), stateOf(node) } */
  constructor(store, actions) {
    this.store = store;
    this.actions = actions;
    this.closed = remembered();
    this.nets = h('section', { class: 'dm-over-card dm-nets' });
    this.legend = h('section', { class: 'dm-over-card dm-legend' });
    this.el = h('div', { class: 'dm-overview', 'aria-label': 'Reti e legenda' }, this.nets, this.legend);
  }

  toggle(which) {
    this.closed = { ...this.closed, [which]: !this.closed[which] };
    try {
      localStorage.setItem('dm-overview', JSON.stringify(this.closed));
    } catch { /* senza memoria del browser: si riapre aperto */ }
    this.render();
  }

  head(which, title) {
    const open = !this.closed[which];
    return h('button', {
      class: 'dm-over-head',
      type: 'button',
      'aria-expanded': String(open),
      title: open ? 'Chiudi il riquadro' : 'Apri il riquadro',
      onclick: () => this.toggle(which),
    }, h('span', { class: 'dm-over-title' }, title), h('span', { class: 'dm-spacer' }), icon(open ? uiIcons.down : uiIcons.left, 12));
  }

  render() {
    const map = this.store.map;
    const rows = networksOf(map);
    const lit = this.actions.vlan();

    this.nets.hidden = rows.length === 0;
    if (rows.length) {
      fill(this.nets, this.head('nets', 'Reti'), this.closed.nets ? null : h('div', { class: 'dm-net-list' }, ...rows.map((row) => {
        const on = row.vlan != null && lit === row.vlan;
        return h('button', {
          class: `dm-net${on ? ' on' : ''}${lit != null && !on ? ' off' : ''}`,
          type: 'button',
          style: { '--net': toneVar(row.tone) },
          title: row.vlan != null ? (on ? 'Mostra tutte le reti' : `Mostra solo la VLAN ${row.vlan}`) : 'Inquadra la zona',
          onclick: () => {
            if (row.vlan != null) this.actions.pickVlan(on ? null : row.vlan);
            else if (row.zone) this.actions.frameZone(row.zone);
          },
        },
        h('span', { class: 'dm-net-stripe' }),
        h('span', { class: 'dm-net-text' },
          h('span', { class: 'dm-net-vlan' }, row.vlanText ? `VLAN ${row.vlanText}` : 'Rete'),
          h('span', { class: 'dm-net-name' }, row.name || h('span', { class: 'dm-net-none' }, 'senza zona')),
          row.cidr ? h('span', { class: 'dm-net-cidr' }, row.cidr) : null));
      })));
    }

    const entries = legendOf(map, (node) => this.actions.stateOf(node));
    const items = [
      ...entries.speeds.map((speed) => ({ key: `speed-${speed}`, sample: line({ width: strokeOf({ speed }) }), label: speed })),
      ...entries.styles.map((style) => ({ key: `style-${style}`, sample: line({ width: 1.5, dash: DASH[style], round: style === 'dotted' }), label: STYLE_NAME[style] })),
      ...(entries.access.length ? [{ key: 'access', sample: line({ width: 2.5, colors: entries.access.slice(0, 3).map(toneVar) }), label: 'Porta access, nel colore della sua VLAN' }] : []),
      ...(entries.trunk ? [{ key: 'trunk', sample: line({ width: 1.5, trunk: true }), label: 'Trunk (VLAN taggate)' }] : []),
      ...(entries.portVlans ? [{ key: 'port-vlans', sample: bars(), label: 'VLAN delle porte · alta = non taggata' }] : []),
      ...entries.vpns.map((vpn) => ({ key: `vpn-${vpn.id}`, sample: brandLine(vpn), label: vpn.label })),
    ];
    this.legend.hidden = items.length === 0;
    if (items.length) {
      fill(this.legend, this.head('legend', 'Legenda'), this.closed.legend ? null : h('div', { class: 'dm-legend-list' }, ...items.map((item) => h('div', { class: 'dm-legend-row', 'data-legend': item.key },
        item.sample, h('span', { class: 'dm-legend-label' }, item.label)))));
    }
    this.el.hidden = this.nets.hidden && this.legend.hidden;
  }
}

function svg(inner) {
  const el = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  el.setAttribute('class', 'dm-legend-sample');
  el.setAttribute('width', '44');
  el.setAttribute('height', '10');
  el.setAttribute('viewBox', '0 0 44 10');
  el.innerHTML = inner;
  return el;
}

/** Il campione di un cavo: lo spessore, il tratto, i colori delle VLAN o la doppia linea del trunk. */
function line({ width, dash = null, round = false, colors = null, trunk = false }) {
  const dashText = dash ? ` stroke-dasharray="${dash}"` : '';
  if (colors) {
    const step = 40 / colors.length;
    return svg(colors.map((color, index) => `<path d="M${(2 + index * step).toFixed(1)} 5 H${(2 + (index + 1) * step - 2).toFixed(1)}" stroke="${color}" stroke-width="${width}" stroke-linecap="round"/>`).join(''));
  }
  return svg((trunk ? `<path d="M2 5 H42" stroke="var(--vt-muted)" stroke-opacity="0.5" stroke-width="${width + 3}"/>` : '')
    + `<path d="M2 5 H42" stroke="${trunk ? 'var(--vt-ink)' : 'var(--vt-muted)'}" stroke-width="${width}" stroke-linecap="${round ? 'round' : 'butt'}"${dashText}/>`);
}

/** Il campione di un collegamento VPN: i pallini nel colore del servizio e il suo logo in mezzo. */
function brandLine(vpn) {
  const el = svg(`<path d="M2 5 H42" stroke="${vpnColor(vpn.id)}" stroke-width="3" stroke-linecap="round" stroke-dasharray="${DASH.brand}"/>`);
  const wrap = document.createElement('span');
  wrap.className = 'dm-legend-vpn';
  wrap.append(el, logoSvg(vpn, 14));
  return wrap;
}

/** I trattini delle VLAN sulle porte: uno alto (non taggata), due bassi (taggate). */
function bars() {
  return svg('<rect x="14" y="3" width="5" height="7" rx="1" fill="var(--vt-in)"/><rect x="21" y="6" width="5" height="4" rx="1" fill="var(--vt-out)"/><rect x="28" y="6" width="5" height="4" rx="1" fill="var(--vt-muted)"/>');
}
