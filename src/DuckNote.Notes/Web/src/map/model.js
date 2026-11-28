// Il modello della mappa: apparati, cavi e zone, come stanno nel file della nota (mappa.json).
// Tutto quello che entra (dal file, da un incolla, da un messaggio della finestra) passa da
// normalize*: il resto del codice puo' fidarsi della forma e dei limiti.

export const GRID = 8;

// ------------------------------------------------------------------ misure delle schede
// Le schede come in Vantage (github.com/levi-xy/vantage, licenza MIT): la linguetta del nome sta
// a cavallo del bordo di sopra e sporge di TAB_RAISE; il contenuto comincia CONTENT_TOP sotto
// quel bordo; switch, router e firewall hanno il pannello frontale a destra, dentro la scheda,
// con le prese disegnate. Internet e' una nuvola. Il rettangolo dell'apparato comprende la
// linguetta: x, y e' il suo angolo in alto a sinistra.

/** Quanto sporge la linguetta sopra al corpo, e quanto e' alta (entra nel corpo per il resto). */
export const TAB_RAISE = 15;
export const TAB_H = 22;
/** Il margine dei lati, e dove comincia il contenuto sotto al bordo di sopra del corpo. */
export const PAD_X = 14;
export const CONTENT_TOP = 24;
/** La tessera dell'icona e la colonna dei testi accanto al pannello. */
export const ICON = 34;
export const TEXT_W = 200;
export const CARD_W = 248;
/** Linguetta, contenuto (icona, riga dei valori) e margine di sotto. */
export const CARD_H = TAB_RAISE + CONTENT_TOP + ICON + 10 + 16 + 12;
export const CLOUD_W = 210;
export const CLOUD_H = 116;

// il pannello frontale: le prese a grandezza piena (come Vantage alla misura "m")
const RJ45 = [26, 22];
const CAGE = [34, 18];
const NUMBER_H = 11;
const CAPTION_H = 11;
const BARS_H = 9;
const JACK_GAP = 2;
const CELL_PAD = 8;
const ROW_GAP = 4;
const GROUP_GAP = 14;
/** Le prese di un gruppo su due file: 8, quattro colonne, dispari sopra. */
const GROUP = 8;
/** La cornice incassata attorno alle prese (margine compreso il bordo). */
export const INSET = 9;

/** Le misure "di prima" per un apparato nuovo, quando non si sa ancora che cosa sara'. */
export const NODE_W = CARD_W;
export const NODE_H = CARD_H;
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

/** Gli apparati che possono avere porte (il pannello frontale nella scheda): tutti tranne Internet. */
export const PORTED = new Set(KINDS.map((kind) => kind.id).filter((id) => id !== 'internet'));

/** Le scelte rapide del numero di porte per tipo (in rame, poi le gabbie SFP); ogni altro numero si scrive. */
export const PORT_PRESETS = {
  switch: { ports: [5, 8, 16, 24, 48], sfp: [2, 4] },
  firewall: { ports: [4, 6, 8, 10], sfp: [2, 4] },
  router: { ports: [4, 5, 8], sfp: [1, 2] },
  ap: { ports: [1, 2], sfp: [] },
  server: { ports: [1, 2, 4], sfp: [2] },
  nas: { ports: [1, 2, 4], sfp: [2] },
  pc: { ports: [1, 2], sfp: [] },
  laptop: { ports: [1], sfp: [] },
  phone: { ports: [1], sfp: [] },
  printer: { ports: [1], sfp: [] },
  camera: { ports: [1], sfp: [] },
  iot: { ports: [1], sfp: [] },
  other: { ports: [1, 2, 4, 8], sfp: [2] },
};

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
    sfp: Math.round(num(o.sfp, 0, 16, 0)),
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
    // il servizio di un collegamento VPN (tailscale, netbird, wireguard...): vpn.js li conosce
    vpn: typeof o.vpn === 'string' && /^[a-z][a-z0-9-]{1,23}$/.test(o.vpn) ? o.vpn : '',
    notes: str(o.notes, LIMITS.notes),
  };
}

/** I collegamenti che passano da una presa vera: non il Wi-Fi e non le VPN (wg0 non e' una porta). */
export const physical = (link) => link?.kind !== 'wifi' && link?.kind !== 'vpn';

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

const snapUp = (value) => Math.ceil(value / GRID) * GRID;
const half = (value) => Math.round(value * 2) / 2;
const panels = new Map();

/**
 * Il pannello frontale di uno switch (router, firewall): le prese RJ45 e le gabbie SFP, a
 * gruppi. Fino a 8 prese stanno in una fila, coi numeri sopra e sotto il nome di chi ci e'
 * collegato; oltre, due file a gruppi di 8 (dispari sopra, pari sotto, come sugli switch veri),
 * piu' piccole. Ogni presa ha la sua cella: il trattino delle VLAN, il numero e la didascalia
 * stanno dentro. Null se l'apparato non ha porte. Le misure sono relative all'angolo delle prese.
 */
export function panelOf(node) {
  if (!PORTED.has(node?.kind)) return null;
  const ports = node.ports > 0 ? node.ports : 0;
  const sfp = node.sfp > 0 ? node.sfp : 0;
  if (ports === 0 && sfp === 0) return null;
  const key = `${ports}:${sfp}`;
  if (panels.has(key)) return panels.get(key);

  const total = ports + sfp;
  const rows = ports > 8 || sfp > 4 || total > 10 ? 2 : 1;
  const scale = total <= 10 ? 1 : total <= 30 ? 0.78 : 0.66;
  const flags = { numbers: rows === 1, captions: rows === 1 };
  const dims = (shape) => [half(shape[0] * scale), half(shape[1] * scale)];
  const slotH = Math.max(ports ? dims(RJ45)[1] : 0, sfp ? dims(CAGE)[1] : 0);
  const parts = 2 + Number(flags.numbers) + Number(flags.captions);
  const cellH = slotH + BARS_H + (flags.numbers ? NUMBER_H : 0) + (flags.captions ? CAPTION_H : 0) + JACK_GAP * (parts - 1);
  const height = rows * cellH + (rows - 1) * ROW_GAP;

  const groups = [];
  if (ports) {
    if (rows === 2) for (let first = 1; first <= ports; first += GROUP) groups.push({ kind: 'rj45', first, count: Math.min(GROUP, ports - first + 1) });
    else groups.push({ kind: 'rj45', first: 1, count: ports });
  }
  if (sfp) groups.push({ kind: 'cage', first: 1, count: sfp });

  const jacks = [];
  let x = 0;
  for (const group of groups) {
    const [jw, jh] = dims(group.kind === 'cage' ? CAGE : RJ45);
    const cellW = jw + CELL_PAD;
    const cols = rows === 2 ? Math.ceil(group.count / 2) : group.count;
    for (let k = 0; k < group.count; k++) {
      const n = group.first + k;
      const col = rows === 2 ? Math.floor(k / 2) : k;
      const row = rows === 2 ? k % 2 : 0;
      // la fila di sopra e' capovolta: la presa guarda in su, il trattino delle VLAN sta sopra
      const flip = rows === 2 && row === 0;
      let at = 0;
      const next = (h, on) => {
        const y = at;
        if (on) at += h + JACK_GAP;
        return y;
      };
      const pieces = flip
        ? { barsY: next(BARS_H, true), jackY: next(slotH, true), numberY: 0, captionY: 0 }
        : { numberY: next(NUMBER_H, flags.numbers), jackY: next(slotH, true), barsY: next(BARS_H, true), captionY: next(CAPTION_H, flags.captions) };
      jacks.push({
        kind: group.kind,
        n,
        key: `${group.kind}:${n}`,
        row,
        flip,
        x: x + col * cellW,
        y: row * (cellH + ROW_GAP),
        w: cellW,
        h: cellH,
        jackX: (cellW - jw) / 2,
        jackY: pieces.jackY + (slotH - jh) / 2,
        jackW: jw,
        jackH: jh,
        numberY: pieces.numberY,
        barsY: pieces.barsY,
        captionY: pieces.captionY,
      });
    }
    x += cols * cellW + GROUP_GAP;
  }
  const panel = { jacks, width: x - GROUP_GAP, height, rows, scale, flags };
  panels.set(key, panel);
  return panel;
}

/** La cornice del pannello nella scheda: a destra, sotto al bordo di sopra (coordinate dell'apparato). */
export function panelBox(node) {
  const panel = panelOf(node);
  if (!panel) return null;
  const w = Math.ceil(panel.width) + 2 * INSET;
  const h = Math.ceil(panel.height) + 2 * INSET;
  const size = sizeOf(node);
  return { panel, x: size.w - PAD_X - w, y: TAB_RAISE + CONTENT_TOP, w, h, ox: size.w - PAD_X - w + INSET, oy: TAB_RAISE + CONTENT_TOP + INSET };
}

/** Quanto occupa l'apparato sulla mappa, linguetta compresa. */
export function sizeOf(node) {
  if (node?.kind === 'internet') return { w: CLOUD_W, h: CLOUD_H };
  const panel = panelOf(node);
  if (!panel) return { w: CARD_W, h: CARD_H };
  const w = Math.ceil(panel.width) + 2 * INSET;
  const h = Math.ceil(panel.height) + 2 * INSET;
  return {
    w: snapUp(PAD_X + TEXT_W + PAD_X + w + PAD_X),
    h: snapUp(TAB_RAISE + Math.max(CARD_H - TAB_RAISE, CONTENT_TOP + h + 12)),
  };
}

/**
 * La presa di una porta, relativa all'angolo dell'apparato: il riquadro della presa, il centro,
 * la fila e ax, la x dove ci si attacca il cavo. Nelle due file la presa di sopra e quella di
 * sotto stanno nella stessa colonna: i loro cavi si attaccano 3 pixel a sinistra e a destra del
 * centro, perche' non corrano uno sopra l'altro. kind: 'rj45' (rame) o 'cage' (SFP).
 */
export function portCell(node, n, kind = 'rj45') {
  const box = panelBox(node);
  if (!box || !Number.isInteger(n)) return null;
  const jack = box.panel.jacks.find((one) => one.kind === kind && one.n === n);
  if (!jack) return null;
  const x = box.ox + jack.x + jack.jackX;
  const y = box.oy + jack.y + jack.jackY;
  const cx = x + jack.jackW / 2;
  const ax = cx + (box.panel.rows === 2 ? (jack.row === 0 ? -3 : 3) : 0);
  return { x, y, w: jack.jackW, h: jack.jackH, cx, cy: y + jack.jackH / 2, ax, row: jack.row, key: jack.key };
}

/**
 * Gli apparati che contano le porte da zero (eth0, Gi0/0, ge-0/0/0): quelli dove un cavo nomina
 * una porta col numero 0, o una scheda Linux (ethN). Li' eth0 e' la presa 1, eth1 la 2. Per il
 * rame e per le SFP a parte.
 */
export function zeroBased(map) {
  const zero = new Map();
  const mark = (id, label) => {
    // eth1 su Linux e' la seconda scheda anche se eth0 non ha cavi sulla mappa
    const kind = portNumber(label) === 0 || /^eth\d+$/i.test(String(label ?? '').trim()) ? 'rj45' : sfpNumber(label) === 0 ? 'cage' : null;
    if (!kind) return;
    if (!zero.has(id)) zero.set(id, new Set());
    zero.get(id).add(kind);
  };
  for (const link of map?.links ?? []) {
    if (!physical(link)) continue;
    mark(link.a, link.pa);
    mark(link.b, link.pb);
  }
  return zero;
}

/**
 * La porta che un'etichetta nomina su un apparato: una presa in rame o una gabbia SFP, se ci sono.
 * zero: i tipi di porta che sull'apparato si contano da zero (da zeroBased).
 */
export function portOf(node, label, zero = null) {
  const copper = portNumber(label);
  if (copper != null) {
    const n = copper + (zero?.has('rj45') ? 1 : 0);
    return n >= 1 && n <= (node?.ports ?? 0) ? { kind: 'rj45', n } : null;
  }
  const cage = sfpNumber(label);
  if (cage == null) return null;
  const n = cage + (zero?.has('cage') ? 1 : 0);
  return n >= 1 && n <= (node?.sfp ?? 0) ? { kind: 'cage', n } : null;
}

export const centerOf = (node) => {
  const size = sizeOf(node);
  return { x: node.x + size.w / 2, y: node.y + size.h / 2 };
};

export const rectOf = (node) => ({ x: node.x, y: node.y, ...sizeOf(node) });

/** Il corpo della scheda, senza la parte della linguetta che sporge (per la nuvola, tutto). */
export function bodyOf(node) {
  const rect = rectOf(node);
  return node.kind === 'internet' ? rect : { x: rect.x, y: rect.y + TAB_RAISE, w: rect.w, h: rect.h - TAB_RAISE };
}

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
  for (const node of map.nodes) {
    if (!ids || ids.has(node.id)) {
      const size = sizeOf(node);
      grow(node.x, node.y, size.w, size.h);
    }
  }
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
 * Il numero in fondo all'etichetta: "Gi1/0/12" e' 12, "eth3" 3, "5" 5 (dove si conta da zero,
 * eth3 e' la quarta presa: vedi zeroBased). Le porte in fibra e di uplink (Te, Tw, Fo, Hu, xe,
 * sfp) sono altre porte fisiche: nel pannello delle porte in rame non contano, altrimenti
 * "Te1/0/1" occuperebbe la porta 1 in rame.
 */
export function portNumber(label) {
  const text = String(label ?? '').trim();
  if (/^(te|tw|fo|hu|xe|et|sfp|qsfp)/i.test(text) && !/^eth/i.test(text)) return null;
  const match = /(\d{1,3})\s*$/.exec(text);
  return match ? Number(match[1]) : null;
}

/** Il numero della gabbia SFP che un'etichetta nomina: "Te1/0/2" e' la 2, "sfp-sfpplus1" la 1. */
export function sfpNumber(label) {
  const text = String(label ?? '').trim();
  if (!/^(te|tw|fo|hu|xe|et|sfp|qsfp)/i.test(text) || /^eth/i.test(text)) return null;
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
  const name = `${host?.name ?? ''} ${host?.model ?? ''}`.toLowerCase();
  if (type.includes('stampante')) return 'printer';
  if (type.includes('videocamera') || type.includes('nvr')) return 'camera';
  if (type.includes('nas')) return 'nas';
  if (type.includes('router') || type.includes('gateway')) return 'router';
  if (type.includes('switch')) return 'switch';
  if (type.includes('access point')) return 'ap';
  if (type.includes('voip')) return 'phone';
  // i telefoni e i tablet: iPhone, iPad, Android, o un indirizzo privato da telefono
  if (/iphone|ipad|telefono|tablet/.test(type)) return 'phone';
  if (type === 'mac') return /macbook/.test(name) ? 'laptop' : 'pc';
  if (/\btv\b|chromecast|altoparlante|homepod|apple tv|amazon|console/.test(type)) return 'iot';
  if (type.includes('iot')) return 'iot';
  if (type.includes('server windows')) return 'server';
  if (/proxmox|virtualizzazione|media server|linux|raspberry|home assistant|macchina virtuale/.test(type)) return 'server';
  if (type.includes('windows') || os.includes('windows')) return /laptop-/.test(name) ? 'laptop' : 'pc';
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
