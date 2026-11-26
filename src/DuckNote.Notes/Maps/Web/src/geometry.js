// Dove passano i cavi: da bordo a bordo degli apparati, e piu' cavi fra gli stessi due apparati
// (un aggregato, un collegamento di riserva) corrono paralleli invece di sovrapporsi.

import { NODE_W, NODE_H } from './model.js';

const PARALLEL = 10;

/** Dove il segmento dal centro di `rect` verso `to` esce dal rettangolo. */
function exit(from, to, rect) {
  const dx = to.x - from.x;
  const dy = to.y - from.y;
  let t = 1;
  if (dx > 0) t = Math.min(t, (rect.x + rect.w - from.x) / dx);
  if (dx < 0) t = Math.min(t, (rect.x - from.x) / dx);
  if (dy > 0) t = Math.min(t, (rect.y + rect.h - from.y) / dy);
  if (dy < 0) t = Math.min(t, (rect.y - from.y) / dy);
  t = Math.max(0, t);
  return { x: from.x + dx * t, y: from.y + dy * t };
}

/** Per ogni cavo, il suo posto fra quelli che uniscono la stessa coppia di apparati. */
export function bundles(links) {
  const groups = new Map();
  for (const link of links) {
    const key = link.a < link.b ? `${link.a}|${link.b}` : `${link.b}|${link.a}`;
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(link);
  }
  const place = new Map();
  for (const group of groups.values()) {
    group.forEach((link, index) => place.set(link.id, { index, count: group.length }));
  }
  return place;
}

/**
 * La strada di un cavo: i due capi sul bordo degli apparati, la direzione e la normale (per le
 * etichette), la lunghezza visibile. Null se gli apparati si sovrappongono.
 */
export function linkPath(a, b, index = 0, count = 1) {
  const ca = { x: a.x + NODE_W / 2, y: a.y + NODE_H / 2 };
  const cb = { x: b.x + NODE_W / 2, y: b.y + NODE_H / 2 };
  const dx = cb.x - ca.x;
  const dy = cb.y - ca.y;
  const length = Math.hypot(dx, dy);
  if (length < 1) return null;
  const ux = dx / length;
  const uy = dy / length;
  // la normale sempre dallo stesso lato, qualunque sia il verso del cavo
  const flip = a.id < b.id ? 1 : -1;
  const nx = -uy * flip;
  const ny = ux * flip;
  const offset = (index - (count - 1) / 2) * PARALLEL;
  const ra = { x: a.x - 1, y: a.y - 1, w: NODE_W + 2, h: NODE_H + 2 };
  const rb = { x: b.x - 1, y: b.y - 1, w: NODE_W + 2, h: NODE_H + 2 };
  const sa = { x: ca.x + nx * offset, y: ca.y + ny * offset };
  const sb = { x: cb.x + nx * offset, y: cb.y + ny * offset };
  const p = exit(sa, sb, ra);
  const q = exit(sb, sa, rb);
  const visible = (q.x - p.x) * ux + (q.y - p.y) * uy;
  if (visible <= 2) return null;
  return { p, q, ux, uy, nx, ny, length: visible, mid: { x: (p.x + q.x) / 2, y: (p.y + q.y) / 2 } };
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
