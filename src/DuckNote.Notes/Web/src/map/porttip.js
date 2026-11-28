// Il riquadro di una porta col mouse sopra, come in Vantage: il numero e il nome della porta, lo
// stato del collegamento con la velocita', chi c'e' dall'altra parte del cavo e le VLAN (la non
// taggata e le taggate, col nome della zona che le porta). Sta sotto la presa, allineato a destra
// se la presa e' nella meta' destra del pannello; sparisce appena si preme, si trascina o si zooma.

import { h, fill } from '../common/dom.js';
import { parseVlans } from './model.js';

/** Il nome di ogni VLAN: quello della zona che la porta. */
function vlanNames(map) {
  const names = new Map();
  for (const zone of map.zones) {
    for (const id of parseVlans(zone.vlan).ids) if (!names.has(id) && zone.name) names.set(id, zone.name);
  }
  return names;
}

export class PortTip {
  constructor(scene, store) {
    this.scene = scene;
    this.store = store;
    this.el = h('div', { class: 'dm-port-tip', role: 'tooltip', hidden: true });
    scene.host.append(this.el);
    this.jack = null;
    const svg = scene.svg;
    svg.addEventListener('pointerover', (event) => this.over(event));
    svg.addEventListener('pointerout', (event) => {
      if (this.jack && !this.jack.contains(event.relatedTarget)) this.hide();
    });
    svg.addEventListener('pointerdown', () => this.hide(), true);
    svg.addEventListener('wheel', () => this.hide(), { passive: true });
  }

  over(event) {
    if (event.pointerType === 'touch') return;
    const jack = event.target.closest?.('.dm-jack');
    if (!jack || jack === this.jack) return;
    if (['dragging', 'panning', 'plugging'].some((name) => this.scene.host.classList.contains(name))) return;
    const node = this.store.find(jack.closest('[data-id]')?.dataset.id);
    if (!node) return;
    this.jack = jack;
    this.show(jack, node);
  }

  show(jack, node) {
    const kind = jack.dataset.kind;
    const n = Number(jack.dataset.port);
    const used = this.scene.layout?.ports?.get(node.id)?.get(`${kind}:${n}`) ?? null;
    const name = `${kind === 'cage' ? 'SFP' : 'Porta'} ${n}`;
    const lines = [];
    const state = used?.link === 'up' ? h('span', { class: 'ok' }, ` · UP${used.speed ? ` · ${used.speed}` : ''}`)
      : used?.link === 'down' ? h('span', { class: 'crit' }, ' · DOWN')
        : used?.speed ? h('span', { class: 'muted' }, ` · ${used.speed}`) : null;
    lines.push(h('span', { class: 'dm-tip-head' }, name, used?.label ? h('span', { class: 'dm-tip-port' }, ` · ${used.label}`) : null, state));
    if (used) {
      lines.push(h('span', { class: 'dm-tip-line' }, 'Collegata a ', h('b', {}, used.other || 'un apparato'), used.otherPort ? h('span', { class: 'dm-tip-port' }, ` · ${used.otherPort}`) : null));
      const names = vlanNames(this.store.map);
      const vlan = (id) => (names.has(id) ? `${id} · ${names.get(id)}` : String(id));
      const text = used.vlans.all ? 'Tutte le VLAN (trunk)'
        : `${used.vlans.untagged != null ? `Non taggata VLAN ${vlan(used.vlans.untagged)}` : 'Nessuna VLAN non taggata'}${used.vlans.tagged.length ? ` · taggate ${used.vlans.tagged.join(', ')}` : ''}`;
      lines.push(h('span', { class: 'dm-tip-line muted' }, text));
    } else {
      lines.push(h('span', { class: 'dm-tip-line muted' }, 'Libera'));
    }
    fill(this.el, ...lines);
    this.el.hidden = false;
    // sotto la presa; a destra del pannello si allinea a destra, perche' non esca dalla scheda
    const host = this.scene.host.getBoundingClientRect();
    const at = jack.getBoundingClientRect();
    const panel = jack.parentNode.getBoundingClientRect();
    const right = at.left + at.width / 2 > panel.left + panel.width / 2;
    const width = this.el.offsetWidth;
    let left = right ? at.right - width : at.left;
    left = Math.max(host.left + 6, Math.min(host.right - width - 6, left));
    let top = at.bottom + 6;
    if (top + this.el.offsetHeight > host.bottom - 6) top = at.top - 6 - this.el.offsetHeight;
    this.el.style.transform = `translate(${Math.round(left - host.left)}px, ${Math.round(top - host.top)}px)`;
  }

  hide() {
    this.jack = null;
    this.el.hidden = true;
  }
}
