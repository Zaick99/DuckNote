// Lo stato della pagina: il documento (le mappe della nota), la mappa aperta, la selezione, la
// cronologia di Annulla e gli host che la finestra conosce. Ogni modifica passa da qui: prima
// la foto per Annulla (begin), poi il cambiamento, poi l'avviso a chi disegna e a chi salva.

import {
  NODE_W, NODE_H, GRID, KIND, blankDoc, blankMap, normalizeDoc, normalizeMap, normalizeNode, normalizeLink, normalizeZone,
  uid, centerOf, membersOf, boundsOf, hostKey, kindFromScan, nameFromScan, inCidr, isIpv4,
} from './model.js';

const HISTORY = 100;
const MERGE_MS = 1200;

class History {
  constructor() {
    this.undo = [];
    this.redo = [];
    this.key = null;
    this.at = 0;
  }

  /** La foto di prima; i passi dello stesso gesto (una parola scritta, le frecce) si uniscono. */
  push(photo, key) {
    const now = Date.now();
    if (key && key === this.key && now - this.at < MERGE_MS) {
      this.at = now;
      return;
    }
    this.undo.push(photo);
    if (this.undo.length > HISTORY) this.undo.shift();
    this.redo.length = 0;
    this.key = key ?? null;
    this.at = now;
  }

  cut() {
    this.key = null;
  }
}

const photoOf = (map) => JSON.stringify({ name: map.name, nodes: map.nodes, links: map.links, zones: map.zones });

export class Store {
  constructor() {
    this.doc = blankDoc();
    this.sel = new Set();
    this.readonly = false;
    this.hosts = [];
    this.groups = [];
    this.states = new Map();
    this.listeners = new Set();
    this.histories = new Map();
    this.revision = 0;
  }

  get map() {
    return this.doc.maps.find((map) => map.id === this.doc.current) ?? this.doc.maps[0];
  }

  /** what: 'map' (contenuto, da salvare), 'select', 'hosts', 'doc' (un'altra mappa o un altro file). */
  on(listener) {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  emit(what) {
    // la revisione cambia con la mappa: chi ha fatto calcoli sulla geometria sa se valgono ancora
    if (what === 'map' || what === 'doc') this.revision++;
    for (const listener of this.listeners) listener(what);
  }

  history(map = this.map) {
    let history = this.histories.get(map.id);
    if (!history) this.histories.set(map.id, (history = new History()));
    return history;
  }

  // ------------------------------------------------------------------ documento

  load(raw) {
    this.doc = normalizeDoc(raw ?? null);
    this.sel.clear();
    this.histories.clear();
    this.emit('doc');
  }

  clear() {
    this.doc = blankDoc();
    this.sel.clear();
    this.histories.clear();
    this.hosts = [];
    this.groups = [];
    this.states.clear();
    this.emit('doc');
  }

  json() {
    return JSON.stringify(this.doc);
  }

  switchTo(id) {
    if (!this.doc.maps.some((map) => map.id === id) || this.doc.current === id) return;
    this.doc.current = id;
    this.sel.clear();
    this.emit('doc');
    this.emit('map');
  }

  addMap(name) {
    if (this.readonly) return null;
    const map = blankMap(name || this.freeName('Mappa'));
    this.doc.maps.push(map);
    this.doc.current = map.id;
    this.sel.clear();
    this.emit('doc');
    this.emit('map');
    return map;
  }

  duplicateMap() {
    if (this.readonly) return null;
    const copy = normalizeMap({ ...JSON.parse(JSON.stringify(this.map)), id: uid('m'), name: this.freeName(`${this.map.name} (copia)`) });
    this.doc.maps.push(copy);
    this.doc.current = copy.id;
    this.sel.clear();
    this.emit('doc');
    this.emit('map');
    return copy;
  }

  renameMap(name) {
    const clean = String(name ?? '').trim().slice(0, 60);
    if (this.readonly || !clean || clean === this.map.name) return;
    this.begin();
    this.map.name = clean;
    this.emit('doc');
    this.emit('map');
  }

  removeMap() {
    if (this.readonly) return;
    const index = this.doc.maps.indexOf(this.map);
    this.histories.delete(this.map.id);
    this.doc.maps.splice(index, 1);
    if (this.doc.maps.length === 0) this.doc.maps.push(blankMap());
    this.doc.current = this.doc.maps[Math.min(index, this.doc.maps.length - 1)].id;
    this.sel.clear();
    this.emit('doc');
    this.emit('map');
  }

  freeName(base) {
    const names = new Set(this.doc.maps.map((map) => map.name));
    if (!names.has(base)) return base;
    for (let i = 2; ; i++) if (!names.has(`${base} ${i}`)) return `${base} ${i}`;
  }

  // ------------------------------------------------------------------ cronologia

  /** La foto prima di una modifica. Falso se la mappa e' in sola lettura. */
  begin(key) {
    if (this.readonly) return false;
    this.history().push(photoOf(this.map), key);
    return true;
  }

  /** Il gesto e' finito: il prossimo cambiamento e' un passo nuovo di Annulla. */
  cut() {
    this.history().cut();
  }

  get canUndo() {
    return !this.readonly && this.history().undo.length > 0;
  }

  get canRedo() {
    return !this.readonly && this.history().redo.length > 0;
  }

  undo() {
    const history = this.history();
    if (this.readonly || history.undo.length === 0) return false;
    history.redo.push(photoOf(this.map));
    this.restore(history.undo.pop());
    history.cut();
    return true;
  }

  redo() {
    const history = this.history();
    if (this.readonly || history.redo.length === 0) return false;
    history.undo.push(photoOf(this.map));
    this.restore(history.redo.pop());
    history.cut();
    return true;
  }

  restore(photo) {
    const saved = JSON.parse(photo);
    const map = this.map;
    const again = normalizeMap({ ...saved, id: map.id, view: map.view });
    map.name = again.name;
    map.nodes = again.nodes;
    map.links = again.links;
    map.zones = again.zones;
    for (const id of [...this.sel]) if (!this.find(id)) this.sel.delete(id);
    this.emit('doc');
    this.emit('map');
    this.emit('select');
  }

  // ------------------------------------------------------------------ cercare

  find(id) {
    const map = this.map;
    return map.nodes.find((n) => n.id === id) ?? map.links.find((l) => l.id === id) ?? map.zones.find((z) => z.id === id) ?? null;
  }

  typeOf(id) {
    const map = this.map;
    if (map.nodes.some((n) => n.id === id)) return 'node';
    if (map.links.some((l) => l.id === id)) return 'link';
    if (map.zones.some((z) => z.id === id)) return 'zone';
    return null;
  }

  node(id) {
    return this.map.nodes.find((n) => n.id === id) ?? null;
  }

  selected(type) {
    return [...this.sel].filter((id) => this.typeOf(id) === type).map((id) => this.find(id));
  }

  // ------------------------------------------------------------------ selezione

  select(ids, add = false) {
    if (!add) this.sel.clear();
    for (const id of ids) if (this.find(id)) this.sel.add(id);
    this.cut();
    this.emit('select');
  }

  toggle(id) {
    if (this.sel.has(id)) this.sel.delete(id);
    else if (this.find(id)) this.sel.add(id);
    this.emit('select');
  }

  deselect() {
    if (this.sel.size === 0) return;
    this.sel.clear();
    this.emit('select');
  }

  selectAll() {
    this.select(this.map.nodes.map((n) => n.id));
  }

  // ------------------------------------------------------------------ modificare

  /** Un apparato nuovo, col suo angolo in alto a sinistra a (x, y). */
  addNode(kind, x, y, extra = {}) {
    if (!this.begin()) return null;
    const node = normalizeNode({ kind, name: extra.name ?? this.freeNodeName(KIND[kind]?.label ?? 'Apparato'), ...extra, id: uid('n'), x: snap(x), y: snap(y) });
    this.map.nodes.push(node);
    this.emit('map');
    return node;
  }

  freeNodeName(base) {
    const names = new Set(this.map.nodes.map((n) => n.name));
    if (!names.has(base)) return base;
    for (let i = 2; ; i++) if (!names.has(`${base} ${i}`)) return `${base} ${i}`;
  }

  /** Un cavo fra due apparati: il tipo dipende da chi collega (un access point va in Wi-Fi). */
  addLink(a, b, extra = {}) {
    const from = this.node(a);
    const to = this.node(b);
    if (!from || !to || a === b || !this.begin()) return null;
    const wireless = (from.kind === 'ap') !== (to.kind === 'ap') && [from, to].some((n) => ['laptop', 'phone', 'iot', 'camera', 'printer'].includes(n.kind));
    const wan = [from.kind, to.kind].includes('internet');
    const link = normalizeLink({ kind: wireless ? 'wifi' : wan ? 'wan' : 'eth', ...extra, id: uid('l'), a, b });
    this.map.links.push(link);
    this.emit('map');
    return link;
  }

  addZone(rect, extra = {}) {
    if (!this.begin()) return null;
    const used = new Set(this.map.zones.map((z) => z.tone));
    const tone = ['Blue', 'Teal', 'Purple', 'Green', 'Yellow', 'Pink', 'Gray', 'Red'].find((t) => !used.has(t)) ?? 'Blue';
    const zone = normalizeZone({ name: `Zona ${this.map.zones.length + 1}`, tone, ...extra, id: uid('z'), x: snap(rect.x), y: snap(rect.y), w: snap(rect.w), h: snap(rect.h) });
    // le zone stanno sotto: le piu' grandi in fondo, cosi' una zona dentro l'altra resta cliccabile
    this.map.zones.push(zone);
    this.map.zones.sort((p, q) => q.w * q.h - p.w * p.h);
    this.emit('map');
    return zone;
  }

  /** Una zona attorno agli apparati selezionati, con un po' d'aria. */
  zoneAround(ids) {
    const box = boundsOf(this.map, new Set(ids));
    if (!box) return null;
    const pad = 32;
    return this.addZone({ x: box.x - pad, y: box.y - pad - 24, w: box.w + pad * 2, h: box.h + pad * 2 + 24 });
  }

  update(id, patch, key) {
    const item = this.find(id);
    if (!item) return;
    const changed = Object.entries(patch).some(([field, value]) => item[field] !== value);
    if (!changed || !this.begin(key ?? `${id}:${Object.keys(patch).join(',')}`)) return;
    const type = this.typeOf(id);
    const next = type === 'node' ? normalizeNode({ ...item, ...patch }) : type === 'link' ? normalizeLink({ ...item, ...patch }) : normalizeZone({ ...item, ...patch });
    for (const field of Object.keys(patch)) item[field] = next[field];
    if (type === 'zone') this.map.zones.sort((p, q) => q.w * q.h - p.w * p.h);
    this.emit('map');
  }

  /** Sposta apparati e zone (in coordinate del mondo), gia' fotografati da chi trascina. */
  moveTo(positions) {
    for (const [id, { x, y }] of positions) {
      const item = this.find(id);
      if (item) {
        item.x = Math.round(x);
        item.y = Math.round(y);
      }
    }
    this.emit('map');
  }

  nudge(dx, dy) {
    const ids = [...this.sel].filter((id) => ['node', 'zone'].includes(this.typeOf(id)));
    if (ids.length === 0 || !this.begin('nudge')) return;
    const moving = new Set(ids);
    for (const zone of this.selected('zone')) for (const node of membersOf(this.map, zone)) moving.add(node.id);
    for (const id of moving) {
      const item = this.find(id);
      item.x += dx;
      item.y += dy;
    }
    this.emit('map');
  }

  remove(ids = [...this.sel]) {
    const gone = new Set(ids);
    if (gone.size === 0 || !this.begin()) return 0;
    const map = this.map;
    const before = map.nodes.length + map.links.length + map.zones.length;
    map.nodes = map.nodes.filter((n) => !gone.has(n.id));
    const alive = new Set(map.nodes.map((n) => n.id));
    map.links = map.links.filter((l) => !gone.has(l.id) && alive.has(l.a) && alive.has(l.b));
    map.zones = map.zones.filter((z) => !gone.has(z.id));
    for (const id of gone) this.sel.delete(id);
    this.emit('map');
    this.emit('select');
    return before - (map.nodes.length + map.links.length + map.zones.length);
  }

  swapEnds(id) {
    const link = this.map.links.find((l) => l.id === id);
    if (!link || !this.begin()) return;
    [link.a, link.b, link.pa, link.pb] = [link.b, link.a, link.pb, link.pa];
    this.emit('map');
  }

  // ------------------------------------------------------------------ copiare e incollare

  /** Quello che e' selezionato, pronto per gli appunti (con i cavi fra gli apparati scelti). */
  fragment(ids = [...this.sel]) {
    const set = new Set(ids);
    const map = this.map;
    for (const zone of map.zones) if (set.has(zone.id)) for (const node of membersOf(map, zone)) set.add(node.id);
    const nodes = map.nodes.filter((n) => set.has(n.id));
    const kept = new Set(nodes.map((n) => n.id));
    const links = map.links.filter((l) => kept.has(l.a) && kept.has(l.b));
    const zones = map.zones.filter((z) => set.has(z.id));
    if (nodes.length + zones.length === 0) return null;
    return { ducknoteMap: 1, nodes, links, zones };
  }

  /** Incolla un pezzo di mappa spostato di (dx, dy), con identificativi nuovi; lo seleziona. */
  paste(fragment, dx, dy) {
    if (!fragment || !this.begin()) return [];
    const ids = new Map();
    const fresh = (old, prefix) => {
      const id = uid(prefix);
      ids.set(old, id);
      return id;
    };
    const added = [];
    for (const raw of fragment.nodes ?? []) {
      const node = normalizeNode({ ...raw, id: fresh(raw.id, 'n'), x: (raw.x ?? 0) + dx, y: (raw.y ?? 0) + dy });
      this.map.nodes.push(node);
      added.push(node.id);
    }
    for (const raw of fragment.links ?? []) {
      if (!ids.has(raw.a) || !ids.has(raw.b)) continue;
      this.map.links.push(normalizeLink({ ...raw, id: uid('l'), a: ids.get(raw.a), b: ids.get(raw.b) }));
    }
    for (const raw of fragment.zones ?? []) {
      const zone = normalizeZone({ ...raw, id: fresh(raw.id, 'z'), x: (raw.x ?? 0) + dx, y: (raw.y ?? 0) + dy });
      this.map.zones.push(zone);
      added.push(zone.id);
    }
    this.map.zones.sort((p, q) => q.w * q.h - p.w * p.h);
    this.sel = new Set(added);
    this.emit('map');
    this.emit('select');
    return added;
  }

  // ------------------------------------------------------------------ host della rete

  /**
   * list: [{ key, ip, name, ..., groups: [id] }]; groups: [{ id, name, monitor }], i gruppi di
   * scansione della nota (senza "nota", che c'e' sempre). Senza groups restano quelli di prima.
   */
  setHosts(list, groups) {
    this.hosts = Array.isArray(list) ? list.filter((h) => h && typeof h === 'object' && (h.key || h.ip)) : [];
    if (Array.isArray(groups)) {
      this.groups = groups.filter((g) => g && typeof g.id === 'string' && g.id && g.id !== 'nota')
        .map((g) => ({ id: g.id, name: String(g.name ?? '') || 'Gruppo', monitor: !!g.monitor }));
    }
    this.states.clear();
    for (const host of this.hosts) {
      for (const name of [host.key, host.ip, host.name]) {
        const key = hostKey(name);
        if (key && !this.states.has(key)) this.states.set(key, host);
      }
    }
    this.emit('hosts');
  }

  /** L'host della scansione che corrisponde all'apparato (per indirizzo o per nome). */
  hostOf(node) {
    const key = hostKey(node?.host);
    return key ? this.states.get(key) ?? null : null;
  }

  /** up, down, wait (da verificare), unknown (mai provato), none (senza indirizzo). */
  stateOf(node) {
    if (!node?.host) return 'none';
    const host = this.hostOf(node);
    return host ? host.state ?? 'unknown' : 'unknown';
  }

  /** Gli host gia' sulla mappa aperta, per chiave. */
  placedHosts() {
    const placed = new Map();
    for (const node of this.map.nodes) {
      const key = hostKey(node.host);
      if (!key) continue;
      placed.set(key, node);
      const host = this.states.get(key);
      for (const other of [host?.key, host?.ip, host?.name]) if (other) placed.set(hostKey(other), node);
    }
    return placed;
  }

  /** Un apparato dalla scansione: tipo, nome, indirizzo, MAC e marca sono gia' quelli trovati. */
  nodeFromHost(host, x, y) {
    return this.addNode(kindFromScan(host), x, y, {
      name: this.freeNodeName(nameFromScan(host) || 'Host'),
      host: host.key || host.ip,
      mac: host.mac ?? '',
      model: host.vendor ?? '',
    });
  }

  /**
   * Tutti gli host trovati e non ancora sulla mappa, in un colpo: ognuno nella zona della sua
   * sottorete se c'e', altrimenti in righe sotto quello che c'e' gia'. Un passo solo di Annulla.
   */
  addHosts(hosts, origin) {
    const placed = this.placedHosts();
    const fresh = hosts.filter((host) => !placed.has(hostKey(host.key || host.ip)));
    if (fresh.length === 0 || !this.begin()) return [];
    const added = [];
    const map = this.map;
    const box = boundsOf(map);
    const columns = Math.max(3, Math.min(8, Math.ceil(Math.sqrt(fresh.length * 1.6))));
    let free = 0;
    const slots = new Map();
    for (const host of fresh) {
      const ip = isIpv4(host.ip) ? host.ip : isIpv4(host.key) ? host.key : '';
      const zone = ip ? map.zones.find((z) => z.cidr && inCidr(ip, z.cidr)) : null;
      let x;
      let y;
      if (zone) {
        const used = slots.get(zone.id) ?? membersOf(map, zone).length;
        slots.set(zone.id, used + 1);
        const perRow = Math.max(1, Math.floor((zone.w - 32) / (NODE_W + 24)));
        x = zone.x + 16 + (used % perRow) * (NODE_W + 24);
        y = zone.y + 48 + Math.floor(used / perRow) * (NODE_H + 28);
      } else {
        const left = box ? box.x : origin.x;
        const top = box ? box.y + box.h + 64 : origin.y;
        x = left + (free % columns) * (NODE_W + 32);
        y = top + Math.floor(free / columns) * (NODE_H + 40);
        free++;
      }
      const node = normalizeNode({
        kind: kindFromScan(host),
        name: nameFromScan(host) || 'Host',
        host: host.key || host.ip,
        mac: host.mac ?? '',
        model: host.vendor ?? '',
        id: uid('n'),
        x: snap(x),
        y: snap(y),
      });
      map.nodes.push(node);
      added.push(node.id);
    }
    this.sel = new Set(added);
    this.emit('map');
    this.emit('select');
    return added;
  }
}

export function snap(value, step = GRID) {
  return Math.round(value / step) * step;
}

export { centerOf, membersOf, boundsOf };
