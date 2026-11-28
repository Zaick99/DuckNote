// Il disegno della mappa in SVG: zone sotto, cavi in mezzo, apparati sopra. Le testate delle
// zone (nome, rete, VLAN) stanno sopra ai cavi, come le etichette: un cavo che entra nella zona
// ci passa sotto. Ogni oggetto del modello ha il suo elemento, creato una volta e poi solo
// aggiornato (render si chiama spesso: a ogni fotogramma di un trascinamento).

import { vpnOf, vpnColor, badgeMarkup, badgePoint } from './vpn.js';
import { s, attr, text, toggleClass, fit, measure, shown } from '../common/dom.js';
import {
  KIND, MIN_ZOOM, MAX_ZOOM, TONES, TAB_H, TAB_RAISE, PAD_X, CONTENT_TOP, ICON, TEXT_W,
  carriesVlan, parseVlans, sizeOf, panelOf, panelBox, boundsOf,
} from './model.js';
import { kinds as kindIcons } from '../common/icons.js';
import { cableRoutes } from './geometry.js';
import { tween, Curves, still } from '../common/motion.js';

export const FONT = '-apple-system, BlinkMacSystemFont, "SF Pro Text", "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif';
export const MONO = '"SF Mono", ui-monospace, Menlo, "Cascadia Mono", Consolas, monospace';
export const ZONE_FONT = `700 13px ${FONT}`;
export const ZONE_SUB_FONT = `11px ${MONO}`;
const NAME_FONT = `700 14px ${FONT}`;
const ROLE_FONT = `12px ${FONT}`;
const STATUS_FONT = `700 11px ${FONT}`;
const MODEL_FONT = `700 13px ${FONT}`;
const ADDRESS_FONT = `11px ${MONO}`;
const FIGURE_FONT = `12px ${FONT}`;
const FIGURE_BOLD = `700 12px ${FONT}`;
const CAPTION_FONT = `8.5px ${FONT}`;
const CLOUD_FONT = `700 17px ${FONT}`;

// Il disegno di Vantage (github.com/levi-xy/vantage, licenza MIT), rifatto in SVG: le parti
// fisiche delle prese hanno gli stessi colori nei due temi (un'apertura scura, i contatti d'oro).
const OPENING = '#12161c';
const GOLD = '#c9a227';

/** Le parole dello stato nella linguetta, come in Vantage: UP verde, DOWN rosso. */
export const STATUS = { up: 'UP', down: 'DOWN' };

/** Il colore di un tono nella tavolozza della mappa (quella degli apparati di Vantage). */
export const toneVar = (tone) => `var(--vt-tone-${tone})`;

const esc = (value) => String(value).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);
const r1 = (value) => Math.round(value * 10) / 10;

/** Il colore di un apparato: quello del suo tipo. */
export const colorOf = (node) => toneVar((KIND[node.kind] ?? KIND.other).tone);

/**
 * La linguetta di un apparato, a cavallo del bordo di sopra: il nome, "· posto" e lo stato.
 * Torna i testi gia' tagliati e la larghezza.
 */
export function tabOf(node, state) {
  const size = sizeOf(node);
  const status = STATUS[state] ?? '';
  const statusW = status ? Math.ceil(measure(status, STATUS_FONT)) : 0;
  const room = Math.max(48, size.w - 2 * PAD_X - 20 - (status ? statusW + 6 : 0));
  const name = fit(node.name || KIND[node.kind]?.label || 'Apparato', NAME_FONT, room);
  const nameW = Math.ceil(measure(name, NAME_FONT));
  const left = room - nameW - 6;
  const role = node.place && left > 24 ? fit(`· ${node.place}`, ROLE_FONT, left) : '';
  const roleW = role ? Math.ceil(measure(role, ROLE_FONT)) : 0;
  return { name, nameW, role, roleW, status, statusW, width: 10 + nameW + (role ? 6 + roleW : 0) + (status ? 6 + statusW : 0) + 10 };
}

/**
 * Le righe della scheda: il modello (o il tipo), l'indirizzo, e la riga dei valori come quella
 * di Vantage ("Risposta 0.6 ms · Sistema Linux"), coi valori nel colore dell'apparato.
 * figure: [{ label, value }] oppure { text, tone } quando c'e' solo da dire lo stato.
 */
export function cardLines(node, host, state, used) {
  const panel = panelOf(node);
  const room = (panel ? TEXT_W : sizeOf(node).w - PAD_X) - PAD_X - ICON - 10;
  const kind = KIND[node.kind]?.label ?? 'Apparato';
  const title = fit(node.model || (panel && node.ports ? `${kind} · ${node.ports} porte` : kind), MODEL_FONT, room);
  const address = node.host ? fit(node.host, ADDRESS_FONT, room) : '';
  const full = sizeOf(node).w - 2 * PAD_X - (panel ? sizeOf(node).w - PAD_X - TEXT_W : 0);
  let figure;
  if (state === 'down') figure = { text: 'Non risponde', tone: 'var(--vt-crit-text)' };
  else if (state === 'wait') figure = { text: 'Da verificare', tone: 'var(--vt-warn)' };
  else if (state === 'unknown') figure = { text: 'Mai controllato', tone: 'var(--vt-muted)' };
  else if (state === 'none') figure = { text: node.place || 'Senza indirizzo: non si controlla', tone: 'var(--vt-muted)' };
  else {
    const pairs = [];
    if (panel && used) pairs.push({ label: 'Porte', value: `${used.size}/${(node.ports || 0) + (node.sfp || 0)}` });
    if (host?.rtt) pairs.push({ label: 'Risposta', value: host.rtt });
    if (host?.os) pairs.push({ label: 'Sistema', value: host.os });
    if (pairs.length === 0) pairs.push({ label: 'Stato', value: 'risponde' });
    figure = fitPairs(pairs, Math.max(60, full));
  }
  return { title, address, figure };
}

/** Le coppie "nome valore" della riga dei valori finche' ci stanno (l'ultima tagliata). */
function fitPairs(pairs, room) {
  const out = [];
  let used = 0;
  for (const pair of pairs) {
    const sep = out.length ? measure(' · ', FIGURE_FONT) : 0;
    const label = `${pair.label} `;
    const width = sep + measure(label, FIGURE_FONT) + measure(pair.value, FIGURE_BOLD);
    if (used + width > room) {
      const rest = room - used - sep - measure(label, FIGURE_FONT);
      if (rest > 24) out.push({ label, value: fit(pair.value, FIGURE_BOLD, rest) });
      break;
    }
    out.push({ label, value: pair.value });
    used += width;
  }
  return out;
}

/** La nuvola di Vantage (viewBox 210 x 110), stirata sulla misura data. */
const CLOUD = 'M50 102 C24 102 8 88 8 70 C8 54 20 44 36 42 C38 22 58 8 82 8 C100 8 114 16 122 28 C128 24 136 22 144 22 C164 22 178 36 180 52 C194 54 202 66 202 78 C202 92 190 102 174 102 Z';

/** Il colore di ogni VLAN: quello della zona che la porta, oppure uno fisso dal suo numero. */
export function vlanTones(map) {
  const tones = new Map();
  for (const zone of map.zones) {
    for (const id of parseVlans(zone.vlan).ids) if (!tones.has(id)) tones.set(id, zone.tone);
  }
  return tones;
}

const FREE_TONES = TONES.filter((tone) => tone !== 'Red' && tone !== 'Gray');

export const vlanTone = (tones, id) => tones.get(id) ?? FREE_TONES[id % FREE_TONES.length];

/** Lo spessore del cavo dalla velocita', come in Vantage: 100M sottile, 10G e oltre spesso. */
const WIDTH = { '10M': 1, '100M': 1, '1G': 1.5, '2.5G': 2.5, '5G': 3, '10G': 4, '25G': 5, '40G': 6, '100G': 7 };
export const strokeOf = (link) => WIDTH[link.speed] ?? 1.5;

/** Il tratto: pieno, punteggiato (via Internet: VPN e WAN), tratteggiato (Wi-Fi), a pallini (un servizio VPN). */
export const DASH = { solid: null, dotted: '1.5 4', dashed: '7 5', down: '6 5', brand: '0.01 6' };

/** Lo spessore del disegno: quello della velocita'; i pallini di un servizio VPN almeno 3, che il colore si veda. */
export const widthOf = (link, wire) => (wire.brand ? Math.max(3, strokeOf(link)) : strokeOf(link));

/** I capi del tratto: tondi per i punti e i pallini, dritti per il resto. */
export const capOf = (wire) => (wire.style === 'dotted' || wire.style === 'brand' ? 'round' : 'butt');

/**
 * Come si disegna un cavo, con le regole di Vantage: rosso tratteggiato se un capo e' giu'; nel
 * colore della VLAN se porta una VLAN sola (porta access); verde se un capo risponde; grigio se
 * non si sa. Un trunk (piu' VLAN) ha una seconda linea chiara sotto ed e' scuro finche' non
 * risponde. style: la riga della legenda; dash: il tratto; color: il colore (variabile CSS).
 */
export function wireOf(link, stateA, stateB, tones) {
  const down = stateA === 'down' || stateB === 'down';
  const up = !down && (stateA === 'up' || stateB === 'up');
  const vlans = parseVlans(link.vlan);
  const trunk = vlans.all || vlans.ids.size > 1;
  const access = !trunk && vlans.ids.size === 1;
  const tone = access ? vlanTone(tones, [...vlans.ids][0]) : null;
  // un collegamento VPN che nomina il suo servizio ha il colore del logo (rosso solo se e' giu')
  const brand = vpnOf(link);
  const style = brand ? 'brand' : link.kind === 'vpn' || link.kind === 'wan' ? 'dotted' : link.kind === 'wifi' ? 'dashed' : 'solid';
  const color = down ? 'var(--vt-crit)' : brand ? vpnColor(brand.id) : tone ? toneVar(tone) : up ? 'var(--vt-ok)' : trunk ? 'var(--vt-ink)' : 'var(--vt-muted)';
  return { down, up, trunk: trunk && !brand, access: access && !brand, tone: brand ? null : tone, style, dash: down ? DASH.down : DASH[style], color, brand };
}

/** Il testo della velocita' accanto al cavo (per il Wi-Fi senza velocita', "Wi-Fi"). */
export const speedText = (link) => link.speed || (link.kind === 'wifi' ? 'Wi-Fi' : '');

/** Il punto dell'etichetta della velocita': un po' prima del capo di arrivo, accanto alla linea. */
export function tagPoint(points) {
  const end = points[points.length - 1];
  const prev = points[points.length - 2];
  const dx = Math.sign(end.x - prev.x);
  const dy = Math.sign(end.y - prev.y);
  return dy !== 0
    ? { x: end.x + 6, y: end.y - dy * 22 + 4, anchor: 'start' }
    : { x: end.x - dx * 26, y: end.y - 6, anchor: 'middle' };
}

export function zoneLabel(zone) {
  const parts = [];
  if (zone.cidr) parts.push(zone.cidr);
  if (zone.vlan) parts.push(`VLAN ${zone.vlan}`);
  return parts.join(' · ');
}

/** La linguetta di una zona: nome e rete, tagliati perche' stiano nella zona, e la sua larghezza. */
export function zoneHead(zone) {
  const name = fit(zone.name || 'Zona', ZONE_FONT, Math.max(40, zone.w - 70));
  const nameWidth = Math.ceil(measure(name, ZONE_FONT));
  const label = zoneLabel(zone);
  const sub = label ? fit(label, ZONE_SUB_FONT, Math.max(0, zone.w - nameWidth - 84)) : '';
  const subWidth = sub && sub !== '…' ? Math.ceil(measure(sub, ZONE_SUB_FONT)) + 8 : 0;
  return { name, nameWidth, sub: subWidth ? sub : '', subWidth, width: 10 + nameWidth + subWidth + 10 };
}

/** Dove sta la linguetta di una zona (a cavallo del suo bordo di sopra), in coordinate della mappa. */
export const zoneHeadRect = (zone) => ({ x: zone.x + PAD_X, y: zone.y - TAB_RAISE, w: zoneHead(zone).width, h: TAB_H });

/**
 * I cavi da disegnare: la strada di ognuno (ad angolo retto, dalla presa della porta quando la
 * nomina), come disegnarlo e la velocita' da scriverci accanto. Lo usano lo schermo e
 * l'esportazione.
 */
export function linkLayout(map, quick = false, stateOf = () => 'none') {
  const nodes = new Map(map.nodes.map((n) => [n.id, n]));
  const routes = cableRoutes(map, { quick, tabEnd: (node) => PAD_X + tabOf(node, stateOf(node)).width });
  const tones = vlanTones(map);
  const entries = [];
  for (const link of map.links) {
    const a = nodes.get(link.a);
    const b = nodes.get(link.b);
    if (!a || !b) continue;
    const route = routes.get(link.id) ?? null;
    const points = route?.points ?? null;
    const speed = speedText(link);
    const wire = wireOf(link, stateOf(a), stateOf(b), tones);
    entries.push({
      link,
      a,
      b,
      route,
      points,
      wire,
      speed,
      tag: points && speed ? tagPoint(points) : null,
      // il logo del servizio VPN, sul tratto che entra nell'apparato d'arrivo
      badge: points && wire.brand ? badgePoint(points) : null,
      title: cableTitle(link, a, b),
    });
  }
  return entries;
}

/** Il cavo in una riga, per il cartellino che compare col mouse sopra. */
function cableTitle(link, a, b) {
  const end = (node, port) => `${node.name || 'apparato'}${port ? ` ${port}` : ''}`;
  const parts = [`${end(a, link.pa)} → ${end(b, link.pb)}`];
  const brand = vpnOf(link);
  if (brand) parts.push(brand.label);
  if (link.speed) parts.push(link.speed);
  if (link.vlan) parts.push(`VLAN ${link.vlan}`);
  return parts.join(' · ');
}

/**
 * Le porte usate di ogni apparato, presa per presa ('rj45:12', 'cage:1'): il cavo, le VLAN
 * (una sola: non taggata, il trattino alto; piu' d'una: taggate, i trattini bassi), il nome di
 * chi c'e' dall'altra parte (la didascalia), lo stato del collegamento (il LED).
 */
export function usedPorts(entries, stateOf = () => 'none', tones = new Map()) {
  const used = new Map();
  for (const entry of entries) {
    for (const [end, node, other] of [['a', entry.a, entry.b], ['b', entry.b, entry.a]]) {
      const port = entry.route?.ends[end].port;
      if (!port) continue;
      if (!used.has(node.id)) used.set(node.id, new Map());
      const label = end === 'a' ? entry.link.pa : entry.link.pb;
      const vlans = parseVlans(entry.link.vlan);
      const ids = [...vlans.ids].sort((x, y) => x - y);
      const mine = stateOf(node);
      const theirs = stateOf(other);
      const link = mine === 'down' || theirs === 'down' ? 'down' : mine === 'up' || theirs === 'up' ? 'up' : 'off';
      used.get(node.id).set(port.key, {
        untagged: ids.length === 1 && !vlans.all ? vlanTone(tones, ids[0]) : null,
        tagged: ids.length > 1 ? ids.slice(0, 4).map((id) => vlanTone(tones, id)) : vlans.all ? ['Gray'] : [],
        caption: other.name || KIND[other.kind]?.label || '',
        link,
        // per il riquadro della porta col mouse sopra
        label,
        other: other.name || KIND[other.kind]?.label || '',
        otherPort: end === 'a' ? entry.link.pb : entry.link.pa,
        speed: entry.link.speed,
        vlans: { untagged: ids.length === 1 && !vlans.all ? ids[0] : null, tagged: ids.length > 1 ? ids : [], all: vlans.all },
        title: `${label} → ${other.name || 'apparato'}${entry.link.speed ? ` · ${entry.link.speed}` : ''}${entry.link.vlan ? ` · VLAN ${entry.link.vlan}` : ''}${link === 'up' ? ' · UP' : link === 'down' ? ' · DOWN' : ''}`,
      });
    }
  }
  return used;
}

// ------------------------------------------------------------------ la scheda

/** La linguetta: un rettangolo con gli angoli di sopra arrotondati; i lati scendono fino al bordo del corpo. */
function tabPaths(width) {
  const x0 = PAD_X + 0.5;
  const x1 = PAD_X + width - 0.5;
  const r = 7;
  const fill = `M${x0} ${TAB_H} V${r} Q${x0} 0.5 ${x0 + r} 0.5 H${x1 - r} Q${x1} 0.5 ${x1} ${r} V${TAB_H} Z`;
  const edge = `M${x0} ${TAB_RAISE + 1} V${r} Q${x0} 0.5 ${x0 + r} 0.5 H${x1 - r} Q${x1} 0.5 ${x1} ${r} V${TAB_RAISE + 1}`;
  return { fill, edge };
}

/** Il bordo colorato di sopra del corpo (3 px, segue gli angoli arrotondati). */
function topBar(w, r) {
  const y = TAB_RAISE + 1.5;
  return `M1.5 ${y + r - 1.5} Q1.5 ${y} ${r} ${y} H${w - r} Q${w - 1.5} ${y} ${w - 1.5} ${y + r - 1.5}`;
}

/** Una presa: RJ45 (apertura, contatti, LED) o gabbia SFP, nella sua cella, come in Vantage. */
function jackMarkup(jack, used, flags, titles = false) {
  const stroke = used ? (used.untagged ? toneVar(used.untagged) : 'var(--vt-muted)') : 'var(--vt-port-off)';
  const linkLed = used?.link === 'up' ? 'var(--vt-ok)' : used?.link === 'down' ? 'var(--vt-crit)' : 'var(--vt-port-off)';
  const parts = [];
  if (flags.numbers) {
    parts.push(`<text x="${r1(jack.w / 2)}" y="${r1(jack.numberY + 8.5)}" text-anchor="middle" font-size="9" fill="var(--vt-muted)">${jack.n}</text>`);
  }
  const [vw, vh] = jack.kind === 'cage' ? [34, 18] : [26, 22];
  const sx = jack.jackW / vw;
  const sy = jack.jackH / vh;
  const turn = jack.flip ? ` transform="rotate(180 ${vw / 2} ${vh / 2})"` : '';
  let inner;
  if (jack.kind === 'cage') {
    inner = `<rect x="4" y="5" width="${vw - 8}" height="8" fill="${OPENING}"/><rect x="4" y="2.5" width="${vw - 8}" height="1.2" fill="var(--vt-port-off)"/><circle cx="${vw - 3.5}" cy="${vh - 3}" r="1.3" fill="${linkLed}"/>`;
  } else {
    const pins = Array.from({ length: 8 }, (_, i) => `<rect x="${r1(6.6 + i * 1.65)}" y="4.6" width="0.9" height="3" fill="${GOLD}"/>`).join('');
    inner = `<path d="M5 4 H21 V14 H17 V18 H9 V14 H5 Z" fill="${OPENING}"/>${pins}<rect x="2" y="17" width="3.5" height="2.2" rx=".5" fill="${linkLed}"/><rect x="20.5" y="17" width="3.5" height="2.2" rx=".5" fill="var(--vt-port-off)"/>`;
  }
  parts.push(`<g transform="translate(${r1(jack.jackX)} ${r1(jack.jackY)}) scale(${r1(sx * 1000) / 1000} ${r1(sy * 1000) / 1000})">`
    + `<rect x="0.75" y="0.75" width="${vw - 1.5}" height="${vh - 1.5}" rx="2" fill="var(--vt-chip)" stroke="${stroke}" stroke-width="1.5"/>`
    + `<g${turn}>${inner}</g></g>`);
  if (used) {
    const bars = [...(used.untagged ? [{ tone: used.untagged, h: 7 }] : []), ...used.tagged.map((tone) => ({ tone, h: 4 }))];
    const total = bars.length * 4 + Math.max(0, bars.length - 1);
    let bx = jack.w / 2 - total / 2;
    for (const bar of bars) {
      parts.push(`<rect x="${r1(bx)}" y="${r1(jack.barsY + 8 - bar.h)}" width="4" height="${bar.h}" fill="${toneVar(bar.tone)}"/>`);
      bx += 5;
    }
    if (flags.captions && used.caption) {
      parts.push(`<text x="${r1(jack.w / 2)}" y="${r1(jack.captionY + 8.5)}" text-anchor="middle" font-size="8.5" fill="var(--vt-muted)">${esc(fit(used.caption, CAPTION_FONT, jack.w))}</text>`);
    }
  }
  const title = used ? `${jack.kind === 'cage' ? 'SFP' : 'Porta'} ${jack.n}: ${used.title}` : `${jack.kind === 'cage' ? 'SFP' : 'Porta'} ${jack.n}: libera`;
  return `<g class="dm-jack${used ? ` used${used.link === 'down' ? ' down' : ''}` : ''}" data-port="${jack.n}" data-kind="${jack.kind}" transform="translate(${r1(jack.x)} ${r1(jack.y)})">${titles ? `<title>${esc(title)}</title>` : ''}`
    + `<rect width="${r1(jack.w)}" height="${r1(jack.h)}" fill="transparent"/>${parts.join('')}</g>`;
}

/**
 * Il disegno di un apparato in coordinate sue: la scheda di Vantage (linguetta, bordo colorato,
 * icona, modello, indirizzo, riga dei valori, pannello frontale) o la nuvola per Internet. I
 * colori sono variabili CSS: l'esportazione li sostituisce coi colori veri.
 */
export function cardMarkup(node, state, host, ports, { titles = false } = {}) {
  const size = sizeOf(node);
  const { w, h } = size;
  const down = state === 'down';
  const linked = !!node.host;
  const color = colorOf(node);
  const icon = kindIcons[node.kind] ?? kindIcons.other;

  if (node.kind === 'internet') {
    const name = fit(node.name || 'Internet', CLOUD_FONT, w * 0.6);
    const sub = node.host ? fit(node.host, `11px ${FONT}`, w * 0.6) : '';
    const sx = r1((w / 210) * 1000) / 1000;
    const sy = r1((h / 110) * 1000) / 1000;
    return `<path class="dm-node-ring" d="${CLOUD}" transform="translate(-6 -5) scale(${r1(((w + 12) / 210) * 1000) / 1000} ${r1(((h + 10) / 110) * 1000) / 1000})" fill="none" stroke="transparent" stroke-width="3" vector-effect="non-scaling-stroke"/>`
      + `<path class="dm-node-card dm-node-cloud" d="${CLOUD}" transform="scale(${sx} ${sy})" fill="var(--vt-panel)" stroke="${down ? 'var(--vt-crit)' : 'var(--vt-line)'}" stroke-width="1.5" vector-effect="non-scaling-stroke"${linked ? '' : ' stroke-dasharray="5 4"'}/>`
      + `<text class="dm-node-title" x="${w / 2}" y="${r1(h * 0.5 + 4)}" text-anchor="middle" font-size="17" font-weight="700" fill="${down ? 'var(--vt-crit-text)' : 'var(--vt-ink)'}">${esc(name)}</text>`
      + (sub ? `<text class="dm-node-ip" x="${w / 2}" y="${r1(h * 0.5 + 20)}" text-anchor="middle" font-size="11" fill="var(--vt-muted)">${esc(sub)}</text>` : '');
  }

  const box = panelBox(node);
  const radius = box ? 8 : 10;
  const tab = tabOf(node, state);
  const lines = cardLines(node, host, state, ports);
  const paths = tabPaths(tab.width);
  const top = TAB_RAISE + CONTENT_TOP;
  const textX = PAD_X + ICON + 10;
  const out = [
    `<rect class="dm-node-ring" x="-4" y="${TAB_RAISE - 4}" width="${w + 8}" height="${h - TAB_RAISE + 8}" rx="${radius + 4}" fill="none" stroke="transparent" stroke-width="3"/>`,
    `<rect class="dm-node-card" x="0.5" y="${TAB_RAISE + 0.5}" width="${w - 1}" height="${h - TAB_RAISE - 1}" rx="${radius}" fill="${box ? 'url(#dm-face)' : 'var(--vt-panel)'}" stroke="${down ? 'var(--vt-crit)' : 'var(--vt-line)'}" stroke-width="1"${linked ? '' : ' stroke-dasharray="5 4"'}/>`,
    linked ? `<path class="dm-node-bar" d="${topBar(w, radius)}" fill="none" stroke="${down ? 'var(--vt-crit)' : color}" stroke-width="3"/>` : '',
    `<path class="dm-node-tab" d="${paths.fill}" fill="${box ? 'var(--vt-chip)' : 'var(--vt-panel)'}"/>`,
    `<path class="dm-node-tab-edge" d="${paths.edge}" fill="none" stroke="${down ? 'var(--vt-crit)' : 'var(--vt-line)'}" stroke-width="1"/>`,
    `<text class="dm-node-tabname" x="${PAD_X + 10}" y="15.5" font-size="14" font-weight="700" fill="${down ? 'var(--vt-crit-text)' : 'var(--vt-ink)'}">${esc(tab.name)}`
      + (tab.role ? `<tspan dx="6" font-size="12" font-weight="400" fill="var(--vt-muted)">${esc(tab.role)}</tspan>` : '')
      + (tab.status ? `<tspan class="dm-node-status" dx="6" font-size="11" font-weight="700" fill="${down ? 'var(--vt-crit-text)' : 'var(--vt-ok-text)'}">${tab.status}</tspan>` : '')
      + '</text>',
    `<rect class="dm-node-tile" x="${PAD_X}" y="${top}" width="${ICON}" height="${ICON}" rx="8" fill="${linked ? color : 'var(--vt-chip)'}" fill-opacity="${linked ? 0.16 : 1}"/>`,
    `<path class="dm-node-icon" transform="translate(${PAD_X + 5} ${top + 5})" d="${icon}" fill="none" stroke="${linked ? color : 'var(--vt-muted)'}" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"/>`,
  ];
  const second = lines.address;
  const firstY = second ? top + 14 : top + 21;
  out.push(`<text class="dm-node-title" x="${textX}" y="${firstY}" font-size="13" font-weight="700" fill="${down ? 'var(--vt-crit-text)' : 'var(--vt-ink)'}">${esc(lines.title)}</text>`);
  if (second) out.push(`<text class="dm-node-ip" x="${textX}" y="${firstY + 16}" font-size="11" font-family="${esc(MONO)}" fill="var(--vt-out)">${esc(second)}</text>`);
  const figureY = top + ICON + 10 + 12;
  if (Array.isArray(lines.figure)) {
    const spans = lines.figure.map((pair, i) => `${i ? '<tspan fill="var(--vt-muted)"> · </tspan>' : ''}<tspan fill="var(--vt-muted)">${esc(pair.label)}</tspan><tspan font-weight="700" fill="${color}">${esc(pair.value)}</tspan>`).join('');
    out.push(`<text class="dm-node-info" x="${PAD_X}" y="${figureY}" font-size="12">${spans}</text>`);
  } else if (lines.figure?.text) {
    out.push(`<text class="dm-node-info" x="${PAD_X}" y="${figureY}" font-size="12" fill="${lines.figure.tone}">${esc(fit(lines.figure.text, FIGURE_FONT, (box ? TEXT_W : w) - 2 * PAD_X))}</text>`);
  }
  if (box) {
    out.push(`<rect class="dm-panel" x="${box.x + 0.5}" y="${box.y + 0.5}" width="${box.w - 1}" height="${box.h - 1}" rx="6" fill="var(--vt-inset)" stroke="var(--vt-line)" stroke-width="1"/>`);
    out.push(`<g class="dm-jacks" transform="translate(${box.ox} ${box.oy})">${box.panel.jacks.map((jack) => jackMarkup(jack, ports?.get(jack.key), box.panel.flags, titles)).join('')}</g>`);
  }
  return out.join('');
}

/** I pallini da cui si tira un cavo, a meta' di ogni lato del corpo. */
export function handlesMarkup(node) {
  const size = sizeOf(node);
  const top = node.kind === 'internet' ? 8 : TAB_RAISE;
  const bottom = node.kind === 'internet' ? size.h - 8 : size.h;
  const middle = (top + bottom) / 2;
  return [['t', size.w / 2, top], ['r', size.w, middle], ['b', size.w / 2, bottom], ['l', 0, middle]]
    .map(([side, x, y]) => `<circle class="dm-handle" data-part="handle" data-side="${side}" cx="${x}" cy="${y}" r="5"/>`).join('');
}

/** Il gradiente delle placche (switch, router, firewall): come le placche metalliche di Vantage. */
export const DEFS = '<defs><linearGradient id="dm-face" x1="0" y1="0" x2="0" y2="1">'
  + '<stop offset="0" stop-color="var(--vt-chip)"/><stop offset="0.55" stop-color="var(--vt-panel)"/><stop offset="1" stop-color="var(--vt-inset)"/>'
  + '</linearGradient></defs>';

const toneVars = (tone) => `--tone: var(--c-${tone}); --tone-soft: var(--t-${tone}-soft); --tone-border: var(--t-${tone}-border); --tone-zone: var(--t-${tone}-zone)`;

export class Scene {
  constructor(host, store) {
    this.host = host;
    this.store = store;
    this.view = { x: 0, y: 0, k: 1 };
    this.filter = { text: '', vlan: null };
    this.els = { nodes: new Map(), links: new Map(), labels: new Map(), zones: new Map(), heads: new Map() };
    this.frame = 0;
    // durante un trascinamento le etichette si sistemano alla buona, a fine gesto per bene
    this.quick = false;
    this.listeners = new Set();
    this.pulse = new Set();

    this.svg = s('svg', { class: 'dm-svg', role: 'img', 'aria-label': 'Mappa della rete', 'font-family': FONT });
    this.svg.innerHTML = DEFS;
    this.world = s('g', { class: 'dm-world' });
    this.layers = {
      zones: s('g', { class: 'dm-zones' }),
      links: s('g', { class: 'dm-links' }),
      heads: s('g', { class: 'dm-heads' }),
      labels: s('g', { class: 'dm-labels' }),
      nodes: s('g', { class: 'dm-nodes' }),
      over: s('g', { class: 'dm-over' }),
    };
    this.world.append(this.layers.zones, this.layers.links, this.layers.heads, this.layers.labels, this.layers.nodes, this.layers.over);
    this.svg.append(this.world);
    host.append(this.svg);
  }

  // ------------------------------------------------------------------ vista

  get size() {
    const rect = this.host.getBoundingClientRect();
    return { w: rect.width, h: rect.height, left: rect.left, top: rect.top };
  }

  /** Da coordinate dello schermo (clientX/Y) a coordinate della mappa. */
  toWorld(clientX, clientY) {
    const { left, top } = this.size;
    return { x: (clientX - left - this.view.x) / this.view.k, y: (clientY - top - this.view.y) / this.view.k };
  }

  toScreen(x, y) {
    const { left, top } = this.size;
    return { x: left + this.view.x + x * this.view.k, y: top + this.view.y + y * this.view.k };
  }

  setView(x, y, k) {
    this.view = { x, y, k: Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, k)) };
    this.applyView();
  }

  applyView() {
    const { x, y, k } = this.view;
    attr(this.world, 'transform', `translate(${x.toFixed(2)} ${y.toFixed(2)}) scale(${k.toFixed(4)})`);
    // la griglia di puntini segue la mappa; da lontano i puntini si diradano
    const step = 16 * k * (k < 0.5 ? 4 : 1);
    this.host.style.backgroundSize = `${step}px ${step}px`;
    this.host.style.backgroundPosition = `${x}px ${y}px`;
    toggleClass(this.svg, 'lod-far', k < 0.5);
    toggleClass(this.svg, 'lod-tiny', k < 0.3);
    for (const listener of this.listeners) listener(this.view);
  }

  onView(listener) {
    this.listeners.add(listener);
  }

  /** Zoom attorno a un punto dello schermo. */
  zoomAt(clientX, clientY, factor) {
    const { left, top } = this.size;
    const k = Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, this.view.k * factor));
    const px = clientX - left;
    const py = clientY - top;
    const x = px - (px - this.view.x) * (k / this.view.k);
    const y = py - (py - this.view.y) * (k / this.view.k);
    this.setView(x, y, k);
  }

  /** La vista che va a (x, y, k) in 420 ms, come uno scroll guidato. */
  animateTo(x, y, k, ms = 420) {
    const from = { ...this.view };
    tween(this, 'view', 0, 1, ms, (t) => {
      // lo zoom si interpola in scala logaritmica: il movimento sembra uniforme
      const kt = Math.exp(Math.log(from.k) + (Math.log(k) - Math.log(from.k)) * t);
      this.setView(from.x + (x - from.x) * t, from.y + (y - from.y) * t, kt);
    }, null, Curves.quintOut);
  }

  /** Inquadra il riquadro dato (in coordinate della mappa), con un margine. */
  /** right: pixel a destra occupati da altro (i riquadri della vista): la mappa sta nel resto. */
  frameRect(rect, { animate = true, maxZoom = 1.15, pad = 56, right = 0 } = {}) {
    const { w, h } = this.size;
    if (!rect || w < 10 || h < 10) return;
    const room = w - right > 420 ? right : 0;
    const k = Math.min(maxZoom, Math.max(MIN_ZOOM, Math.min((w - room - pad * 2) / Math.max(rect.w, 1), (h - pad * 2) / Math.max(rect.h, 1))));
    const x = (w - room) / 2 - (rect.x + rect.w / 2) * k;
    const y = h / 2 - (rect.y + rect.h / 2) * k;
    if (animate) this.animateTo(x, y, k);
    else this.setView(x, y, k);
  }

  /** Porta un punto della mappa al centro, senza cambiare zoom. */
  centerOn(point, animate = true) {
    const { w, h } = this.size;
    const k = this.view.k;
    const x = w / 2 - point.x * k;
    const y = h / 2 - point.y * k;
    if (animate) this.animateTo(x, y, k);
    else this.setView(x, y, k);
  }

  /** Il centro dello schermo in coordinate della mappa. */
  middle() {
    const { w, h, left, top } = this.size;
    return this.toWorld(left + w / 2, top + h / 2);
  }

  // ------------------------------------------------------------------ disegno

  invalidate() {
    if (this.frame) return;
    this.frame = requestAnimationFrame(() => {
      this.frame = 0;
      this.render();
    });
  }

  render() {
    const map = this.store.map;
    const lit = this.litSet(map);
    this.renderZones(map, lit);
    this.renderLinks(map, lit);
    this.renderNodes(map, lit);
  }

  /** Quello che la ricerca o il filtro VLAN lasciano acceso; null = tutto. */
  litSet(map) {
    const q = this.filter.text.trim().toLowerCase();
    const vlan = this.filter.vlan;
    if (!q && vlan == null) return null;
    const lit = new Set();
    if (q) {
      for (const node of map.nodes) {
        const hay = `${node.name} ${node.host} ${node.mac} ${node.model} ${node.place} ${node.notes} ${KIND[node.kind]?.label ?? ''}`.toLowerCase();
        if (hay.includes(q)) lit.add(node.id);
      }
      for (const zone of map.zones) {
        if (`${zone.name} ${zone.cidr} ${zone.vlan}`.toLowerCase().includes(q)) lit.add(zone.id);
      }
      for (const link of map.links) {
        if (`${link.pa} ${link.pb} ${link.vlan} ${link.notes}`.toLowerCase().includes(q) || (lit.has(link.a) && lit.has(link.b))) lit.add(link.id);
      }
    }
    if (vlan != null) {
      const byVlan = new Set();
      for (const link of map.links) {
        if (carriesVlan(link.vlan, vlan)) {
          byVlan.add(link.id);
          byVlan.add(link.a);
          byVlan.add(link.b);
        }
      }
      for (const zone of map.zones) if (parseVlans(zone.vlan).ids.has(vlan)) byVlan.add(zone.id);
      if (q) {
        for (const id of [...lit]) if (!byVlan.has(id)) lit.delete(id);
      } else {
        for (const id of byVlan) lit.add(id);
      }
    }
    return lit;
  }

  renderZones(map, lit) {
    const keep = new Set();
    const layer = this.layers.zones;
    map.zones.forEach((zone, index) => {
      keep.add(zone.id);
      let g = this.els.zones.get(zone.id);
      if (!g) {
        g = this.zoneElement(zone);
        this.els.zones.set(zone.id, g);
        const top = this.zoneHeadElement(zone);
        this.els.heads.set(zone.id, top);
        this.layers.heads.append(top);
      }
      if (layer.children[index] !== g) layer.insertBefore(g, layer.children[index] ?? null);
      const top = this.els.heads.get(zone.id);
      if (this.layers.heads.children[index] !== top) this.layers.heads.insertBefore(top, this.layers.heads.children[index] ?? null);
      this.updateZone(g, zone, lit);
    });
    this.prune(this.els.zones, keep);
    this.prune(this.els.heads, keep);
  }

  zoneElement(zone) {
    const g = s('g', { class: 'dm-zone', 'data-type': 'zone', 'data-id': zone.id });
    g.innerHTML = `<rect class="dm-zone-box" rx="10"/><path class="dm-zone-bar" fill="none" stroke-width="3"/><rect class="dm-zone-edge" rx="10"/>`
      + `<g class="dm-grips">${['nw', 'ne', 'se', 'sw'].map((c) => `<rect class="dm-grip" data-part="grip" data-corner="${c}" width="10" height="10" rx="3"/>`).join('')}</g>`;
    return g;
  }

  /**
   * La linguetta della zona, come quelle delle schede (a cavallo del bordo di sopra), nello
   * strato sopra ai cavi: stesso tipo e stesso id della zona.
   */
  zoneHeadElement(zone) {
    const g = s('g', { class: 'dm-zone dm-zone-top', 'data-type': 'zone', 'data-id': zone.id });
    g.innerHTML = `<g class="dm-zone-head" data-part="head"><path class="dm-zone-chip"/><path class="dm-zone-chip-edge" fill="none"/>`
      + `<text class="dm-zone-name" x="${PAD_X + 10}" y="15.5"></text><text class="dm-zone-sub" y="15.5"></text></g>`;
    return g;
  }

  updateZone(g, zone, lit) {
    const sel = this.store.sel.has(zone.id);
    const top = this.els.heads.get(zone.id);
    for (const el of [g, top]) {
      if (!el) continue;
      attr(el, 'transform', `translate(${zone.x} ${zone.y})`);
      attr(el, 'style', `${toneVars(zone.tone)}; --zone: var(--vt-tone-${zone.tone})`);
      toggleClass(el, 'selected', sel);
      toggleClass(el, 'dim', lit && !lit.has(zone.id));
    }
    for (const rect of g.querySelectorAll('.dm-zone-box, .dm-zone-edge')) {
      attr(rect, 'width', zone.w);
      attr(rect, 'height', zone.h);
    }
    attr(g.querySelector('.dm-zone-bar'), 'd', `M1.5 11 Q1.5 1.5 11 1.5 H${zone.w - 11} Q${zone.w - 1.5} 1.5 ${zone.w - 1.5} 11`);
    const head = zoneHead(zone);
    const paths = tabPaths(head.width);
    attr(top, 'transform', `translate(${zone.x} ${zone.y - TAB_RAISE})`);
    attr(top.querySelector('.dm-zone-chip'), 'd', paths.fill);
    attr(top.querySelector('.dm-zone-chip-edge'), 'd', paths.edge);
    text(top.querySelector('.dm-zone-name'), head.name);
    const subEl = top.querySelector('.dm-zone-sub');
    text(subEl, head.sub);
    attr(subEl, 'x', PAD_X + 10 + head.nameWidth + 8);
    const grips = g.querySelectorAll('.dm-grip');
    const at = { nw: [-5, -5], ne: [zone.w - 5, -5], se: [zone.w - 5, zone.h - 5], sw: [-5, zone.h - 5] };
    for (const grip of grips) {
      const [x, y] = at[grip.dataset.corner];
      attr(grip, 'x', x);
      attr(grip, 'y', y);
    }
  }

  renderLinks(map, lit) {
    const keep = new Set();
    // strade ed etichette si ricalcolano solo se la mappa e' cambiata (non per una selezione)
    const revision = `${this.store.revision}:${this.store.hostsRevision ?? 0}:${map.id}:${this.quick ? 'q' : ''}`;
    if (this.layout?.revision !== revision) {
      const stateOf = (node) => this.store.stateOf(node);
      const entries = linkLayout(map, this.quick, stateOf);
      this.layout = { revision, entries, ports: usedPorts(entries, stateOf, vlanTones(map)) };
    }
    for (const entry of this.layout.entries) {
      const { link } = entry;
      keep.add(link.id);
      let line = this.els.links.get(link.id);
      if (!line) {
        line = s('g', { class: 'dm-link', 'data-type': 'link', 'data-id': link.id });
        line.innerHTML = '<title></title><path class="dm-link-halo" fill="none" stroke-linejoin="round"/><path class="dm-link-under" fill="none" stroke-linejoin="round"/>'
          + '<path class="dm-link-line" fill="none" stroke-linejoin="round"/><path class="dm-link-hit" fill="none" stroke-linejoin="round"/>';
        this.els.links.set(link.id, line);
        this.layers.links.append(line);
      }
      // la velocita' in uno strato suo, sopra a tutti i cavi: nessuna linea ci passa sopra
      let tags = this.els.labels.get(link.id);
      if (!tags) {
        tags = s('g', { class: 'dm-link-labels', 'data-type': 'link', 'data-id': link.id });
        tags.innerHTML = '<text class="dm-link-speed" font-size="11" stroke-width="3" paint-order="stroke" stroke-linejoin="round"></text><g class="dm-link-badge"></g>';
        this.els.labels.set(link.id, tags);
        this.layers.labels.append(tags);
      }
      this.updateLink(line, tags, entry, lit);
    }
    this.prune(this.els.links, keep);
    this.prune(this.els.labels, keep);
  }

  updateLink(line, tags, entry, lit) {
    const { link, points, wire, title } = entry;
    const sel = this.store.sel.has(link.id);
    const dim = lit && !lit.has(link.id);
    attr(line, 'class', `dm-link k-${link.kind} w-${wire.style}${wire.down ? ' down' : ''}${wire.up ? ' up' : ''}${wire.trunk ? ' trunk' : ''}${sel ? ' selected' : ''}${dim ? ' dim' : ''}`);
    attr(tags, 'class', `dm-link-labels${sel ? ' selected' : ''}${dim ? ' dim' : ''}`);
    if (!points) {
      shown(line, false);
      shown(tags, false);
      return;
    }
    shown(line, true);
    const d = points.map((point, index) => `${index ? 'L' : 'M'}${point.x.toFixed(1)} ${point.y.toFixed(1)}`).join(' ');
    const width = widthOf(link, wire);
    const [titleEl, halo, under, main, hit] = line.children;
    text(titleEl, title);
    for (const path of [halo, under, main, hit]) attr(path, 'd', d);
    // scelto: un alone blu largo sotto; trunk: una seconda linea chiara piu' larga sotto
    attr(halo, 'stroke', sel ? 'var(--vt-in)' : 'none');
    attr(halo, 'stroke-width', width + 6);
    attr(halo, 'stroke-opacity', 0.35);
    attr(under, 'stroke', wire.trunk && !wire.down ? 'var(--vt-muted)' : 'none');
    attr(under, 'stroke-width', width + 3);
    attr(under, 'stroke-opacity', 0.5);
    attr(under, 'stroke-dasharray', wire.dash);
    attr(main, 'stroke', wire.color);
    attr(main, 'stroke-width', width);
    attr(main, 'stroke-dasharray', wire.dash);
    attr(main, 'stroke-linecap', capOf(wire));
    line.dataset.vpn = wire.brand?.id ?? '';
    attr(hit, 'stroke', 'transparent');
    attr(hit, 'stroke-width', Math.max(14, width + 8));
    const [tag, badge] = tags.children;
    shown(tags, !!(entry.tag && entry.speed) || !!entry.badge);
    if (entry.tag && entry.speed) {
      shown(tag, true);
      attr(tag, 'x', entry.tag.x.toFixed(1));
      attr(tag, 'y', entry.tag.y.toFixed(1));
      attr(tag, 'text-anchor', entry.tag.anchor);
      attr(tag, 'fill', wire.down ? 'var(--vt-crit-text)' : 'var(--vt-muted)');
      attr(tag, 'stroke', 'var(--vt-canvas)');
      text(tag, entry.speed);
    } else {
      shown(tag, false);
    }
    // il logo si ridisegna solo se cambia il servizio, il punto o lo stato
    const look = entry.badge ? `${wire.brand.id}:${entry.badge.x.toFixed(1)}:${entry.badge.y.toFixed(1)}:${wire.down ? 1 : 0}` : '';
    if (badge.dataset.look !== look) {
      badge.dataset.look = look;
      badge.innerHTML = entry.badge ? badgeMarkup(wire.brand, entry.badge.x, entry.badge.y, wire.down) : '';
    }
  }

  renderNodes(map, lit) {
    const keep = new Set();
    const layer = this.layers.nodes;
    map.nodes.forEach((node, index) => {
      keep.add(node.id);
      let g = this.els.nodes.get(node.id);
      if (!g) {
        g = this.nodeElement(node);
        this.els.nodes.set(node.id, g);
        if (this.pulse.has(node.id)) this.pop(g, node);
      }
      if (layer.children[index] !== g) layer.insertBefore(g, layer.children[index] ?? null);
      this.updateNode(g, node, lit);
    });
    this.pulse.clear();
    this.prune(this.els.nodes, keep);
  }

  nodeElement(node) {
    const g = s('g', { class: 'dm-node', 'data-type': 'node', 'data-id': node.id });
    g.innerHTML = '<g class="dm-node-body"></g><g class="dm-handles"></g>';
    return g;
  }

  /**
   * La scheda di un apparato: si rifa' il suo disegno solo se cambia quello che mostra (nome,
   * stato, righe, porte, misura); selezione, ricerca e stato restano classi sul gruppo.
   */
  updateNode(g, node, lit) {
    const sel = this.store.sel.has(node.id);
    const state = this.store.stateOf(node);
    const kind = KIND[node.kind] ?? KIND.other;
    attr(g, 'transform', `translate(${node.x} ${node.y})`);
    attr(g, 'class', `dm-node k-${node.kind} st-${state}${sel ? ' selected' : ''}${lit && !lit.has(node.id) ? ' dim' : ''}${this.hover === node.id ? ' target' : ''}`);
    attr(g, 'style', toneVars(kind.tone));
    const ports = this.layout?.ports?.get(node.id) ?? null;
    const host = this.store.hostOf(node);
    const sig = JSON.stringify([node.kind, node.name, node.model, node.host, node.place, node.ports, node.sfp, state, host?.rtt ?? '', host?.os ?? '',
      ports ? [...ports].map(([key, p]) => `${key}:${p.untagged}:${p.tagged}:${p.link}:${p.caption}:${p.title}`) : null]);
    if (g.dataset.sig === sig) return;
    g.dataset.sig = sig;
    g.firstChild.innerHTML = cardMarkup(node, state, host, ports);
    g.lastChild.innerHTML = handlesMarkup(node);
  }

  /** Un apparato appena arrivato si posa con un piccolo rimbalzo. */
  pop(g, node) {
    const body = g.querySelector('.dm-node-body');
    const size = sizeOf(node ?? this.store.node(g.dataset.id));
    tween(g, 'pop', 0.86, 1, 300, (v) => {
      body.setAttribute('transform', `translate(${size.w / 2} ${size.h / 2}) scale(${v}) translate(${-size.w / 2} ${-size.h / 2})`);
      g.style.opacity = String(Math.min(1, 0.35 + (v - 0.86) / 0.14));
    }, () => {
      body.removeAttribute('transform');
      g.style.opacity = '';
    }, Curves.backOut(0.35));
  }

  /**
   * La mappa che arriva (aperta, o un'altra mappa della nota): le zone affiorano, gli apparati
   * scendono in onda dall'alto in basso (e un poco da sinistra a destra), i cavi e le loro
   * etichette arrivano dietro ai loro due apparati. Poco piu' di mezzo secondo in tutto; con
   * "riduci movimento" la mappa c'e' e basta.
   */
  enter() {
    this.settle();
    const map = this.store.map;
    const box = boundsOf(map);
    if (still() || !box) return;
    this.render();
    const items = [];
    const delays = new Map();
    const fade = (el, delay, ms) => {
      if (el) items.push({ el, delay, ms, node: null });
    };
    for (const zone of map.zones) {
      fade(this.els.zones.get(zone.id), 0, 260);
      fade(this.els.heads.get(zone.id), 60, 260);
    }
    for (const node of map.nodes) {
      const g = this.els.nodes.get(node.id);
      if (!g) continue;
      const delay = 40 + ((node.y - box.y) / Math.max(1, box.h)) * 300 + ((node.x - box.x) / Math.max(1, box.w)) * 90;
      delays.set(node.id, delay);
      items.push({ el: g.firstChild, delay, ms: 380, node, size: sizeOf(node) });
    }
    for (const link of map.links) {
      const delay = Math.max(delays.get(link.a) ?? 0, delays.get(link.b) ?? 0) + 140;
      fade(this.els.links.get(link.id), delay, 300);
      fade(this.els.labels.get(link.id), delay + 90, 260);
    }
    const bounce = Curves.backOut(0.5);
    const paint = (item, t) => {
      const shown = Curves.cubicOut(t);
      item.el.style.opacity = String(shown);
      if (!item.node) return;
      const { w, h } = item.size;
      const scale = 0.92 + 0.08 * bounce(t);
      const drop = (1 - shown) * -12;
      item.el.setAttribute('transform', `translate(${w / 2} ${h / 2 + drop}) scale(${scale.toFixed(4)}) translate(${-w / 2} ${-h / 2})`);
    };
    for (const item of items) paint(item, 0);
    const start = performance.now();
    const tick = (now) => {
      let busy = false;
      for (const item of items) {
        const t = Math.min(1, Math.max(0, (now - start - item.delay) / item.ms));
        if (t < 1) busy = true;
        paint(item, t);
      }
      if (busy) this.entering = { frame: requestAnimationFrame(tick), items };
      else this.settle();
    };
    this.entering = { frame: requestAnimationFrame(tick), items };
  }

  /** L'onda dell'apertura finisce subito: tutto al suo posto. */
  settle() {
    if (!this.entering) return;
    cancelAnimationFrame(this.entering.frame);
    for (const item of this.entering.items) {
      item.el.style.opacity = '';
      if (item.node) item.el.removeAttribute('transform');
    }
    this.entering = null;
  }

  prune(els, keep) {
    for (const [id, el] of els) {
      if (!keep.has(id)) {
        el.remove();
        els.delete(id);
      }
    }
  }

  forget() {
    this.settle();
    this.layout = null;
    for (const els of Object.values(this.els)) {
      for (const el of els.values()) el.remove();
      els.clear();
    }
    this.layers.over.replaceChildren();
  }

  // ------------------------------------------------------------------ sopra la mappa

  /** Il rettangolo della selezione col trascinamento (in coordinate della mappa). */
  marquee(rect) {
    let el = this.layers.over.querySelector('.dm-marquee');
    if (!rect) {
      el?.remove();
      return;
    }
    if (!el) {
      el = s('rect', { class: 'dm-marquee', rx: 4 });
      this.layers.over.append(el);
    }
    attr(el, 'x', rect.x);
    attr(el, 'y', rect.y);
    attr(el, 'width', rect.w);
    attr(el, 'height', rect.h);
    attr(el, 'stroke-width', (1 / this.view.k).toFixed(3));
  }

  /** Il cavo che si sta tirando: dal bordo dell'apparato fino al puntatore. */
  wire(from, to) {
    let el = this.layers.over.querySelector('.dm-wire');
    if (!from) {
      el?.remove();
      return;
    }
    if (!el) {
      el = s('path', { class: 'dm-wire' });
      this.layers.over.append(el);
    }
    attr(el, 'd', `M${from.x} ${from.y} L${to.x} ${to.y}`);
  }

  /** Le guide di allineamento mentre si trascina (linee verticali e orizzontali). */
  guides(lines) {
    const over = this.layers.over;
    for (const old of over.querySelectorAll('.dm-guide')) old.remove();
    for (const line of lines ?? []) {
      over.append(s('line', { class: 'dm-guide', x1: line.x1, y1: line.y1, x2: line.x2, y2: line.y2, 'stroke-width': (1 / this.view.k).toFixed(3) }));
    }
  }

  /** L'apparato sotto il puntatore mentre si tira un cavo. */
  setTarget(id) {
    if (this.hover === id) return;
    this.hover = id;
    this.invalidate();
  }

  elementOf(id) {
    return this.els.nodes.get(id) ?? this.els.links.get(id) ?? this.els.zones.get(id) ?? null;
  }
}
