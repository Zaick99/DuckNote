// Dove vanno le etichette dei cavi (le porte vicino ai capi, velocita' e VLAN a meta') perche'
// si leggano tutte. Ognuna cerca il primo posto libero lungo il suo cavo e, se serve, di fianco;
// evita gli apparati, le testate delle zone e le etichette gia' messe. Se proprio non c'e'
// posto (cavi cortissimi, apparati ammucchiati) va dove copre meno: resta comunque visibile.

import { NODE_W, NODE_H } from './model.js';
import { measure } from '../common/dom.js';

export const PORT_HEIGHT = 16;
export const MID_HEIGHT = 18;
const CELL = 64;
const STEP = 5;
const GAP = 3;

/** Gli ingombri gia' sulla mappa, divisi in caselle: si confronta solo con i vicini. */
class Room {
  constructor() {
    this.cells = new Map();
    this.stamp = 0;
  }

  add(rect, weight) {
    const item = { x: rect.x, y: rect.y, w: rect.w, h: rect.h, weight, seen: 0 };
    const x0 = Math.floor(rect.x / CELL);
    const x1 = Math.floor((rect.x + rect.w) / CELL);
    const y0 = Math.floor(rect.y / CELL);
    const y1 = Math.floor((rect.y + rect.h) / CELL);
    for (let x = x0; x <= x1; x++) {
      for (let y = y0; y <= y1; y++) {
        const key = x * 1_000_003 + y;
        let list = this.cells.get(key);
        if (!list) this.cells.set(key, (list = []));
        list.push(item);
      }
    }
  }

  /** Quanto il rettangolo copre di quello che c'e' gia' (0: e' libero); oltre `limit` si smette di contare. */
  cost(rect, limit = Infinity) {
    const stamp = ++this.stamp;
    let total = 0;
    const x0 = Math.floor(rect.x / CELL);
    const x1 = Math.floor((rect.x + rect.w) / CELL);
    const y0 = Math.floor(rect.y / CELL);
    const y1 = Math.floor((rect.y + rect.h) / CELL);
    for (let x = x0; x <= x1; x++) {
      for (let y = y0; y <= y1; y++) {
        const list = this.cells.get(x * 1_000_003 + y);
        if (!list) continue;
        for (let i = 0; i < list.length; i++) {
          const item = list[i];
          if (item.seen === stamp) continue;
          item.seen = stamp;
          const w = Math.min(rect.x + rect.w, item.x + item.w) - Math.max(rect.x, item.x);
          const h = Math.min(rect.y + rect.h, item.y + item.h) - Math.max(rect.y, item.y);
          if (w > 0 && h > 0) {
            total += w * h * item.weight;
            if (total > limit) return total;
          }
        }
      }
    }
    return total;
  }
}

const box = (center, size) => ({ x: center.x - size.w / 2 - GAP, y: center.y - size.h / 2 - GAP, w: size.w + GAP * 2, h: size.h + GAP * 2 });

/** Fra i posti possibili (gia' in ordine di preferenza) il primo libero, o quello che copre meno. */
function choose(room, candidates, size, budget) {
  let best = null;
  let tried = 0;
  for (const candidate of candidates) {
    if (++tried > budget) break;
    // un posto che costa gia' piu' del migliore trovato non serve contarlo fino in fondo
    const limit = best ? (best.score - candidate.shift) / 4 : Infinity;
    if (limit <= 0) continue;
    const cost = room.cost(box(candidate.at, size), limit);
    if (cost === 0) return candidate;
    const score = cost * 4 + candidate.shift;
    if (!best || score < best.score) best = { ...candidate, score };
  }
  return best;
}

/**
 * Le corsie di un'etichetta lungo un cavo: partendo da `origin` nel verso dato, sulla linea,
 * appena di fianco, o una o due etichette piu' in la' (per i cavi cortissimi e gli incroci
 * affollati: allora un filo la lega al suo cavo). Ogni corsia ha il suo costo: scorrere lungo il
 * cavo costa meno che allontanarsene.
 */
function lanes(path, origin, direction, size, start, end, stride) {
  const half = Math.abs(path.nx) * size.w / 2 + Math.abs(path.ny) * size.h / 2;
  const step = half * 2 + 6;
  const count = Math.max(1, Math.floor((end - start) / stride) + 1);
  return [[0, 0], [half + 4, 14], [-(half + 4), 14], [half + 4 + step, 70], [-(half + 4 + step), 70], [half + 4 + step * 2, 120], [-(half + 4 + step * 2), 120]]
    .map(([offset, cost]) => ({ origin, direction, start, offset, cost, count, lead: Math.abs(offset) > half + 5 }));
}

/**
 * I posti delle corsie dal piu' vicino al posto giusto al piu' lontano, uno alla volta: quasi
 * sempre il primo e' libero, e gli altri non si calcolano nemmeno.
 */
function* candidates(path, list, stride) {
  const next = new Array(list.length).fill(0);
  for (;;) {
    let pick = -1;
    let low = Infinity;
    for (let i = 0; i < list.length; i++) {
      if (next[i] >= list[i].count) continue;
      const shift = list[i].cost + next[i] * stride;
      if (shift < low) {
        low = shift;
        pick = i;
      }
    }
    if (pick < 0) return;
    const lane = list[pick];
    const d = lane.start + next[pick]++ * stride;
    const anchor = { x: lane.origin.x + path.ux * lane.direction * d, y: lane.origin.y + path.uy * lane.direction * d };
    yield { at: { x: anchor.x + path.nx * lane.offset, y: anchor.y + path.ny * lane.offset }, anchor, lead: lane.lead, shift: low };
  }
}

/** Quanto un'etichetta si allunga lungo il cavo, dal suo centro. */
const alongOf = (path, size) => Math.abs(path.ux) * size.w / 2 + Math.abs(path.uy) * size.h / 2;

/** La misura di un'etichetta: il testo in monospazio e un po' d'aria ai lati. */
export function chipSize(text, height, font) {
  return { w: Math.ceil(measure(text, font)) + 12, h: height };
}

/**
 * Le etichette di tutti i cavi. entries: [{ link, path, pa, pb, mid }] con i testi gia' pronti
 * (o vuoti); obstacles: i rettangoli da non coprire (le testate delle zone). Torna, per cavo,
 * { a, b, mid }: per ogni etichetta il centro (at), il punto del cavo a cui appartiene (anchor) e
 * se serve il filo che ve la lega (lead); null se l'etichetta non c'e'.
 */
export function placeLabels(nodes, entries, obstacles, font, { quick = false } = {}) {
  // mentre si trascina basta un posto buono fra i primi: quello migliore arriva quando si lascia
  const budget = quick ? 24 : Infinity;
  const room = new Room();
  for (const node of nodes) room.add({ x: node.x - 2, y: node.y - 2, w: NODE_W + 4, h: NODE_H + 4 }, 6);
  for (const rect of obstacles) room.add(rect, 3);
  const placed = new Map();
  for (const entry of entries) placed.set(entry.link.id, { a: null, b: null, mid: null });
  const keep = (entry, key, spot, size) => {
    if (!spot) return;
    placed.get(entry.link.id)[key] = { at: spot.at, anchor: spot.anchor, lead: spot.lead };
    room.add(box(spot.at, size), 2);
  };

  // prima le porte: stanno dove i cavi si affollano (attorno agli switch), hanno meno scelta
  for (const entry of entries) {
    const { path } = entry;
    if (!path) continue;
    for (const end of ['a', 'b']) {
      const text = end === 'a' ? entry.pa : entry.pb;
      if (!text) continue;
      const size = chipSize(text, PORT_HEIGHT, font);
      const along = alongOf(path, size);
      const near = along + 4;
      const far = Math.max(near, Math.min(path.length / 2 - along, near + 110));
      // sui cavi lunghi i passi si allargano: al massimo una ventina di posti per corsia
      const stride = Math.max(STEP, (far - near) / 20);
      const list = lanes(path, end === 'a' ? path.p : path.q, end === 'a' ? 1 : -1, size, near, far, stride);
      keep(entry, end, choose(room, candidates(path, list, stride), size, budget), size);
    }
  }

  // poi velocita' e VLAN, attorno al mezzo del cavo
  for (const entry of entries) {
    const { path } = entry;
    if (!path || !entry.mid) continue;
    const size = chipSize(entry.mid, MID_HEIGHT, font);
    const reach = Math.max(0, path.length * 0.36 - alongOf(path, size) / 2);
    const stride = Math.max(STEP, reach / 20);
    const list = [...lanes(path, path.mid, 1, size, 0, reach, stride), ...lanes(path, path.mid, -1, size, stride, reach, stride)];
    keep(entry, 'mid', choose(room, candidates(path, list, stride), size, budget), size);
  }
  return placed;
}
