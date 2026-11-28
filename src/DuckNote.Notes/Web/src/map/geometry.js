// Dove passano i cavi: ad angolo retto, da lato a lato delle schede. Un cavo esce dal lato
// rivolto verso l'altro apparato; su uno switch (router, firewall) con le porte esce dalla
// colonna della sua porta, sugli altri apparati i cavi dello stesso lato si dividono il lato in
// ordine, perche' non si incrocino appena usciti. Il gomito gira a meta' strada, o dove non
// attraversa altre schede e non corre sopra a un altro cavo.

import { rectOf, bodyOf, portCell, portOf, zeroBased, physical } from './model.js';

/** Quanto stanno lontani dagli angoli i capi distribuiti su un lato. */
const EDGE_PAD = 14;
/** L'aria che un cavo lascia attorno alle schede che non sono le sue. */
const AIR = 8;
/** Il passo delle corsie provate per il gomito. */
const LANE = 8;
/** Due cavi paralleli a meno di cosi' si confondono. */
const CLOSE = 5;
/** Quanto costa, nella scelta del gomito, attraversare una scheda, incrociare un cavo, correre sopra a un altro. */
const COST_BOX = 10;
const COST_CROSS = 4;
const COST_BUSY = 9;

/** I lati da cui un cavo esce da a e entra in b; null se le due schede si toccano. */
function sidesOf(ra, rb) {
  if (rb.y >= ra.y + ra.h + 10) return ['bottom', 'top'];
  if (rb.y + rb.h <= ra.y - 10) return ['top', 'bottom'];
  if (rb.x >= ra.x + ra.w + 10) return ['right', 'left'];
  if (rb.x + rb.w <= ra.x - 10) return ['left', 'right'];
  return null;
}

const vertical = (side) => side === 'top' || side === 'bottom';

/** Il punto di un lato del corpo della scheda alla posizione data (x per sopra/sotto, y per i fianchi). */
function onSide(body, side, at) {
  switch (side) {
    case 'top': return { x: at, y: body.y };
    case 'bottom': return { x: at, y: body.y + body.h };
    case 'left': return { x: body.x, y: at };
    default: return { x: body.x + body.w, y: at };
  }
}

/** Le caselle della mappa con le schede: per sapere in fretta cosa c'e' vicino a un segmento. */
class Boxes {
  constructor(rects) {
    this.cell = 160;
    this.cells = new Map();
    for (const rect of rects) {
      const r = { ...rect, x: rect.x - AIR, y: rect.y - AIR, w: rect.w + AIR * 2, h: rect.h + AIR * 2 };
      for (let x = Math.floor(r.x / this.cell); x <= Math.floor((r.x + r.w) / this.cell); x++) {
        for (let y = Math.floor(r.y / this.cell); y <= Math.floor((r.y + r.h) / this.cell); y++) {
          const key = x * 1_000_003 + y;
          if (!this.cells.has(key)) this.cells.set(key, []);
          this.cells.get(key).push(r);
        }
      }
    }
  }

  /** Quante schede (non quelle dei due capi) attraversa il segmento dritto da p a q. */
  crossings(p, q, skip) {
    const x0 = Math.min(p.x, q.x);
    const x1 = Math.max(p.x, q.x);
    const y0 = Math.min(p.y, q.y);
    const y1 = Math.max(p.y, q.y);
    const seen = new Set();
    let count = 0;
    for (let x = Math.floor(x0 / this.cell); x <= Math.floor(x1 / this.cell); x++) {
      for (let y = Math.floor(y0 / this.cell); y <= Math.floor(y1 / this.cell); y++) {
        for (const r of this.cells.get(x * 1_000_003 + y) ?? []) {
          if (seen.has(r) || skip.has(r.id)) continue;
          seen.add(r);
          if (x1 > r.x && x0 < r.x + r.w && y1 > r.y && y0 < r.y + r.h) count++;
        }
      }
    }
    return count;
  }
}

/**
 * I tratti gia' tracciati: per non mettere due gomiti uno sopra l'altro e per contare quanti
 * cavi incrocia una strada nuova. Con piu' cavi che escono dallo stesso lato (uno switch e i
 * suoi PC) vince cosi' la forma a L annidate: chi va piu' lontano gira piu' vicino all'apparato.
 */
class Lanes {
  constructor() {
    this.across = [];
    this.along = [];
    this.segments = [];
  }

  /** Quanti tratti gia' tracciati incrocia la spezzata (non contano i capi che si toccano). */
  crossings(points) {
    let count = 0;
    for (let i = 1; i < points.length; i++) {
      const p = points[i - 1];
      const q = points[i];
      const horizontal = Math.abs(p.y - q.y) < 0.5;
      for (const seg of this.segments) {
        if (seg.horizontal === horizontal) continue;
        const [h, v] = horizontal ? [{ p, q }, seg] : [seg, { p, q }];
        const y = h.p.y;
        const x = v.p.x;
        const inH = x > Math.min(h.p.x, h.q.x) + 0.5 && x < Math.max(h.p.x, h.q.x) - 0.5;
        const inV = y > Math.min(v.p.y, v.q.y) + 0.5 && y < Math.max(v.p.y, v.q.y) - 0.5;
        if (inH && inV) count++;
      }
    }
    return count;
  }

  trace(points) {
    for (let i = 1; i < points.length; i++) {
      const p = points[i - 1];
      const q = points[i];
      this.segments.push({ p, q, horizontal: Math.abs(p.y - q.y) < 0.5 });
    }
  }

  /** Il tratto orizzontale a quota y fra x0 e x1 (o verticale a x fra y0 e y1) corre sopra a un altro? */
  busy(horizontal, at, from, to) {
    const list = horizontal ? this.across : this.along;
    const lo = Math.min(from, to);
    const hi = Math.max(from, to);
    return list.some((lane) => Math.abs(lane.at - at) < CLOSE && Math.min(hi, lane.hi) - Math.max(lo, lane.lo) > 2);
  }

  add(horizontal, at, from, to) {
    (horizontal ? this.across : this.along).push({ at, lo: Math.min(from, to), hi: Math.max(from, to) });
  }
}

/** I tratti di una spezzata, con direzione e normale: le etichette ci si appoggiano. */
export function segmentsOf(points) {
  const out = [];
  for (let i = 1; i < points.length; i++) {
    const p = points[i - 1];
    const q = points[i];
    const dx = q.x - p.x;
    const dy = q.y - p.y;
    const length = Math.hypot(dx, dy);
    if (length < 0.5) continue;
    const ux = dx / length;
    const uy = dy / length;
    out.push({ p, q, ux, uy, nx: -uy, ny: ux, length, mid: { x: (p.x + q.x) / 2, y: (p.y + q.y) / 2 } });
  }
  return out;
}

/**
 * Le strade di tutti i cavi della mappa. tabEnd(node): fin dove arriva la linguetta (i cavi
 * dall'alto entrano a destra di lei). quick: durante un trascinamento i gomiti stanno a meta'
 * strada, senza cercare posto. Torna, per cavo, { points, segs, ends } (null se le schede si
 * sovrappongono): ends.a e ends.b dicono lato, punto e porta di ciascun capo.
 */
export function cableRoutes(map, { tabEnd = () => 0, quick = false } = {}) {
  const nodes = new Map(map.nodes.map((node) => [node.id, node]));
  const bodies = new Map(map.nodes.map((node) => [node.id, bodyOf(node)]));
  const routes = new Map();
  const pending = [];
  const buckets = new Map();
  const zero = zeroBased(map);

  // i lati e, dove c'e' la porta giusta, il punto; gli altri capi aspettano il loro turno
  for (const link of map.links) {
    const a = nodes.get(link.a);
    const b = nodes.get(link.b);
    if (!a || !b || a === b) {
      routes.set(link.id, null);
      continue;
    }
    const ra = bodies.get(a.id);
    const rb = bodies.get(b.id);
    const sides = sidesOf(ra, rb);
    if (!sides) {
      routes.set(link.id, null);
      continue;
    }
    const entry = { link, a, b, ends: { a: { node: a, side: sides[0], point: null, port: null, anchored: false }, b: { node: b, side: sides[1], point: null, port: null, anchored: false } } };
    for (const [end, node, other, label] of [['a', a, b, link.pa], ['b', b, a, link.pb]]) {
      const info = entry.ends[end];
      const named = physical(link) ? portOf(node, label, zero.get(node.id)) : null;
      const cell = named ? portCell(node, named.n, named.kind) : null;
      // la porta si accende comunque sul pannello; il cavo ci si attacca se esce da sopra o da sotto
      if (cell) info.port = { number: named.n, kind: named.kind, key: cell.key, cell };
      if (cell && vertical(info.side)) {
        info.point = onSide(bodies.get(node.id), info.side, node.x + cell.ax);
        info.anchored = true;
        continue;
      }
      const key = `${node.id}:${info.side}`;
      if (!buckets.has(key)) buckets.set(key, []);
      const o = rectOf(other);
      buckets.get(key).push({ info, toward: vertical(info.side) ? o.x + o.w / 2 : o.y + o.h / 2 });
    }
    pending.push(entry);
  }

  // i capi senza porta si dividono il loro lato, in ordine di dove va l'altro capo
  for (const [key, list] of buckets) {
    const node = nodes.get(key.slice(0, key.lastIndexOf(':')));
    const side = key.slice(key.lastIndexOf(':') + 1);
    const body = bodies.get(node.id);
    list.sort((p, q) => p.toward - q.toward);
    let from;
    let to;
    if (vertical(side)) {
      from = body.x + EDGE_PAD;
      to = body.x + body.w - EDGE_PAD;
      // dall'alto si entra a destra della linguetta, se c'e' posto
      const tab = side === 'top' ? node.x + tabEnd(node) + 10 : from;
      if (to - tab >= 24) from = Math.max(from, tab);
    } else {
      from = body.y + 12;
      to = body.y + body.h - 12;
    }
    const middle = vertical(side) ? body.x + body.w / 2 : body.y + body.h / 2;
    list.forEach((item, index) => {
      const at = list.length === 1
        ? Math.min(to, Math.max(from, middle))
        : from + ((index + 1) * (to - from)) / (list.length + 1);
      item.info.point = onSide(body, side, at);
    });
  }

  // le strade: dritte se i capi sono allineati, altrimenti con un gomito dove c'e' posto
  const boxes = quick ? null : new Boxes(map.nodes.map((node) => ({ ...rectOf(node), id: node.id })));
  const lanes = new Lanes();
  for (const entry of pending) {
    const { a, b, ends } = entry;
    const pa = ends.a.point;
    const pb = ends.b.point;
    const skip = new Set([a.id, b.id]);
    const down = vertical(ends.a.side);
    let points;
    if (down ? Math.abs(pa.x - pb.x) < 0.5 : Math.abs(pa.y - pb.y) < 0.5) {
      points = [pa, pb];
    } else {
      const start = down ? pa.y : pa.x;
      const end = down ? pb.y : pb.x;
      const middle = Math.round((start + end) / 2);
      const lo = Math.min(start, end) + 10;
      const hi = Math.max(start, end) - 10;
      const tries = [middle];
      if (!quick) {
        for (let d = LANE; d <= Math.max(0, (hi - lo) / 2); d += LANE) tries.push(middle - d, middle + d);
      }
      const bend = (at) => (down
        ? [pa, { x: pa.x, y: at }, { x: pb.x, y: at }, pb]
        : [pa, { x: at, y: pa.y }, { x: at, y: pb.y }, pb]);
      let best = null;
      for (const at of tries) {
        if (tries.length > 1 && (at < lo || at > hi)) continue;
        const candidate = bend(at);
        let cost = 0;
        if (boxes) {
          for (let i = 1; i < candidate.length; i++) cost += boxes.crossings(candidate[i - 1], candidate[i], skip) * COST_BOX;
          cost += lanes.crossings(candidate) * COST_CROSS;
        }
        if (lanes.busy(down, at, down ? pa.x : pa.y, down ? pb.x : pb.y)) cost += COST_BUSY;
        cost += Math.abs(at - middle) / 200;
        if (!best || cost < best.cost) best = { at, points: candidate, cost };
        if (cost < 0.05) break;
      }
      points = best.points;
      lanes.add(down, best.at, down ? pa.x : pa.y, down ? pb.x : pb.y);
    }
    if (boxes) lanes.trace(points);
    const segs = segmentsOf(points);
    if (segs.length === 0) {
      routes.set(entry.link.id, null);
      continue;
    }
    routes.set(entry.link.id, { points, segs, ends });
  }
  return routes;
}

/** Distanza di un punto da un segmento (per capire se un clic e' su un cavo). */
export function distanceToSegment(point, p, q) {
  const dx = q.x - p.x;
  const dy = q.y - p.y;
  const length = dx * dx + dy * dy;
  const t = length ? Math.max(0, Math.min(1, ((point.x - p.x) * dx + (point.y - p.y) * dy) / length)) : 0;
  return Math.hypot(point.x - (p.x + t * dx), point.y - (p.y + t * dy));
}

export const intersects = (a, b) => a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y;

export function normalizeRect(x0, y0, x1, y1) {
  return { x: Math.min(x0, x1), y: Math.min(y0, y1), w: Math.abs(x1 - x0), h: Math.abs(y1 - y0) };
}
