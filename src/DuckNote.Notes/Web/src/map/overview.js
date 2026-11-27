// I riquadri della vista, in alto a sinistra sulla mappa: le reti (le zone con la loro VLAN e la
// sottorete, piu' le VLAN dei cavi che non hanno una zona) e la legenda dei cavi, solo con gli
// stili che la mappa usa davvero. Un clic su una rete accende solo quella (il filtro VLAN), un
// altro clic la spegne; una zona senza VLAN si inquadra. Ogni riquadro si chiude sulla sua
// testata e se lo ricorda.

import { h, fill, icon } from '../common/dom.js';
import { ui as uiIcons } from '../common/icons.js';
import { parseVlans } from './model.js';
import { vlanTones, vlanTone, wireOf } from './scene.js';

/** Gli stili dei cavi, nell'ordine della legenda. */
const LEGEND = [
  { style: 'up', label: 'Attivo', sub: 'risponde' },
  { style: 'down', label: 'Spento', sub: 'un capo non risponde' },
  { style: 'access', label: 'Una VLAN', sub: 'nel colore della sua rete' },
  { style: 'trunk', label: 'Più VLAN', sub: 'trunk' },
  { style: 'net', label: 'Internet o VPN' },
  { style: 'wifi', label: 'Wi-Fi' },
  { style: 'fiber', label: 'Fibra' },
  { style: 'plain', label: 'Non controllato' },
];

const SAMPLE_W = 34;

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

/** Gli stili dei cavi usati nella mappa, e i colori delle porte access (per il campione). */
export function stylesOf(map, stateOf) {
  const nodes = new Map(map.nodes.map((node) => [node.id, node]));
  const tones = vlanTones(map);
  const used = new Set();
  const access = [];
  for (const link of map.links) {
    const a = nodes.get(link.a);
    const b = nodes.get(link.b);
    if (!a || !b) continue;
    const wire = wireOf(link, stateOf(a), stateOf(b), tones);
    used.add(wire.style);
    if (wire.style === 'access' && !access.includes(wire.tone)) access.push(wire.tone);
  }
  return { used, access };
}

export class Overview {
  /** actions: { vlan(): numero acceso o null, pickVlan(id), frameZone(zone) } */
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

  head(which, title, count) {
    const open = !this.closed[which];
    return h('button', {
      class: 'dm-over-head',
      type: 'button',
      'aria-expanded': String(open),
      title: open ? 'Chiudi il riquadro' : 'Apri il riquadro',
      onclick: () => this.toggle(which),
    }, h('span', { class: 'dm-over-title' }, title), count != null ? h('span', { class: 'dm-over-count' }, String(count)) : null,
    h('span', { class: 'dm-spacer' }), icon(open ? uiIcons.down : uiIcons.right, 12));
  }

  render() {
    const map = this.store.map;
    const rows = networksOf(map);
    const { used, access } = stylesOf(map, (node) => this.store.stateOf(node));
    const lit = this.actions.vlan();

    this.nets.hidden = rows.length === 0;
    if (rows.length) {
      fill(this.nets, this.head('nets', 'Reti', rows.length), this.closed.nets ? null : h('div', { class: 'dm-net-list' }, ...rows.map((row) => {
        const on = row.vlan != null && lit === row.vlan;
        return h('button', {
          class: `dm-net${on ? ' on' : ''}${lit != null && !on ? ' off' : ''}`,
          type: 'button',
          style: { '--tone': `var(--c-${row.tone})`, '--tone-soft': `var(--t-${row.tone}-soft)` },
          title: row.vlan != null ? (on ? 'Mostra tutte le reti' : `Mostra solo la VLAN ${row.vlan}`) : 'Inquadra la zona',
          onclick: () => {
            if (row.vlan != null) this.actions.pickVlan(on ? null : row.vlan);
            else if (row.zone) this.actions.frameZone(row.zone);
          },
        },
        h('span', { class: 'dm-net-stripe' }),
        h('span', { class: 'dm-net-vlan' }, row.vlanText ? `VLAN ${row.vlanText}` : '—'),
        h('span', { class: 'dm-net-text' },
          h('span', { class: 'dm-net-name' }, row.name || h('span', { class: 'dm-net-none' }, 'senza zona')),
          row.cidr ? h('span', { class: 'dm-net-cidr' }, row.cidr) : null));
      })));
    }

    const items = LEGEND.filter((item) => used.has(item.style));
    this.legend.hidden = items.length === 0;
    if (items.length) {
      fill(this.legend, this.head('legend', 'Legenda'), this.closed.legend ? null : h('div', { class: 'dm-legend-list' }, ...items.map((item) => h('div', { class: 'dm-legend-row' },
        sample(item.style, access),
        h('span', { class: 'dm-legend-label' }, item.label, item.sub ? h('span', { class: 'dm-legend-sub' }, item.sub) : null)))));
    }
    this.el.hidden = this.nets.hidden && this.legend.hidden;
  }
}

/** Il campione di un cavo: un trattino con lo stile della mappa (per le VLAN, i loro colori). */
function sample(style, access) {
  const svg = (inner) => {
    const el = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    el.setAttribute('class', `dm-legend-sample w-${style}`);
    el.setAttribute('width', String(SAMPLE_W));
    el.setAttribute('height', '10');
    el.setAttribute('viewBox', `0 0 ${SAMPLE_W} 10`);
    el.innerHTML = inner;
    return el;
  };
  if (style === 'access') {
    const tones = access.slice(0, 3);
    const step = SAMPLE_W / tones.length;
    return svg(tones.map((tone, index) => `<path d="M${(index * step + 2).toFixed(1)} 5 H${((index + 1) * step - 2).toFixed(1)}" style="stroke: var(--c-${tone})"/>`).join(''));
  }
  return svg(`<path d="M2 5 H${SAMPLE_W - 2}"/>`);
}
