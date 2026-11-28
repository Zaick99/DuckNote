// La scheda a destra: i dati di quello che e' selezionato. Un apparato (nome, tipo, indirizzo
// col suo stato, MAC, marca, posizione, porte, cavi, note), un cavo (tipo, velocita', porte ai
// due capi, VLAN), una zona (rete, VLAN, colore), o piu' oggetti insieme (allinea, distribuisci,
// raggruppa). Si ricostruisce quando cambia la selezione; quando cambia la mappa si aggiornano
// solo i valori, e mai il campo in cui si sta scrivendo.

import { VPNS, logoSvg } from './vpn.js';
import { h, icon, fill, shortcut } from '../common/dom.js';
import { ui as uiIcons, kinds as kindIcons } from '../common/icons.js';
import {
  KINDS, KIND, PORTED, PORT_PRESETS, CABLES, SPEEDS, TONES, membersOf, parseCidr, inCidr, isIpv4, portOf, zeroBased, physical, hostKey,
} from './model.js';
import { show, hide } from '../common/motion.js';

const TONE_NAMES = { Blue: 'Blu', Teal: 'Verde acqua', Green: 'Verde', Yellow: 'Giallo', Purple: 'Viola', Pink: 'Rosa', Red: 'Rosso', Gray: 'Grigio' };
const STATE = {
  up: { label: 'Attivo', tone: 'Green' },
  down: { label: 'Spento', tone: 'Red' },
  wait: { label: 'Da verificare', tone: 'Orange' },
  unknown: { label: 'Mai controllato', tone: 'Gray' },
};

export class Inspector {
  /**
   * actions: { focusMap(), check(host), reveal(host), duplicate(), remove(), align(mode),
   *            spread(axis), arrange(), zoneAround(), connectSelected(), addHosts(list), frame(id) }
   */
  constructor(store, actions) {
    this.store = store;
    this.actions = actions;
    this.key = null;
    this.visible = false;
    this.ups = [];
    this.body = h('div', { class: 'dm-insp-body' });
    this.el = h('aside', { class: 'dm-inspector', hidden: true, 'aria-label': 'Scheda' }, this.body);
  }

  refresh(what) {
    const ids = [...this.store.sel];
    if (ids.length === 0) {
      this.key = null;
      if (this.visible) {
        this.visible = false;
        hide(this.el, { dx: 10, dy: 0 });
      }
      return;
    }
    const key = ids.join(',') + (this.store.readonly ? '#ro' : '');
    if (key !== this.key || what === 'doc' || (what === 'map' && ids.some((id) => !this.store.find(id)))) {
      this.key = key;
      this.build(ids);
      // anche se stava ancora sparendo: la scheda torna (la nuova animazione ferma la vecchia)
      if (!this.visible) {
        this.visible = true;
        show(this.el, { dx: 10, dy: 0, ms: 240 });
      }
      return;
    }
    for (const up of this.ups) up();
  }

  /** Il cursore nel campo dato (doppio clic sulla mappa, F2). */
  focus(field) {
    const input = this.body.querySelector(`[data-field="${field}"]`);
    if (input) {
      input.focus();
      input.select?.();
    }
  }

  build(ids) {
    this.ups = [];
    const types = ids.map((id) => this.store.typeOf(id));
    let parts;
    if (ids.length === 1 && types[0] === 'node') parts = this.nodeView(ids[0]);
    else if (ids.length === 1 && types[0] === 'link') parts = this.linkView(ids[0]);
    else if (ids.length === 1 && types[0] === 'zone') parts = this.zoneView(ids[0]);
    else parts = this.manyView(ids);
    const focused = document.activeElement;
    fill(this.body, ...parts.flat().filter(Boolean));
    if (focused && !document.contains(focused)) this.actions.focusMap();
    for (const up of this.ups) up();
    if (this.store.readonly) for (const el of this.body.querySelectorAll('input, textarea, select, button.edit')) el.disabled = true;
  }

  // ------------------------------------------------------------------ campi

  head(title, sub, tile) {
    const close = h('button', { class: 'dm-tool small dm-press', type: 'button', title: 'Chiudi la scheda (Esc)', onclick: () => {
      this.store.deselect();
      this.actions.focusMap();
    } }, icon(uiIcons.close, 13));
    return h('div', { class: 'dm-insp-head' }, tile ?? null, h('div', { class: 'dm-insp-titles' }, title, sub ?? null), close);
  }

  label(textValue, control, hint) {
    return h('label', { class: 'dm-row' }, h('span', { class: 'dm-label' }, textValue), control, hint ?? null);
  }

  input(id, field, { placeholder = '', mono = false, big = false, settle = false, type = 'text', max = 160, min, step } = {}) {
    const el = h('input', {
      class: `dm-field${mono ? ' mono' : ''}${big ? ' big' : ''}`,
      type,
      placeholder,
      spellcheck: 'false',
      autocomplete: 'off',
      maxlength: type === 'number' ? null : max,
      min,
      max: type === 'number' ? max : null,
      step,
      'data-field': field,
    });
    const value = () => this.store.find(id)?.[field] ?? '';
    let timer = 0;
    const commit = () => {
      clearTimeout(timer);
      const raw = type === 'number' ? Number.parseInt(el.value || '0', 10) || 0 : el.value;
      this.store.update(id, { [field]: raw }, `${id}:${field}`);
    };
    el.addEventListener('input', () => {
      clearTimeout(timer);
      // l'indirizzo entra quando ci si ferma: "192.168.1." sulla strada di "192.168.1.10" non e' un host
      if (settle) timer = setTimeout(commit, 1500);
      else commit();
    });
    el.addEventListener('change', commit);
    el.addEventListener('blur', () => {
      if (settle) commit();
      this.store.cut();
    });
    el.addEventListener('keydown', (event) => {
      if (event.key === 'Enter') {
        event.preventDefault();
        commit();
        this.actions.focusMap();
      } else if (event.key === 'Escape') {
        event.preventDefault();
        clearTimeout(timer);
        el.value = value();
        this.actions.focusMap();
      }
    });
    this.ups.push(() => {
      if (document.activeElement === el) return;
      const v = String(value());
      if (el.value !== v) el.value = v;
    });
    return el;
  }

  notes(id) {
    const el = h('textarea', { class: 'dm-field notes', rows: 3, placeholder: 'Note: credenziali no, ma tutto il resto si', spellcheck: 'true', 'data-field': 'notes', maxlength: 4000 });
    el.addEventListener('input', () => this.store.update(id, { notes: el.value }, `${id}:notes`));
    el.addEventListener('blur', () => this.store.cut());
    el.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        this.actions.focusMap();
      }
    });
    this.ups.push(() => {
      if (document.activeElement === el) return;
      const v = this.store.find(id)?.notes ?? '';
      if (el.value !== v) el.value = v;
    });
    return this.label('Note', el);
  }

  buttons(...items) {
    return h('div', { class: 'dm-insp-actions' }, ...items);
  }

  /** Le scelte rapide di un numero (le porte): un clic lo scrive, quello in uso e' acceso. */
  picks(id, field, current, values) {
    return [0, ...values].map((value) => h('button', {
      class: `dm-pick dm-press edit${current === value ? ' on' : ''}`,
      type: 'button',
      'data-pick': `${field}-${value}`,
      disabled: this.store.readonly,
      title: value === 0 ? (field === 'sfp' ? 'Nessuna porta SFP' : 'Nessuna porta') : `${value} ${field === 'sfp' ? (value === 1 ? 'porta SFP' : 'porte SFP') : (value === 1 ? 'porta' : 'porte')}`,
      onclick: () => this.store.update(id, { [field]: value }),
    }, value === 0 ? '–' : String(value)));
  }

  /** keep: un comando che non cambia la mappa (il Promemoria), attivo anche in sola lettura. */
  button(path, label, onclick, { danger = false, primary = false, title, keep = false } = {}) {
    return h('button', { class: `dm-button small dm-press${keep ? ' keep' : ' edit'}${danger ? ' danger' : ''}${primary ? ' primary' : ''}`, type: 'button', title, onclick }, path ? icon(path, 13) : null, h('span', {}, label));
  }

  // ------------------------------------------------------------------ un apparato

  nodeView(id) {
    const get = () => this.store.find(id);
    const tile = h('span', { class: 'dm-insp-tile' }, icon(kindIcons.other, 22, 24));
    const kindLabel = h('div', { class: 'dm-insp-sub' });
    this.ups.push(() => {
      const node = get();
      if (!node) return;
      const kind = KIND[node.kind] ?? KIND.other;
      tile.style.setProperty('--tone', `var(--c-${kind.tone})`);
      tile.style.setProperty('--tone-soft', `var(--t-${kind.tone}-soft)`);
      tile.querySelector('path').setAttribute('d', kindIcons[node.kind] ?? kindIcons.other);
      kindLabel.textContent = kind.label;
    });
    const name = this.input(id, 'name', { placeholder: 'Nome dell\'apparato', big: true, max: 160 });

    const kinds = h('div', { class: 'dm-kind-pick', role: 'radiogroup', 'aria-label': 'Tipo' });
    for (const kind of KINDS) {
      const button = h('button', {
        class: 'dm-kind-dot dm-press edit',
        type: 'button',
        title: kind.label,
        role: 'radio',
        style: { '--tone': `var(--c-${kind.tone})`, '--tone-soft': `var(--t-${kind.tone}-soft)` },
        onclick: () => this.store.update(id, { kind: kind.id }),
      }, icon(kindIcons[kind.id], 17, 24));
      kinds.append(button);
      this.ups.push(() => {
        const on = get()?.kind === kind.id;
        button.classList.toggle('on', on);
        button.setAttribute('aria-checked', String(on));
      });
    }

    const host = this.input(id, 'host', { placeholder: '192.168.1.10 o nas.casa.lan', mono: true, settle: true, max: 253 });
    const status = h('div', { class: 'dm-status' });
    this.ups.push(() => fill(status, ...this.status(get())));

    // le porte di ogni apparato (tranne Internet): un numero scritto o una scelta rapida per il tipo
    const ports = this.input(id, 'ports', { type: 'number', min: 0, max: 128, step: 1, mono: true });
    const portPicks = h('div', { class: 'dm-picks' });
    const portsRow = this.label('Porte', h('div', { class: 'dm-count' }, ports, portPicks));
    // le gabbie SFP (fibra, uplink) stanno sul pannello in un gruppo a parte, come sugli switch veri
    const sfp = this.input(id, 'sfp', { type: 'number', min: 0, max: 16, step: 1, mono: true });
    const sfpPicks = h('div', { class: 'dm-picks' });
    const sfpRow = this.label('Porte SFP', h('div', { class: 'dm-count' }, sfp, sfpPicks));
    const panel = h('div', { class: 'dm-ports' });
    this.ups.push(() => {
      const node = get();
      const has = node && PORTED.has(node.kind);
      portsRow.hidden = !has;
      sfpRow.hidden = !has;
      panel.hidden = !has || (!node.ports && !node.sfp);
      if (!has) return;
      const presets = PORT_PRESETS[node.kind] ?? PORT_PRESETS.other;
      fill(portPicks, ...this.picks(id, 'ports', node.ports, presets.ports));
      fill(sfpPicks, ...this.picks(id, 'sfp', node.sfp, presets.sfp));
      if (node.ports || node.sfp) fill(panel, ...this.panel(node));
    });

    const cables = h('div', { class: 'dm-cables' });
    this.ups.push(() => fill(cables, ...this.cables(get())));

    return [
      this.head(name, kindLabel, tile),
      this.label('Tipo', kinds),
      this.label('Indirizzo', host, status),
      this.label('MAC', this.input(id, 'mac', { placeholder: '00:11:22:33:44:55', mono: true, max: 40 })),
      this.label('Marca e modello', this.input(id, 'model', { placeholder: 'es. switch 24 porte PoE' })),
      this.label('Posizione', this.input(id, 'place', { placeholder: 'es. sala server, rack 1, U12' })),
      portsRow,
      sfpRow,
      panel,
      cables,
      this.notes(id),
      this.buttons(
        this.button(uiIcons.bell, 'Promemoria', () => this.actions.remind(get()), { keep: true, title: 'Una voce nel calendario legata a questo apparato (una scadenza, un intervento)' }),
        this.button(uiIcons.duplicate, 'Duplica', () => this.actions.duplicate(), { title: `Duplica (${shortcut('D')})` }),
        this.button(uiIcons.trash, 'Elimina', () => this.actions.remove(), { danger: true, title: 'Elimina (Canc)' })),
    ];
  }

  /** Lo stato dell'indirizzo, con i pulsanti per controllarlo e per vederlo nella pagina Rete. */
  status(node) {
    if (!node?.host) return [h('span', { class: 'dm-hint' }, 'Con un indirizzo DuckNote controlla l\'apparato e lo colora col suo stato.')];
    const host = this.store.hostOf(node);
    const state = this.store.stateOf(node);
    const look = STATE[state] ?? STATE.unknown;
    const details = [];
    if (state === 'up' && host?.rtt) details.push(host.rtt);
    if (host?.ip && hostKey(host.ip) !== hostKey(node.host)) details.push(host.ip);
    return [
      h('span', { class: `dm-chip tone-${look.tone}` }, h('span', { class: `dm-dot st-${state}` }), look.label, details.length ? h('span', { class: 'dm-chip-sub' }, details.join(' · ')) : null),
      h('span', { class: 'dm-spacer' }),
      h('button', { class: 'dm-tool small dm-press', type: 'button', title: 'Controlla adesso', onclick: () => this.actions.check(node.host) }, icon(uiIcons.pulse, 14)),
      h('button', { class: 'dm-tool small dm-press', type: 'button', title: 'Apri nella pagina Rete', onclick: () => this.actions.reveal(node.host) }, icon(uiIcons.open, 14)),
    ];
  }

  /** Il pannello frontale: una casella per porta, piena se un cavo la usa. */
  panel(node) {
    const zero = zeroBased(this.store.map).get(node.id);
    const used = new Map();
    for (const link of this.store.map.links) {
      if (!physical(link)) continue;
      const mine = link.a === node.id ? link.pa : link.b === node.id ? link.pb : null;
      const port = mine == null ? null : portOf(node, mine, zero);
      if (port) used.set(`${port.kind}:${port.n}`, link);
    }
    const nodes = new Map(this.store.map.nodes.map((n) => [n.id, n]));
    const cells = [];
    const cell = (kind, n) => {
      const link = used.get(`${kind}:${n}`);
      const other = link ? nodes.get(link.a === node.id ? link.b : link.a) : null;
      const name = kind === 'cage' ? `SFP ${n}` : `Porta ${n}`;
      cells.push(h('button', {
        class: `dm-port-cell${kind === 'cage' ? ' sfp' : ''}${link ? ` on k-${link.kind}` : ''}`,
        type: 'button',
        title: link ? `${name}: ${other?.name ?? ''}${link.vlan ? ` · VLAN ${link.vlan}` : ''}${link.speed ? ` · ${link.speed}` : ''}` : `${name}: libera`,
        onclick: () => {
          if (link) this.store.select([link.id]);
        },
      }, kind === 'cage' ? `S${n}` : String(n)));
    };
    for (let n = 1; n <= node.ports; n++) cell('rj45', n);
    for (let n = 1; n <= node.sfp; n++) cell('cage', n);
    const total = node.ports + node.sfp;
    const free = total - used.size;
    const named = node.kind === 'switch' || node.kind === 'router' || node.kind === 'firewall'
      ? 'Gi1/0/12 e\' la 12; quelle in fibra (Te1/0/1, sfp-sfpplus1) vanno nelle SFP'
      : 'LAN2 e\' la 2, eth0 la prima';
    return [h('div', { class: 'dm-ports-grid', style: { gridTemplateColumns: `repeat(${Math.min(12, Math.max(4, Math.ceil(total / 2)))}, 1fr)` } }, ...cells),
      h('div', { class: 'dm-hint' }, `${free} libere su ${total}. La porta di un cavo si conta dal numero in fondo al nome (${named}).`)];
  }

  /** I cavi dell'apparato: un clic li seleziona. */
  cables(node) {
    if (!node) return [];
    const nodes = new Map(this.store.map.nodes.map((n) => [n.id, n]));
    const links = this.store.map.links.filter((l) => l.a === node.id || l.b === node.id);
    if (links.length === 0) return [];
    const rows = links.map((link) => {
      const mine = link.a === node.id;
      const other = nodes.get(mine ? link.b : link.a);
      const ports = [mine ? link.pa : link.pb, mine ? link.pb : link.pa];
      const detail = [ports[0] || ports[1] ? `${ports[0] || '?'} → ${ports[1] || '?'}` : '', link.speed, link.vlan ? `VLAN ${link.vlan}` : ''].filter(Boolean).join(' · ');
      return h('button', { class: `dm-cable k-${link.kind}`, type: 'button', onclick: () => this.store.select([link.id]) },
        h('span', { class: 'dm-cable-line' }),
        h('span', { class: 'dm-cable-text' }, h('span', { class: 'dm-cable-name' }, other?.name || 'Apparato'), detail ? h('span', { class: 'dm-cable-sub' }, detail) : null));
    });
    return [h('div', { class: 'dm-label' }, `Cavi (${links.length})`), ...rows];
  }

  // ------------------------------------------------------------------ un cavo

  linkView(id) {
    const get = () => this.store.find(id);
    const ends = h('div', { class: 'dm-insp-sub' });
    const labelA = h('span', { class: 'dm-label' });
    const labelB = h('span', { class: 'dm-label' });
    const nodeName = (nodeId) => this.store.node(nodeId)?.name || 'Apparato';
    this.ups.push(() => {
      const link = get();
      if (!link) return;
      ends.textContent = `${nodeName(link.a)} ⟷ ${nodeName(link.b)}`;
      labelA.textContent = `Porta su ${nodeName(link.a)}`;
      labelB.textContent = `Porta su ${nodeName(link.b)}`;
    });

    const kinds = h('div', { class: 'dm-chips', role: 'radiogroup', 'aria-label': 'Tipo di cavo' });
    for (const cable of CABLES) {
      const chip = h('button', { class: `dm-choice dm-press edit k-${cable.id}`, type: 'button', role: 'radio', onclick: () => this.store.update(id, { kind: cable.id }) },
        h('span', { class: 'dm-choice-line' }), cable.label);
      kinds.append(chip);
      this.ups.push(() => {
        const on = get()?.kind === cable.id;
        chip.classList.toggle('on', on);
        chip.setAttribute('aria-checked', String(on));
      });
    }

    // il servizio di un collegamento VPN: il logo sul collegamento e il colore del logo
    const services = h('div', { class: 'dm-vpns', role: 'radiogroup', 'aria-label': 'Servizio VPN' });
    for (const vpn of [...VPNS, null]) {
      const chip = h('button', {
        class: `dm-vpn dm-press edit${vpn ? '' : ' none'}`,
        type: 'button',
        role: 'radio',
        'data-vpn': vpn?.id ?? '',
        title: vpn ? `${vpn.label} · ${vpn.kind}` : 'Una VPN qualsiasi, senza logo',
        style: vpn ? { '--vpn': `var(--vpn-${vpn.id})` } : null,
        onclick: () => this.store.update(id, { vpn: vpn?.id ?? '' }),
      }, vpn ? logoSvg(vpn, 16) : null, h('span', {}, vpn ? vpn.short ?? vpn.label : 'Altra'));
      services.append(chip);
      this.ups.push(() => {
        const on = (get()?.vpn ?? '') === (vpn?.id ?? '') || (!vpn && !VPNS.some((one) => one.id === get()?.vpn));
        chip.classList.toggle('on', on);
        chip.setAttribute('aria-checked', String(on));
      });
    }
    const servicesRow = this.label('Servizio', services);
    this.ups.push(() => {
      servicesRow.hidden = get()?.kind !== 'vpn';
    });

    const speed = h('select', { class: 'dm-field', 'data-field': 'speed' }, ...SPEEDS.map((value) => h('option', { value }, value || 'Non indicata')));
    speed.addEventListener('change', () => this.store.update(id, { speed: speed.value }));
    this.ups.push(() => {
      const v = get()?.speed ?? '';
      if (speed.value !== v) speed.value = v;
    });

    const swap = h('button', { class: 'dm-tool small dm-press edit', type: 'button', title: 'Scambia i due capi', onclick: () => this.store.swapEnds(id) }, icon(uiIcons.swap, 14));
    const title = h('div', { class: 'dm-insp-title' }, 'Cavo');
    const portA = this.input(id, 'pa', { placeholder: 'es. Gi1/0/12, eth0, porta 5', mono: true, max: 40 });
    const portB = this.input(id, 'pb', { placeholder: 'es. LAN1', mono: true, max: 40 });
    // un collegamento VPN o Wi-Fi non passa da una presa: ai capi c'e' un'interfaccia (wg0, tailscale0)
    this.ups.push(() => {
      const link = get();
      if (!link) return;
      const wire = link.kind === 'vpn' || link.kind === 'wifi';
      title.textContent = link.kind === 'vpn' ? 'Collegamento VPN' : link.kind === 'wifi' ? 'Collegamento Wi-Fi' : 'Cavo';
      labelA.textContent = `${wire ? 'Interfaccia' : 'Porta'} su ${nodeName(link.a)}`;
      labelB.textContent = `${wire ? 'Interfaccia' : 'Porta'} su ${nodeName(link.b)}`;
      portA.placeholder = link.kind === 'vpn' ? 'es. wg0, tailscale0' : link.kind === 'wifi' ? 'es. wlan0' : 'es. Gi1/0/12, eth0, porta 5';
      portB.placeholder = link.kind === 'vpn' ? 'es. wg0' : link.kind === 'wifi' ? 'es. SSID Ufficio' : 'es. LAN1';
    });
    return [
      this.head(h('div', { class: 'dm-insp-title-row' }, title, swap), ends, h('span', { class: 'dm-insp-tile line' }, icon(uiIcons.link, 18))),
      this.label('Tipo', kinds),
      servicesRow,
      this.label('Velocità', speed),
      h('label', { class: 'dm-row' }, labelA, portA),
      h('label', { class: 'dm-row' }, labelB, portB),
      this.label('VLAN', this.input(id, 'vlan', { placeholder: '10, oppure 10,20,30-35, oppure trunk', mono: true, max: 120 })),
      this.notes(id),
      this.buttons(this.button(uiIcons.trash, 'Elimina il cavo', () => this.actions.remove(), { danger: true })),
    ];
  }

  // ------------------------------------------------------------------ una zona

  zoneView(id) {
    const get = () => this.store.find(id);
    const tile = h('span', { class: 'dm-insp-tile zone' }, icon(uiIcons.zone, 18));
    this.ups.push(() => {
      const zone = get();
      if (zone) tile.style.setProperty('--tone', `var(--c-${zone.tone})`);
    });
    const name = this.input(id, 'name', { placeholder: 'Nome della zona', big: true, max: 60 });
    const cidr = this.input(id, 'cidr', { placeholder: '192.168.1.0/24', mono: true, max: 64 });
    const cidrHint = h('div', { class: 'dm-hint' });
    this.ups.push(() => {
      const zone = get();
      const net = parseCidr(zone?.cidr);
      cidrHint.textContent = !zone?.cidr ? 'La sottorete: chi la scansione trova li\' dentro finisce in questa zona.'
        : net ? `${net.usable.toLocaleString('it-IT')} indirizzi utili.` : 'Si scrive come 192.168.1.0/24.';
    });

    const tones = h('div', { class: 'dm-tones', role: 'radiogroup', 'aria-label': 'Colore' });
    for (const tone of TONES) {
      const dot = h('button', { class: 'dm-tone dm-press edit', type: 'button', role: 'radio', title: TONE_NAMES[tone], style: { '--tone': `var(--c-${tone})` }, onclick: () => this.store.update(id, { tone }) });
      tones.append(dot);
      this.ups.push(() => {
        const on = get()?.tone === tone;
        dot.classList.toggle('on', on);
        dot.setAttribute('aria-checked', String(on));
      });
    }

    const members = h('div', { class: 'dm-members' });
    this.ups.push(() => {
      const zone = get();
      if (!zone) return;
      const inside = membersOf(this.store.map, zone);
      const net = parseCidr(zone.cidr);
      const placed = this.store.placedHosts();
      const waiting = net ? this.store.hosts.filter((host) => {
        const ip = isIpv4(host.ip) ? host.ip : host.key;
        return inCidr(ip, net) && !placed.has(hostKey(host.key || host.ip));
      }) : [];
      fill(members, 
        h('span', {}, inside.length === 1 ? '1 apparato nella zona' : `${inside.length} apparati nella zona`),
        h('span', { class: 'dm-spacer' }),
        inside.length ? h('button', { class: 'dm-link-button', type: 'button', onclick: () => this.store.select(inside.map((n) => n.id)) }, 'Seleziona') : null,
        waiting.length && !this.store.readonly ? h('button', { class: 'dm-link-button', type: 'button', title: 'Gli host della scansione in questa rete che non sono ancora sulla mappa', onclick: () => this.actions.addHosts(waiting) }, `+${waiting.length} dalla rete`) : null);
    });

    return [
      this.head(name, h('div', { class: 'dm-insp-sub' }, 'Zona'), tile),
      this.label('Rete', cidr, cidrHint),
      this.label('VLAN', this.input(id, 'vlan', { placeholder: 'es. 10', mono: true, max: 60 })),
      this.label('Colore', tones),
      members,
      this.notes(id),
      this.buttons(this.button(uiIcons.trash, 'Elimina la zona', () => this.actions.remove(), { danger: true, title: 'Gli apparati restano dove sono' })),
    ];
  }

  // ------------------------------------------------------------------ piu' oggetti

  manyView(ids) {
    const nodes = ids.filter((id) => this.store.typeOf(id) === 'node');
    const links = ids.filter((id) => this.store.typeOf(id) === 'link');
    const zones = ids.filter((id) => this.store.typeOf(id) === 'zone');
    const parts = [];
    if (nodes.length) parts.push(nodes.length === 1 ? '1 apparato' : `${nodes.length} apparati`);
    if (links.length) parts.push(links.length === 1 ? '1 cavo' : `${links.length} cavi`);
    if (zones.length) parts.push(zones.length === 1 ? '1 zona' : `${zones.length} zone`);
    const title = h('div', { class: 'dm-insp-title' }, parts.join(', '));
    const out = [this.head(title, h('div', { class: 'dm-insp-sub' }, 'Selezionati'), h('span', { class: 'dm-insp-tile line' }, icon(uiIcons.pointer, 17)))];

    if (nodes.length >= 2 && !this.store.readonly) {
      const align = (path, mode, label) => h('button', { class: 'dm-tool dm-press', type: 'button', title: label, onclick: () => this.actions.align(mode) }, icon(path, 16));
      out.push(this.label('Allinea', h('div', { class: 'dm-tool-row' },
        align(uiIcons.alignLeft, 'left', 'A sinistra'), align(uiIcons.alignCenter, 'center', 'Al centro'), align(uiIcons.alignRight, 'right', 'A destra'),
        h('span', { class: 'dm-tool-gap' }),
        align(uiIcons.alignTop, 'top', 'In alto'), align(uiIcons.alignMiddle, 'middle', 'A meta\''), align(uiIcons.alignBottom, 'bottom', 'In basso'))));
    }
    if (nodes.length >= 3 && !this.store.readonly) {
      out.push(this.label('Distribuisci', h('div', { class: 'dm-tool-row' },
        h('button', { class: 'dm-tool dm-press', type: 'button', title: 'Stessa distanza in orizzontale', onclick: () => this.actions.spread('x') }, icon(uiIcons.spreadH, 16)),
        h('button', { class: 'dm-tool dm-press', type: 'button', title: 'Stessa distanza in verticale', onclick: () => this.actions.spread('y') }, icon(uiIcons.spreadV, 16)))));
    }
    const actions = [];
    if (nodes.length === 2) actions.push(this.button(uiIcons.link, 'Collega', () => this.actions.connectSelected(), { primary: true }));
    if (nodes.length >= 1) actions.push(this.button(uiIcons.zone, 'Raggruppa in una zona', () => this.actions.zoneAround(), { title: 'Una zona attorno agli apparati selezionati (Z)' }));
    if (nodes.length >= 2) actions.push(this.button(uiIcons.layout, 'Disponi', () => this.actions.arrange(), { title: 'Mette in ordine solo gli apparati selezionati' }));
    actions.push(this.button(uiIcons.trash, 'Elimina', () => this.actions.remove(), { danger: true }));
    out.push(this.buttons(...actions));
    return out;
  }
}
