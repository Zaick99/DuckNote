// L'esportazione: la mappa in SVG (colori scritti per esteso, cosi' la leggono anche i
// programmi che non capiscono le variabili CSS), in PNG (lo stesso SVG disegnato su una tela, a
// doppia risoluzione) e gli inventari in CSV (li scrive la finestra, col punto e virgola).

import { KIND, CABLE, boundsOf, zoneOf } from './model.js';
import {
  FONT, MONO, DEFS, widthOf, capOf, zoneHead, zoneHeadRect, linkLayout, usedPorts, cardMarkup, vlanTones, toneVar,
} from './scene.js';
import { badgeMarkup, vpnOf } from './vpn.js';

const esc = (value) => String(value ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

/**
 * I colori del disegno sono variabili CSS (quelle della mappa, --vt-*): nel file esportato
 * diventano i colori di adesso, cosi' lo leggono anche i programmi che non le capiscono.
 */
function resolveColours(markup) {
  const source = document.querySelector('.dm-canvas') ?? document.documentElement;
  const style = getComputedStyle(source);
  const cache = new Map();
  const value = (name) => {
    if (!cache.has(name)) cache.set(name, style.getPropertyValue(`--${name}`).trim() || '#888888');
    return cache.get(name);
  };
  // una variabile puo' rimandare a un'altra: si risolve finche' ne restano
  let out = markup;
  for (let pass = 0; pass < 3 && out.includes('var(--'); pass++) out = out.replace(/var\(--([\w-]+)\)/g, (_, name) => value(name));
  return out;
}

/** La mappa come SVG a se', col nome in testa: lo stesso disegno dello schermo, coi colori per esteso. */
export function mapSvg(map, store, { title = true } = {}) {
  const box = boundsOf(map) ?? { x: 0, y: 0, w: 400, h: 200 };
  const pad = 40;
  const head = title ? 46 : 0;
  const x0 = box.x - pad;
  const y0 = box.y - pad - head;
  const width = Math.ceil(box.w + pad * 2);
  const height = Math.ceil(box.h + pad * 2 + head);
  const stateOf = (node) => store.stateOf(node);
  const out = [];
  out.push(`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="${x0} ${y0} ${width} ${height}" font-family="${esc(FONT)}">`);
  out.push(DEFS);
  out.push(`<rect x="${x0}" y="${y0}" width="${width}" height="${height}" fill="var(--vt-canvas)"/>`);
  if (title) {
    const date = new Date().toLocaleDateString('it-IT', { day: 'numeric', month: 'long', year: 'numeric' });
    out.push(`<text x="${x0 + pad}" y="${y0 + 34}" font-size="17" font-weight="700" fill="var(--vt-ink)">${esc(map.name)}</text>`);
    out.push(`<text x="${x0 + width - pad}" y="${y0 + 34}" font-size="11" text-anchor="end" fill="var(--vt-muted)">DuckNote · ${esc(date)}</text>`);
  }

  for (const zone of map.zones) {
    const tone = toneVar(zone.tone);
    out.push(`<g transform="translate(${zone.x} ${zone.y})">`
      + `<rect width="${zone.w}" height="${zone.h}" rx="10" fill="${tone}" fill-opacity="0.07" stroke="${tone}" stroke-opacity="0.45"/>`
      + `<path d="M1.5 11 Q1.5 1.5 11 1.5 H${zone.w - 11} Q${zone.w - 1.5} 1.5 ${zone.w - 1.5} 11" fill="none" stroke="${tone}" stroke-width="3"/>`
      + '</g>');
  }

  // prima tutti i cavi, poi le testate delle zone e le velocita': nessun cavo ci passa sopra
  const layout = linkLayout(map, false, stateOf);
  const ports = usedPorts(layout, stateOf, vlanTones(map));
  const tags = [];
  for (const { link, points, wire, speed, tag, badge, title: about } of layout) {
    if (!points) continue;
    const d = points.map((point, index) => `${index ? 'L' : 'M'}${point.x.toFixed(1)} ${point.y.toFixed(1)}`).join(' ');
    const width = widthOf(link, wire);
    const dash = wire.dash ? ` stroke-dasharray="${wire.dash}"` : '';
    out.push(`<g><title>${esc(about)}</title>`
      + (wire.trunk && !wire.down ? `<path d="${d}" fill="none" stroke="var(--vt-muted)" stroke-opacity="0.5" stroke-width="${width + 3}" stroke-linejoin="round"${dash}/>` : '')
      + `<path d="${d}" fill="none" stroke="${wire.color}" stroke-width="${width}" stroke-linejoin="round" stroke-linecap="${capOf(wire)}"${dash}/></g>`);
    if (badge) tags.push(`<g>${badgeMarkup(wire.brand, badge.x, badge.y, wire.down)}</g>`);
    if (tag && speed) {
      tags.push(`<text x="${tag.x.toFixed(1)}" y="${tag.y.toFixed(1)}" text-anchor="${tag.anchor}" font-size="11" fill="${wire.down ? 'var(--vt-crit-text)' : 'var(--vt-muted)'}" stroke="var(--vt-canvas)" stroke-width="3" paint-order="stroke" stroke-linejoin="round">${esc(speed)}</text>`);
    }
  }
  for (const zone of map.zones) {
    const headText = zoneHead(zone);
    const rect = zoneHeadRect(zone);
    out.push(`<g transform="translate(${rect.x - 14} ${rect.y})">`
      + `<path d="M14.5 ${22} V7 Q14.5 0.5 21.5 0.5 H${14 + headText.width - 7.5} Q${14 + headText.width - 0.5} 0.5 ${14 + headText.width - 0.5} 7 V${22} Z" fill="var(--vt-panel)"/>`
      + `<path d="M14.5 16 V7 Q14.5 0.5 21.5 0.5 H${14 + headText.width - 7.5} Q${14 + headText.width - 0.5} 0.5 ${14 + headText.width - 0.5} 7 V16" fill="none" stroke="var(--vt-line)"/>`
      + `<text x="24" y="15.5" font-size="13" font-weight="700" fill="var(--vt-ink)">${esc(headText.name)}</text>`
      + (headText.sub ? `<text x="${24 + headText.nameWidth + 8}" y="15.5" font-size="11" font-family="${esc(MONO)}" fill="var(--vt-muted)">${esc(headText.sub)}</text>` : '')
      + '</g>');
  }
  out.push(...tags);

  for (const node of map.nodes) {
    const state = store.stateOf(node);
    out.push(`<g transform="translate(${node.x} ${node.y})">${cardMarkup(node, state, store.hostOf(node), ports.get(node.id) ?? null, { titles: true })}</g>`);
  }
  out.push('</svg>');
  return { svg: resolveColours(out.join('')), width, height };
}

/** Lo stesso disegno in PNG, base64 (fino a 16 milioni di pixel: le tele piu' grandi WebKit le rifiuta). */
export async function mapPng(map, store) {
  const { svg, width, height } = mapSvg(map, store);
  const scale = Math.max(0.25, Math.min(2, Math.sqrt(16_000_000 / (width * height))));
  const image = new Image();
  image.src = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`;
  await image.decode();
  const canvas = document.createElement('canvas');
  canvas.width = Math.max(1, Math.round(width * scale));
  canvas.height = Math.max(1, Math.round(height * scale));
  canvas.getContext('2d').drawImage(image, 0, 0, canvas.width, canvas.height);
  return canvas.toDataURL('image/png').slice('data:image/png;base64,'.length);
}

const STATE_TEXT = { up: 'attivo', down: 'spento', wait: 'da verificare', unknown: 'mai controllato', none: '' };

/** L'inventario degli apparati, una riga per apparato. */
export function devicesTable(map, store) {
  const header = ['Nome', 'Tipo', 'Indirizzo', 'Stato', 'MAC', 'Marca e modello', 'Posizione', 'Zona', 'Porte', 'Cavi', 'Note'];
  const links = new Map(map.nodes.map((n) => [n.id, 0]));
  for (const link of map.links) {
    links.set(link.a, (links.get(link.a) ?? 0) + 1);
    links.set(link.b, (links.get(link.b) ?? 0) + 1);
  }
  const rows = [...map.nodes]
    .sort((a, b) => a.name.localeCompare(b.name, 'it', { numeric: true }))
    .map((node) => [
      node.name,
      KIND[node.kind]?.label ?? '',
      node.host,
      STATE_TEXT[store.stateOf(node)] ?? '',
      node.mac,
      node.model,
      node.place,
      zoneOf(map, node)?.name ?? '',
      node.ports ? String(node.ports) : '',
      String(links.get(node.id) ?? 0),
      node.notes,
    ]);
  return { header, rows };
}

/** L'elenco dei cavi: da dove a dove, su che porte, che tipo, che velocita', che VLAN. */
export function linksTable(map) {
  const header = ['Da', 'Porta', 'A', 'Porta', 'Tipo', 'Velocità', 'VLAN', 'Note'];
  const nodes = new Map(map.nodes.map((n) => [n.id, n]));
  const rows = map.links
    .map((link) => [nodes.get(link.a)?.name ?? '', link.pa, nodes.get(link.b)?.name ?? '', link.pb, [CABLE[link.kind]?.label ?? '', vpnOf(link)?.label].filter(Boolean).join(' · '), link.speed, link.vlan, link.notes])
    .sort((a, b) => a[0].localeCompare(b[0], 'it', { numeric: true }) || a[2].localeCompare(b[2], 'it', { numeric: true }));
  return { header, rows };
}

/** Un nome di file dal nome della mappa. */
export function fileName(map, suffix, extension) {
  const base = (map.name || 'Mappa').replace(/[\\/:*?"<>|\u0000-\u001f]+/g, ' ').replace(/\s+/g, ' ').trim() || 'Mappa';
  return `${base}${suffix ? ` - ${suffix}` : ''}.${extension}`;
}
