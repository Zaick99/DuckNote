// Il modello della mappa: apparati, cavi e zone, come stanno nel file della nota (mappa.json).
// Tutto quello che entra (dal file, da un incolla, da un messaggio della finestra) passa da
// normalize*: il resto del codice puo' fidarsi della forma e dei limiti.

export const NODE_W = 176;
export const NODE_H = 52;
export const GRID = 8;
export const MIN_ZOOM = 0.15;
export const MAX_ZOOM = 3;

export const KINDS = [
  { id: 'internet', label: 'Internet', tone: 'Blue' },
  { id: 'firewall', label: 'Firewall', tone: 'Pink' },
  { id: 'router', label: 'Router', tone: 'Blue' },
  { id: 'switch', label: 'Switch', tone: 'Teal' },
  { id: 'ap', label: 'Access point', tone: 'Purple' },
  { id: 'server', label: 'Server', tone: 'Indigo' },
  { id: 'nas', label: 'NAS', tone: 'Indigo' },
  { id: 'pc', label: 'PC', tone: 'Gray' },
  { id: 'laptop', label: 'Portatile', tone: 'Gray' },
  { id: 'phone', label: 'Telefono', tone: 'Teal' },
  { id: 'printer', label: 'Stampante', tone: 'Yellow' },
  { id: 'camera', label: 'Telecamera', tone: 'Yellow' },
  { id: 'iot', label: 'IoT', tone: 'Teal' },
  { id: 'other', label: 'Altro', tone: 'Gray' },
];
export const KIND = Object.fromEntries(KINDS.map((kind) => [kind.id, kind]));

/** Gli apparati che hanno porte da contare (il pannello frontale nella scheda). */
export const PORTED = new Set(['switch', 'router', 'firewall']);

export const CABLES = [
  { id: 'eth', label: 'Rame' },
  { id: 'fiber', label: 'Fibra' },
  { id: 'wifi', label: 'Wi-Fi' },
  { id: 'vpn', label: 'VPN' },
  { id: 'wan', label: 'WAN' },
];
export const CABLE = Object.fromEntries(CABLES.map((cable) => [cable.id, cable]));

export const SPEEDS = ['', '10M', '100M', '1G', '2.5G', '5G', '10G', '25G', '40G', '100G'];

export const TONES = ['Blue', 'Teal', 'Green', 'Yellow', 'Purple', 'Pink', 'Red', 'Gray'];

const LIMITS = { maps: 40, nodes: 2000, links: 4000, zones: 300, text: 160, notes: 4000 };
const COORD = 1_000_000;

// ------------------------------------------------------------------ identificativi

export function uid(prefix) {
  const bytes = crypto.getRandomValues(new Uint8Array(8));
  let id = prefix;
  for (const byte of bytes) id += (byte % 36).toString(36);
  return id;
}

const ID = /^[a-z][a-z0-9]{2,23}$/;

// ------------------------------------------------------------------ normalizzare

function str(value, max = LIMITS.text) {
  if (typeof value !== 'string') return '';
  // niente caratteri di controllo (gli a capo si', nelle note)
  return value.replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/g, '').slice(0, max).trim();
}

function num(value, min, max, fallback) {
  const n = typeof value === 'number' ? value : Number.parseFloat(value);
  return Number.isFinite(n) ? Math.min(max, Math.max(min, n)) : fallback;
}

export function blankMap(name = 'Rete') {
  return { id: uid('m'), name, nodes: [], links: [], zones: [], view: null };
}

export function blankDoc() {
  const map = blankMap();
  return { v: 1, current: map.id, maps: [map] };
}

export function normalizeDoc(raw) {
  const source = raw && typeof raw === 'object' ? raw : {};
  const maps = [];
  const seen = new Set();
  for (const item of Array.isArray(source.maps) ? source.maps.slice(0, LIMITS.maps) : []) {
    const map = normalizeMap(item);
    if (seen.has(map.id)) map.id = uid('m');
    seen.add(map.id);
    maps.push(map);
  }
  if (maps.length === 0) maps.push(blankMap());
  const current = maps.some((map) => map.id === source.current) ? source.current : maps[0].id;
  return { v: 1, current, maps };
}

export function normalizeMap(raw) {
  const source = raw && typeof raw === 'object' ? raw : {};
  const ids = new Set();
  const fresh = (id, prefix) => {
    const value = typeof id === 'string' && ID.test(id) && !ids.has(id) ? id : uid(prefix);
    ids.add(value);
    return value;
  };
  const id = fresh(source.id, 'm');
  const nodes = (Array.isArray(source.nodes) ? source.nodes : []).slice(0, LIMITS.nodes).map((node) => normalizeNode(node, fresh));
  const known = new Set(nodes.map((node) => node.id));
  const links = (Array.isArray(source.links) ? source.links : []).slice(0, LIMITS.links)
    .map((link) => normalizeLink(link, fresh))
    .filter((link) => known.has(link.a) && known.has(link.b) && link.a !== link.b);
  const zones = (Array.isArray(source.zones) ? source.zones : []).slice(0, LIMITS.zones).map((zone) => normalizeZone(zone, fresh));
  const view = source.view && typeof source.view === 'object'
    ? { x: num(source.view.x, -COORD, COORD, 0), y: num(source.view.y, -COORD, COORD, 0), k: num(source.view.k, MIN_ZOOM, MAX_ZOOM, 1) }
    : null;
  return { id, name: str(source.name, 60) || 'Rete', nodes, links, zones, view };
}

export function normalizeNode(raw, fresh = (id, prefix) => (typeof id === 'string' && ID.test(id) ? id : uid(prefix))) {
  const o = raw && typeof raw === 'object' ? raw : {};
  return {
    id: fresh(o.id, 'n'),
    kind: KIND[o.kind] ? o.kind : 'other',
    x: Math.round(num(o.x, -COORD, COORD, 0)),
    y: Math.round(num(o.y, -COORD, COORD, 0)),
    name: str(o.name),
    host: str(o.host, 253),
    mac: str(o.mac, 40),
    model: str(o.model),
    place: str(o.place),
    notes: str(o.notes, LIMITS.notes),
    ports: Math.round(num(o.ports, 0, 128, 0)),
  };
}

export function normalizeLink(raw, fresh = (id, prefix) => (typeof id === 'string' && ID.test(id) ? id : uid(prefix))) {
  const o = raw && typeof raw === 'object' ? raw : {};
  return {
    id: fresh(o.id, 'l'),
    a: typeof o.a === 'string' ? o.a : '',
    b: typeof o.b === 'string' ? o.b : '',
    kind: CABLE[o.kind] ? o.kind : 'eth',
    speed: SPEEDS.includes(o.speed) ? o.speed : '',
    pa: str(o.pa, 40),
    pb: str(o.pb, 40),
    vlan: str(o.vlan, 120),
    notes: str(o.notes, LIMITS.notes),
  };
}

export function normalizeZone(raw, fresh = (id, prefix) => (typeof id === 'string' && ID.test(id) ? id : uid(prefix))) {
  const o = raw && typeof raw === 'object' ? raw : {};
  return {
    id: fresh(o.id, 'z'),
    x: Math.round(num(o.x, -COORD, COORD, 0)),
    y: Math.round(num(o.y, -COORD, COORD, 0)),
    w: Math.round(num(o.w, 160, 40_000, 420)),
    h: Math.round(num(o.h, 96, 40_000, 260)),
    name: str(o.name, 60),
    cidr: str(o.cidr, 64),
    vlan: str(o.vlan, 60),
    tone: TONES.includes(o.tone) ? o.tone : 'Blue',
    notes: str(o.notes, LIMITS.notes),
  };
}

// ------------------------------------------------------------------ geometria degli oggetti

export const centerOf = (node) => ({ x: node.x + NODE_W / 2, y: node.y + NODE_H / 2 });

export const rectOf = (node) => ({ x: node.x, y: node.y, w: NODE_W, h: NODE_H });

export const inside = (point, rect) => point.x >= rect.x && point.x <= rect.x + rect.w && point.y >= rect.y && point.y <= rect.y + rect.h;

/** Gli apparati il cui centro sta nella zona: si muovono con lei. */
export function membersOf(map, zone) {
  return map.nodes.filter((node) => inside(centerOf(node), zone));
}

/** La zona piu' piccola che contiene l'apparato (per l'inventario). */
export function zoneOf(map, node) {
  let best = null;
  for (const zone of map.zones) {
    if (inside(centerOf(node), zone) && (!best || zone.w * zone.h < best.w * best.h)) best = zone;
  }
  return best;
}

/** Il riquadro che contiene tutto: apparati e zone. Null se la mappa e' vuota. */
export function boundsOf(map, ids = null) {
  let x0 = Infinity;
  let y0 = Infinity;
  let x1 = -Infinity;
  let y1 = -Infinity;
  const grow = (x, y, w, h) => {
    x0 = Math.min(x0, x);
    y0 = Math.min(y0, y);
    x1 = Math.max(x1, x + w);
    y1 = Math.max(y1, y + h);
  };
  for (const node of map.nodes) if (!ids || ids.has(node.id)) grow(node.x, node.y, NODE_W, NODE_H);
  for (const zone of map.zones) if (!ids || ids.has(zone.id)) grow(zone.x, zone.y, zone.w, zone.h);
  return Number.isFinite(x0) ? { x: x0, y: y0, w: x1 - x0, h: y1 - y0 } : null;
}

// ------------------------------------------------------------------ VLAN, porte, sottoreti

/** "10, 20, 30-32" come insieme di numeri; "tutte", "all" o "trunk" valgono tutte. */
export function parseVlans(textValue) {
  const result = { all: false, ids: new Set() };
  for (const part of String(textValue ?? '').toLowerCase().split(/[\s,;]+/)) {
    if (!part) continue;
    if (['all', 'tutte', 'trunk', '*'].includes(part)) {
      result.all = true;
      continue;
    }
    const range = /^(\d{1,4})-(\d{1,4})$/.exec(part);
    if (range) {
      const from = Number(range[1]);
      const to = Number(range[2]);
      if (from >= 1 && to <= 4094 && from <= to) for (let v = from; v <= to; v++) result.ids.add(v);
      continue;
    }
    const single = /^(?:vlan)?(\d{1,4})$/.exec(part);
    if (single && Number(single[1]) >= 1 && Number(single[1]) <= 4094) result.ids.add(Number(single[1]));
  }
  return result;
}

export function carriesVlan(textValue, vlan) {
  const parsed = parseVlans(textValue);
  return parsed.all ? String(textValue ?? '').trim().length > 0 : parsed.ids.has(vlan);
}

/**
 * Il numero di porta in fondo all'etichetta: "Gi1/0/12" e' la 12, "eth3" la 3, "5" la 5. Le
 * porte in fibra e di uplink (Te, Tw, Fo, Hu, xe, sfp) sono altre porte fisiche: nel pannello
 * delle porte numerate non contano, altrimenti "Te1/0/1" occuperebbe la porta 1 in rame.
 */
export function portNumber(label) {
  const text = String(label ?? '').trim();
  if (/^(te|tw|fo|hu|xe|et|sfp|qsfp)/i.test(text) && !/^eth/i.test(text)) return null;
  const match = /(\d{1,3})\s*$/.exec(text);
  return match ? Number(match[1]) : null;
}

const IPV4 = /^(25[0-5]|2[0-4]\d|1?\d?\d)(\.(25[0-5]|2[0-4]\d|1?\d?\d)){3}$/;

export const isIpv4 = (value) => IPV4.test(String(value ?? '').trim());

const ipNumber = (ip) => ip.split('.').reduce((acc, part) => acc * 256 + Number(part), 0);

/** "192.168.1.0/24": rete, prefisso e quanti indirizzi utili; null se non lo e'. */
export function parseCidr(value) {
  const match = /^\s*([\d.]+)\s*\/\s*(\d{1,2})\s*$/.exec(String(value ?? ''));
  if (!match || !isIpv4(match[1])) return null;
  const prefix = Number(match[2]);
  if (prefix > 32) return null;
  const size = 2 ** (32 - prefix);
  const base = Math.floor(ipNumber(match[1]) / size) * size;
  return { base, prefix, size, usable: prefix >= 31 ? size : Math.max(0, size - 2) };
}

export function inCidr(ip, cidr) {
  const net = typeof cidr === 'string' ? parseCidr(cidr) : cidr;
  if (!net || !isIpv4(ip)) return false;
  const n = ipNumber(ip.trim());
  return n >= net.base && n < net.base + net.size;
}

// ------------------------------------------------------------------ dalla scansione

/** Il tipo di apparato da quello che la scansione ha capito (DeviceTypes.Classify). */
export function kindFromScan(host) {
  const type = String(host?.type ?? '').toLowerCase();
  const os = String(host?.os ?? '').toLowerCase();
  const vendor = String(host?.vendor ?? '').toLowerCase();
  if (type.includes('stampante')) return 'printer';
  if (type.includes('videocamera') || type.includes('nvr')) return 'camera';
  if (type.includes('nas')) return 'nas';
  if (type.includes('router') || type.includes('gateway')) return 'router';
  if (type.includes('switch')) return 'switch';
  if (type.includes('voip')) return 'phone';
  if (type.includes('iot')) return 'iot';
  if (type.includes('proxmox') || type.includes('virtualizzazione') || type.includes('media server') || type.includes('linux')) return 'server';
  if (type.includes('windows') || os.includes('windows')) return 'pc';
  if (/ubiquiti|aruba|ruckus|tp-link eap|unifi/.test(vendor) && /\bap\b|access point|unifi/.test(`${type} ${vendor}`)) return 'ap';
  if (/apple|samsung|xiaomi|huawei|oneplus|google/.test(vendor)) return 'phone';
  // il primo indirizzo di una rete di casa o d'ufficio e' quasi sempre il gateway
  if (!type && /\.(1|254)$/.test(String(host?.ip ?? host?.key ?? ''))) return 'router';
  if (type.includes('web ui')) return 'other';
  return type ? 'other' : 'pc';
}

/** Il nome da dare a un apparato venuto dalla scansione. */
export function nameFromScan(host) {
  const name = String(host?.name ?? '').trim();
  if (name) return name.split('.')[0].length >= 3 ? name.split('.')[0] : name;
  return String(host?.key ?? host?.ip ?? '').trim();
}

/** Lo stesso host scritto in modi diversi (IP, nome, maiuscole) si riconosce. */
export function hostKey(value) {
  return String(value ?? '').trim().toLowerCase();
}
