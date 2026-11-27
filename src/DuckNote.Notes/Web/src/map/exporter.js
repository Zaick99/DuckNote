// L'esportazione: la mappa in SVG (colori scritti per esteso, cosi' la leggono anche i
// programmi che non capiscono le variabili CSS), in PNG (lo stesso SVG disegnato su una tela, a
// doppia risoluzione) e gli inventari in CSV (li scrive la finestra, col punto e virgola).

import { KIND, CABLE, boundsOf, zoneOf } from './model.js';
import { measure } from '../common/dom.js';
import {
  FONT, MONO, CHIP_FONT, strokeOf, zoneHead, linkLayout, usedPorts, cardMarkup,
} from './scene.js';

const esc = (value) => String(value ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

/** Il colore di un token del tema, com'e' adesso (#RRGGBB o rgba). */
function colour(name, fallback = '#888888') {
  const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return value || fallback;
}

const STATE_COLOUR = { up: '--c-Green', down: '--c-Red', wait: '--c-Orange', unknown: '--c-LabelQuaternary' };

/** I tratteggi dei cavi, come in map.css. */
const DASH = { down: '6 4', net: '0.1 5.5', wifi: '5 5' };

/** La pittura dell'esportazione: gli stessi pezzi della scheda dello schermo, coi colori scritti. */
function exportPaint(c, kind, state) {
  const tone = colour(`--c-${kind.tone}`);
  const down = state === 'down';
  const badge = { up: c.green, down: c.red, wait: c.orange }[state] ?? c.third;
  return (part, info = {}) => {
    switch (part) {
      case 'ring': return 'fill="none" stroke="none"';
      case 'tab':
      case 'card': return `fill="${c.card}" stroke="${down ? c.redBorder : c.edge}"`;
      case 'cloud': return `fill="${c.card}" stroke="${down ? c.red : colour(`--t-${kind.tone}-border`, tone)}" stroke-width="1.2"`;
      case 'tabname': return `font-size="11.5" font-weight="600" fill="${c.label}"`;
      case 'badge': return `font-size="9" font-weight="700" letter-spacing="0.36" fill="${badge}"`;
      case 'accent': return `stroke="${tone}" stroke-width="2" stroke-linecap="round" opacity="0.6"`;
      case 'tile': return `fill="${colour(`--t-${kind.tone}-soft`, '#eeeeee')}"`;
      case 'icon': return `fill="none" stroke="${tone}" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"`;
      case 'title': return `font-size="13" font-weight="600" fill="${c.label}"`;
      case 'ip': return `font-size="11" font-family="${esc(MONO)}" fill="${c.accent}"`;
      case 'info': return `font-size="10.5" fill="${down ? c.red : state === 'up' ? c.second : c.third}"`;
      case 'dot': return `fill="${colour(STATE_COLOUR[state] ?? STATE_COLOUR.unknown)}" stroke="${c.card}" stroke-width="2"`;
      case 'bay': return `fill="${c.field}" stroke="${c.edge}" stroke-width="0.8"`;
      case 'jack': {
        const used = info.used;
        if (!used) return `fill="${c.card}" stroke="${c.control}" stroke-width="0.8"`;
        const fill = used.style === 'down' ? c.red : used.tone ? colour(`--c-${used.tone}`) : c.second;
        return `fill="${fill}" stroke="${fill}"`;
      }
      default: return '';
    }
  };
}

/** La mappa come SVG a se', col nome in testa. */
export function mapSvg(map, store, { title = true } = {}) {
  const box = boundsOf(map) ?? { x: 0, y: 0, w: 400, h: 200 };
  const pad = 40;
  const head = title ? 46 : 0;
  const x0 = box.x - pad;
  const y0 = box.y - pad - head;
  const width = Math.ceil(box.w + pad * 2);
  const height = Math.ceil(box.h + pad * 2 + head);
  const c = {
    paper: colour('--c-BgContent', '#ffffff'),
    card: colour('--c-BgContent', '#ffffff'),
    edge: colour('--c-Separator', '#e2e2e4'),
    label: colour('--c-Label', '#1f2328'),
    second: colour('--c-LabelSecondary', '#6a737d'),
    third: colour('--c-LabelTertiary', '#8d949c'),
    red: colour('--c-Red', '#c0392b'),
    redBorder: colour('--t-Red-border', '#c0392b59'),
    green: colour('--c-Green', '#2e9e5b'),
    orange: colour('--c-Orange', '#e8890c'),
    accent: colour('--c-Accent', '#e8890c'),
    field: colour('--c-BgFieldAlt', '#f3f3f5'),
    control: colour('--c-BorderControl', '#c9c9ce'),
  };
  const out = [];
  out.push(`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="${x0} ${y0} ${width} ${height}" font-family="${esc(FONT)}">`);
  out.push(`<rect x="${x0}" y="${y0}" width="${width}" height="${height}" fill="${c.paper}"/>`);
  if (title) {
    const date = new Date().toLocaleDateString('it-IT', { day: 'numeric', month: 'long', year: 'numeric' });
    out.push(`<text x="${x0 + pad}" y="${y0 + 34}" font-size="17" font-weight="700" fill="${c.label}">${esc(map.name)}</text>`);
    out.push(`<text x="${x0 + width - pad}" y="${y0 + 34}" font-size="11" text-anchor="end" fill="${c.third}">DuckNote · ${esc(date)}</text>`);
  }

  for (const zone of map.zones) {
    const tone = colour(`--c-${zone.tone}`);
    out.push(`<rect x="${zone.x}" y="${zone.y}" width="${zone.w}" height="${zone.h}" rx="16" fill="${colour(`--t-${zone.tone}-zone`, 'none')}" stroke="${colour(`--t-${zone.tone}-border`, tone)}"/>`);
  }
  // le testate delle zone vanno sopra ai cavi (vedi sotto), come sullo schermo
  const heads = map.zones.map((zone) => {
    const tone = colour(`--c-${zone.tone}`);
    const head = zoneHead(zone);
    return `<g transform="translate(${zone.x} ${zone.y})">`
      + `<rect x="12" y="10" width="${head.width}" height="24" rx="7" fill="${c.card}" stroke="${c.edge}"/>`
      + `<circle cx="25" cy="22" r="3.5" fill="${tone}"/>`
      + `<text x="34" y="26" font-size="12" font-weight="600" fill="${c.label}">${esc(head.name)}</text>`
      + (head.sub ? `<text x="${34 + head.nameWidth + 10}" y="26" font-size="11" font-family="${esc(MONO)}" fill="${c.second}">${esc(head.sub)}</text>` : '')
      + '</g>';
  });

  // prima tutte le linee, poi tutte le etichette: nessun cavo passa sopra a una scritta
  const layout = linkLayout(map, false, (node) => store.stateOf(node));
  const ports = usedPorts(layout);
  const chips = [];
  const chip = (label, spot, height, colourText) => {
    if (!label || !spot) return;
    const w = Math.ceil(measure(label, CHIP_FONT)) + 12;
    const left = spot.at.x - w / 2;
    const top = spot.at.y - height / 2;
    const leader = spot.lead ? `<line x1="${(spot.anchor.x - left).toFixed(1)}" y1="${(spot.anchor.y - top).toFixed(1)}" x2="${(w / 2).toFixed(1)}" y2="${(height / 2).toFixed(1)}" stroke="${c.third}" stroke-width="0.8" stroke-dasharray="2 2"/>` : '';
    chips.push(`<g transform="translate(${left.toFixed(1)} ${top.toFixed(1)})">${leader}<rect width="${w}" height="${height}" rx="${height > 17 ? 6 : 5}" fill="${c.card}" stroke="${c.edge}"/>`
      + `<text x="6" y="${height > 17 ? 12.5 : 11.5}" font-size="10.5" font-family="${esc(MONO)}" fill="${colourText}">${esc(label)}</text></g>`);
  };
  for (const { link, points, wire, pa, pb, mid, spots } of layout) {
    if (!points) continue;
    const stroke = wire.style === 'trunk' ? c.second : wire.tone ? colour(`--c-${wire.tone}`) : c.third;
    const dash = DASH[wire.style] ? ` stroke-dasharray="${DASH[wire.style]}"` : '';
    const d = points.map((point, index) => `${index ? 'L' : 'M'}${point.x.toFixed(1)} ${point.y.toFixed(1)}`).join(' ');
    const width = strokeOf(link) + (wire.style === 'trunk' ? 0.6 : 0);
    out.push(`<path d="${d}" fill="none" stroke="${stroke}" stroke-width="${width.toFixed(2)}" stroke-linecap="round" stroke-linejoin="round"${dash}${wire.style === 'down' ? ' opacity="0.85"' : ''}/>`);
    chip(mid, spots?.mid, 18, c.second);
    chip(pa, spots?.a, 16, c.third);
    chip(pb, spots?.b, 16, c.third);
  }
  out.push(...heads, ...chips);

  for (const node of map.nodes) {
    const kind = KIND[node.kind] ?? KIND.other;
    const state = store.stateOf(node);
    out.push(`<g transform="translate(${node.x} ${node.y})">`);
    out.push(cardMarkup(node, state, store.hostOf(node), ports.get(node.id) ?? null, exportPaint(c, kind, state)));
    out.push('</g>');
  }
  out.push('</svg>');
  return { svg: out.join(''), width, height };
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
    .map((link) => [nodes.get(link.a)?.name ?? '', link.pa, nodes.get(link.b)?.name ?? '', link.pb, CABLE[link.kind]?.label ?? '', link.speed, link.vlan, link.notes])
    .sort((a, b) => a[0].localeCompare(b[0], 'it', { numeric: true }) || a[2].localeCompare(b[2], 'it', { numeric: true }));
  return { header, rows };
}

/** Un nome di file dal nome della mappa. */
export function fileName(map, suffix, extension) {
  const base = (map.name || 'Mappa').replace(/[\\/:*?"<>|\u0000-\u001f]+/g, ' ').replace(/\s+/g, ' ').trim() || 'Mappa';
  return `${base}${suffix ? ` - ${suffix}` : ''}.${extension}`;
}
