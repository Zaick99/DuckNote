// "Disponi": la rete in piedi, dall'alto in basso. Si parte dall'apparato piu' "a monte" di
// ogni gruppo collegato (Internet, poi firewall, router, switch...) e si scende lungo i cavi:
// ogni apparato sta sopra a quelli che gli dipendono, centrato. Le foglie numerose (i PC di uno
// switch) vanno in una griglia sotto al loro apparato invece che in una riga lunghissima. Gli
// apparati senza cavi finiscono in una griglia in fondo.

import { sizeOf } from './model.js';

const RANK = { internet: 0, firewall: 1, router: 2, switch: 3, ap: 4, server: 5, nas: 5, pc: 6, laptop: 6, phone: 6, printer: 6, camera: 6, iot: 6, other: 7 };
const GAP_X = 48;
const GAP_Y = 88;
const LEAF_GAP_Y = 28;
const COMPONENT_GAP = 96;

const rank = (node) => RANK[node.kind] ?? 7;
const byRank = (a, b) => rank(a) - rank(b) || a.name.localeCompare(b.name, 'it', { numeric: true });

/** Le nuove posizioni (angolo in alto a sinistra) degli apparati dati, o di tutti. */
export function arrange(map, only = null) {
  const nodes = map.nodes.filter((node) => !only || only.has(node.id));
  if (nodes.length < 2) return null;
  const byId = new Map(nodes.map((node) => [node.id, node]));
  const near = new Map(nodes.map((node) => [node.id, []]));
  for (const link of map.links) {
    if (byId.has(link.a) && byId.has(link.b) && link.a !== link.b) {
      near.get(link.a).push(link.b);
      near.get(link.b).push(link.a);
    }
  }

  // i gruppi collegati, ognuno col suo albero (in ampiezza dalla radice)
  const seen = new Set();
  const trees = [];
  const loose = [];
  const roots = [...nodes].sort((a, b) => byRank(a, b) || near.get(b.id).length - near.get(a.id).length);
  for (const root of roots) {
    if (seen.has(root.id)) continue;
    if (near.get(root.id).length === 0) {
      seen.add(root.id);
      loose.push(root);
      continue;
    }
    const children = new Map();
    const queue = [root.id];
    seen.add(root.id);
    while (queue.length) {
      const id = queue.shift();
      const kids = near.get(id).filter((other) => !seen.has(other)).map((other) => byId.get(other));
      const unique = [...new Map(kids.map((kid) => [kid.id, kid])).values()].sort(byRank);
      for (const kid of unique) seen.add(kid.id);
      children.set(id, unique.map((kid) => kid.id));
      queue.push(...unique.map((kid) => kid.id));
    }
    trees.push({ root: root.id, children });
  }

  const positions = new Map();
  let x = 0;
  let bottom = 0;
  for (const tree of trees) {
    const part = block(tree.root, tree.children, byId);
    part.place(x, 0, positions);
    x += part.w + COMPONENT_GAP;
    bottom = Math.max(bottom, part.h);
  }
  if (loose.length) {
    const grid = gridBlock(loose.sort(byRank).map((node) => node.id), byId);
    const width = Math.max(x - COMPONENT_GAP, grid.w);
    grid.place(trees.length ? (width - grid.w) / 2 : 0, trees.length ? bottom + GAP_Y * 1.5 : 0, positions);
  }
  return positions;
}

/** Il blocco di un apparato e di tutto quello che gli sta sotto: misura e posa. */
function block(id, children, byId) {
  const kids = children.get(id) ?? [];
  const size = sizeOf(byId.get(id));
  if (kids.length === 0) {
    return { w: size.w, h: size.h, place: (x, y, out) => out.set(id, { x, y }) };
  }
  const branches = kids.filter((kid) => (children.get(kid) ?? []).length > 0).map((kid) => block(kid, children, byId));
  const leaves = kids.filter((kid) => (children.get(kid) ?? []).length === 0);
  const parts = [...branches];
  if (leaves.length > 4) parts.push(gridBlock(leaves, byId));
  else for (const leaf of leaves) parts.push(block(leaf, children, byId));

  const inner = parts.reduce((sum, part) => sum + part.w, 0) + GAP_X * (parts.length - 1);
  const w = Math.max(size.w, inner);
  const h = size.h + GAP_Y + Math.max(...parts.map((part) => part.h));
  return {
    w,
    h,
    place: (x, y, out) => {
      out.set(id, { x: x + (w - size.w) / 2, y });
      let cx = x + (w - inner) / 2;
      for (const part of parts) {
        part.place(cx, y + size.h + GAP_Y, out);
        cx += part.w + GAP_X;
      }
    },
  };
}

/** Una griglia di apparati (le foglie di uno switch, gli apparati senza cavi): caselle grandi quanto la scheda piu' grande. */
function gridBlock(ids, byId) {
  const columns = Math.max(1, Math.min(6, Math.ceil(Math.sqrt(ids.length * 1.5))));
  const rows = Math.ceil(ids.length / columns);
  const sizes = ids.map((id) => sizeOf(byId.get(id)));
  const cellW = Math.max(...sizes.map((size) => size.w));
  const cellH = Math.max(...sizes.map((size) => size.h));
  const w = columns * cellW + (columns - 1) * (GAP_X / 2);
  const h = rows * cellH + (rows - 1) * LEAF_GAP_Y;
  return {
    w,
    h,
    place: (x, y, out) => {
      ids.forEach((id, index) => {
        const column = index % columns;
        const row = Math.floor(index / columns);
        out.set(id, { x: x + column * (cellW + GAP_X / 2), y: y + row * (cellH + LEAF_GAP_Y) });
      });
    },
  };
}
