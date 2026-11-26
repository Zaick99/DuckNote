// Le prove della mappa, in Chromium: si usa come una persona (mouse e tastiera veri) e si
// guarda il documento che ne esce. Uso: npm test  (screenshot dei fallimenti in test/out/)
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';
import { pageHtml } from './page.mjs';

const require = createRequire(import.meta.url);
const { chromium } = require('playwright');

const out = new URL('./out/', import.meta.url).pathname;
mkdirSync(out, { recursive: true });

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1200, height: 780 } });
const errors = [];
page.on('pageerror', (e) => errors.push(e.message));
await page.setContent(pageHtml());

let failed = 0;
async function check(name, fn) {
  try {
    await fn();
    console.log(`ok   ${name}`);
  } catch (e) {
    failed++;
    console.log(`FAIL ${name}: ${e.message}`);
    await page.screenshot({ path: `${out}${name.replace(/\W+/g, '_')}.png` });
  }
}
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };
const map = () => page.evaluate(() => JSON.parse(dm.doc()).maps.find((m, i, all) => m.id === JSON.parse(dm.doc()).current) ?? null);
const messages = () => page.evaluate(() => dm.outbox());
// il disegno si rifa' al fotogramma dopo: prima di guardare lo schermo se ne aspetta uno
const frame = () => page.evaluate(() => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve))));
const mod = process.platform === 'darwin' ? 'Meta' : 'Control';
const reset = async (doc = null, options = {}) => {
  await page.evaluate(([d, o]) => dm.load(d, o), [doc, options]);
  await page.evaluate(() => dm.outbox());
  await page.mouse.move(5, 5);
};
const center = async (selector) => {
  const box = await page.locator(selector).first().boundingBox();
  assert(box, `${selector} non c'e'`);
  return { x: box.x + box.width / 2, y: box.y + box.height / 2 };
};
const drag = async (from, to, { steps = 12, shift = false } = {}) => {
  if (shift) await page.keyboard.down('Shift');
  await page.mouse.move(from.x, from.y);
  await page.mouse.down();
  await page.mouse.move(to.x, to.y, { steps });
  await page.mouse.up();
  if (shift) await page.keyboard.up('Shift');
};

const two = {
  v: 1,
  current: 'mtest',
  maps: [{
    id: 'mtest',
    name: 'Prova',
    nodes: [
      { id: 'nrouter', kind: 'router', name: 'Router', host: '10.0.0.1', x: 0, y: 0 },
      { id: 'nswitch', kind: 'switch', name: 'Switch', x: 0, y: 200, ports: 8 },
    ],
    links: [],
    zones: [],
    view: { x: 300, y: 120, k: 1 },
  }],
};

await check('pagina pronta e mappa vuota con l invito', async () => {
  await reset();
  assert(await page.isVisible('.dm-empty'), 'invito della mappa vuota non visibile');
  const m = await map();
  assert(m.nodes.length === 0 && m.name === 'Rete', JSON.stringify(m));
});

await check('clic su un apparato della libreria lo mette al centro e lo salva', async () => {
  await reset();
  await page.click('.dm-kinds .dm-kind-tile:has-text("Router")');
  await page.waitForTimeout(500);
  const m = await map();
  assert(m.nodes.length === 1 && m.nodes[0].kind === 'router' && m.nodes[0].name === 'Router', JSON.stringify(m.nodes));
  const sent = (await messages()).filter((x) => x.t === 'changed');
  assert(sent.length >= 1 && sent.at(-1).doc.maps[0].nodes.length === 1, 'la finestra non ha ricevuto la mappa cambiata');
  assert(!(await page.isVisible('.dm-empty')), 'invito ancora visibile');
});

await check('trascinare dalla libreria posa l apparato dove si lascia', async () => {
  await reset();
  const tile = await center('.dm-kinds .dm-kind-tile:has-text("Switch")');
  await drag(tile, { x: 700, y: 400 }, { steps: 15 });
  const m = await map();
  assert(m.nodes.length === 1 && m.nodes[0].kind === 'switch', JSON.stringify(m.nodes));
  const box = await page.locator(`[data-id="${m.nodes[0].id}"] .dm-node-card`).boundingBox();
  assert(Math.abs(box.x + box.width / 2 - 700) < 12 && Math.abs(box.y + box.height / 2 - 400) < 12, JSON.stringify(box));
});

await check('un cavo tirato dalla maniglia collega due apparati', async () => {
  await reset(two);
  const router = await center('[data-id="nrouter"] .dm-node-card');
  await page.mouse.move(router.x, router.y);
  const handle = await center('[data-id="nrouter"] .dm-handle[data-side="b"]');
  const target = await center('[data-id="nswitch"] .dm-node-card');
  await drag(handle, target, { steps: 10 });
  const m = await map();
  assert(m.links.length === 1 && m.links[0].a === 'nrouter' && m.links[0].b === 'nswitch', JSON.stringify(m.links));
  assert(await page.isVisible('.dm-inspector .dm-chips'), 'la scheda del cavo non si e\' aperta');
});

await check('nel modo Collega si collega trascinando dal corpo', async () => {
  await reset(two);
  await page.keyboard.press('c');
  await drag(await center('[data-id="nswitch"] .dm-node-card'), await center('[data-id="nrouter"] .dm-node-card'));
  const m = await map();
  assert(m.links.length === 1 && m.links[0].a === 'nswitch', JSON.stringify(m.links));
  await page.keyboard.press('Escape');
  assert(!(await page.evaluate(() => document.querySelector('.dm-app').classList.contains('connecting'))), 'ancora in modo Collega');
});

await check('la scheda del cavo cambia velocita VLAN e porte', async () => {
  await reset({ ...two, maps: [{ ...two.maps[0], links: [{ id: 'lone', a: 'nrouter', b: 'nswitch' }] }] });
  await page.click('[data-id="lone"] .dm-link-hit', { force: true });
  await page.click('.dm-inspector .dm-choice:has-text("Fibra")');
  await page.selectOption('.dm-inspector select', '10G');
  await page.fill('.dm-inspector [data-field="pa"]', 'sfp1');
  await page.fill('.dm-inspector [data-field="pb"]', 'Gi0/8');
  await page.fill('.dm-inspector [data-field="vlan"]', '10,20');
  await page.keyboard.press('Enter');
  const link = (await map()).links[0];
  assert(link.kind === 'fiber' && link.speed === '10G' && link.pa === 'sfp1' && link.pb === 'Gi0/8' && link.vlan === '10,20', JSON.stringify(link));
  await frame();
  const label = await page.textContent('[data-id="lone"] .dm-link-chip text');
  assert(label === '10G · VLAN 10,20', label);
  // la porta 8 dello switch adesso e' occupata nel pannello frontale
  await page.click('[data-id="nswitch"] .dm-node-card');
  const used = await page.$$eval('.dm-port-cell.on', (cells) => cells.map((c) => c.textContent));
  assert(used.join(',') === '8', used.join(','));
});

await check('spostare si aggancia alla griglia e Annulla lo rimette', async () => {
  await reset(two);
  const from = await center('[data-id="nswitch"] .dm-node-card');
  await drag(from, { x: from.x + 101, y: from.y + 37 });
  let node = (await map()).nodes.find((n) => n.id === 'nswitch');
  assert(node.x % 8 === 0 && node.y % 8 === 0 && node.x > 80, JSON.stringify(node));
  await page.keyboard.press(`${mod}+z`);
  node = (await map()).nodes.find((n) => n.id === 'nswitch');
  assert(node.x === 0 && node.y === 200, `dopo Annulla: ${JSON.stringify(node)}`);
  await page.keyboard.press(`${mod}+Shift+z`);
  node = (await map()).nodes.find((n) => n.id === 'nswitch');
  assert(node.x > 80, `dopo Ripeti: ${JSON.stringify(node)}`);
});

await check('le guide allineano al centro di un altro apparato', async () => {
  await reset(two);
  const from = await center('[data-id="nswitch"] .dm-node-card');
  // quasi sotto al router: 3 px di scarto, l'aggancio lo porta in colonna
  await drag(from, { x: from.x + 3, y: from.y + 40 });
  const node = (await map()).nodes.find((n) => n.id === 'nswitch');
  assert(node.x === 0, JSON.stringify(node));
});

await check('selezione col rettangolo e allinea a sinistra', async () => {
  await reset({ ...two, maps: [{ ...two.maps[0], nodes: [...two.maps[0].nodes, { id: 'nap', kind: 'ap', name: 'AP', x: 240, y: 104 }] }] });
  await drag({ x: 250, y: 90 }, { x: 900, y: 600 }, { shift: true });
  const selected = await page.evaluate(() => [...dm.store.sel].sort());
  assert(selected.join(',') === 'nap,nrouter,nswitch', selected.join(','));
  await page.click('.dm-inspector button[title="A sinistra"]');
  const xs = (await map()).nodes.map((n) => n.x);
  assert(xs.every((x) => x === 0), xs.join(','));
});

await check('Z fa una zona attorno agli apparati e la zona li porta con se', async () => {
  await reset(two);
  await page.click('[data-id="nrouter"] .dm-node-card');
  await page.click('[data-id="nswitch"] .dm-node-card', { modifiers: ['Shift'] });
  await page.keyboard.press('z');
  let m = await map();
  assert(m.zones.length === 1, JSON.stringify(m.zones));
  await page.keyboard.type('LAN');
  const typed = await page.evaluate(() => [document.activeElement.dataset.field, document.activeElement.value]);
  await page.keyboard.press('Enter');
  m = await map();
  assert(m.zones[0].name === 'LAN', `${m.zones[0].name} (scritto in ${typed.join(': ')})`);
  const head = await center(`[data-id="${m.zones[0].id}"] .dm-zone-chip`);
  await drag(head, { x: head.x + 160, y: head.y });
  m = await map();
  assert(m.nodes.every((n) => n.x === 160), JSON.stringify(m.nodes));
});

const hosts = [
  { key: '10.0.0.1', ip: '10.0.0.1', name: 'gw', type: 'Router / Gateway', state: 'up', rtt: '0.5 ms' },
  { key: '10.0.0.20', ip: '10.0.0.20', name: 'stampante.lan', type: 'Stampante', vendor: 'Brother', mac: 'AA:BB:CC:DD:EE:01', state: 'down' },
  { key: 'nas.casa.lan', ip: '10.0.0.30', name: 'nas.casa.lan', type: 'NAS', state: 'up' },
  { key: '172.16.0.9', ip: '172.16.0.9', name: '', type: '', state: 'unknown' },
];

await check('gli host della scansione colorano gli apparati', async () => {
  await reset(two);
  await page.evaluate((list) => dm.hosts(list), hosts);
  await frame();
  const cls = await page.getAttribute('[data-id="nrouter"]', 'class');
  assert(cls.includes('st-up'), cls);
  const rows = await page.$$eval('.dm-host', (els) => els.map((e) => e.className));
  assert(rows.length === 4 && rows.filter((c) => c.includes('placed')).length === 1, rows.join('|'));
});

await check('trascinare un host lo porta con tipo nome MAC e stato', async () => {
  await reset(two);
  await page.evaluate((list) => dm.hosts(list), hosts);
  await drag(await center('.dm-host:has-text("stampante.lan")'), { x: 800, y: 500 });
  const node = (await map()).nodes.find((n) => n.host === '10.0.0.20');
  assert(node && node.kind === 'printer' && node.name === 'stampante' && node.mac === 'AA:BB:CC:DD:EE:01' && node.model === 'Brother', JSON.stringify(node));
  await frame();
  const cls = await page.getAttribute(`[data-id="${node.id}"]`, 'class');
  assert(cls.includes('st-down'), cls);
});

await check('Aggiungi tutti mette gli host nella zona della loro rete', async () => {
  await reset({ ...two, maps: [{ ...two.maps[0], zones: [{ id: 'zlan', name: 'LAN', cidr: '10.0.0.0/24', x: -40, y: 400, w: 900, h: 300 }] }] });
  await page.evaluate((list) => dm.hosts(list), hosts);
  await page.click('.dm-library .dm-link-button:has-text("Aggiungi tutti")');
  const m = await map();
  assert(m.nodes.length === 5, `apparati: ${m.nodes.length}`);
  const zone = m.zones[0];
  const inZone = m.nodes.filter((n) => n.x >= zone.x && n.y >= zone.y && n.x + 176 <= zone.x + zone.w && n.y + 52 <= zone.y + zone.h).map((n) => n.host);
  assert(inZone.sort().join(',') === '10.0.0.20,nas.casa.lan', inZone.join(','));
  assert(m.nodes.find((n) => n.host === 'nas.casa.lan').kind === 'nas', 'tipo del NAS');
});

await check('copia e incolla con la tastiera duplicano cavi compresi', async () => {
  await reset({ ...two, maps: [{ ...two.maps[0], links: [{ id: 'lone', a: 'nrouter', b: 'nswitch' }] }] });
  await page.click('.dm-canvas', { position: { x: 900, y: 650 } });
  await page.keyboard.press(`${mod}+a`);
  await page.keyboard.press(`${mod}+c`);
  await page.keyboard.press(`${mod}+v`);
  const m = await map();
  assert(m.nodes.length === 4 && m.links.length === 2, `${m.nodes.length} apparati, ${m.links.length} cavi`);
  const ids = new Set(m.nodes.map((n) => n.id));
  assert(ids.size === 4, 'identificativi doppi');
});

await check('Canc elimina l apparato e i suoi cavi', async () => {
  await reset({ ...two, maps: [{ ...two.maps[0], links: [{ id: 'lone', a: 'nrouter', b: 'nswitch' }] }] });
  await page.click('[data-id="nswitch"] .dm-node-card');
  await page.keyboard.press('Delete');
  const m = await map();
  assert(m.nodes.length === 1 && m.links.length === 0, JSON.stringify(m));
});

await check('Disponi mette Internet in alto e gli apparati finali in basso', async () => {
  await reset({
    v: 1, current: 'mlay', maps: [{ id: 'mlay', name: 'L', nodes: [
      { id: 'npc', kind: 'pc', name: 'PC', x: 0, y: 0 },
      { id: 'nsw', kind: 'switch', name: 'SW', x: 500, y: 500 },
      { id: 'ninet', kind: 'internet', name: 'Internet', x: 300, y: 900 },
      { id: 'nrt', kind: 'router', name: 'R', x: 900, y: 100 },
      { id: 'nlone', kind: 'printer', name: 'Sola', x: 50, y: 50 },
    ], links: [{ id: 'la', a: 'npc', b: 'nsw' }, { id: 'lb', a: 'nsw', b: 'nrt' }, { id: 'lc', a: 'nrt', b: 'ninet' }], zones: [] }],
  });
  await page.click('button:has-text("Disponi")');
  const y = Object.fromEntries((await map()).nodes.map((n) => [n.id, n.y]));
  assert(y.ninet < y.nrt && y.nrt < y.nsw && y.nsw < y.npc && y.npc < y.nlone, JSON.stringify(y));
  await page.keyboard.press(`${mod}+z`);
  const back = Object.fromEntries((await map()).nodes.map((n) => [n.id, n.y]));
  assert(back.ninet === 900 && back.npc === 0, `Annulla: ${JSON.stringify(back)}`);
});

await check('il nome si scrive nella scheda e si vede sulla mappa', async () => {
  await reset(two);
  await page.dblclick('[data-id="nrouter"] .dm-node-card');
  await page.keyboard.press(`${mod}+a`);
  await page.keyboard.type('Router principale con spazi');
  await page.keyboard.press('Enter');
  const node = (await map()).nodes.find((n) => n.id === 'nrouter');
  assert(node.name === 'Router principale con spazi', node.name);
  await frame();
  const shown = await page.textContent('[data-id="nrouter"] .dm-node-name');
  assert(shown.startsWith('Router princip'), shown);
});

await check('un indirizzo scritto entra solo quando ci si ferma', async () => {
  await reset(two);
  await page.click('[data-id="nswitch"] .dm-node-card');
  await page.click('.dm-inspector [data-field="host"]');
  await page.keyboard.type('10.0.0.');
  await page.waitForTimeout(300);
  assert((await map()).nodes.find((n) => n.id === 'nswitch').host === '', 'l indirizzo a meta\' e\' gia\' entrato');
  await page.keyboard.type('2');
  await page.keyboard.press('Tab');
  assert((await map()).nodes.find((n) => n.id === 'nswitch').host === '10.0.0.2', 'l indirizzo non e\' entrato');
});

await check('esportazioni PNG SVG e CSV', async () => {
  const far = two.maps[0].nodes.map((n) => (n.id === 'nswitch' ? { ...n, y: 320 } : n));
  await reset({ ...two, maps: [{ ...two.maps[0], nodes: far, links: [{ id: 'lone', a: 'nrouter', b: 'nswitch', speed: '1G', pa: 'eth1', pb: 'Gi0/1' }] }] });
  await page.click('button:has-text("Esporta")');
  await page.click('.dm-menu-row:has-text("Immagine PNG")');
  await page.waitForTimeout(800);
  await page.click('button:has-text("Esporta")');
  await page.click('.dm-menu-row:has-text("Disegno SVG")');
  await page.click('button:has-text("Esporta")');
  await page.click('.dm-menu-row:has-text("Elenco cavi")');
  const sent = (await messages()).filter((x) => x.t === 'export');
  const png = sent.find((x) => x.format === 'png');
  const svg = sent.find((x) => x.format === 'svg');
  const csv = sent.find((x) => x.format === 'csv');
  assert(png && png.name === 'Prova.png' && Buffer.from(png.data, 'base64').subarray(1, 4).toString() === 'PNG', 'PNG');
  assert(svg && svg.text.startsWith('<svg') && svg.text.includes('Gi0/1') && !svg.text.includes('var(--'), 'SVG');
  assert(csv && csv.name === 'Prova - cavi.csv' && csv.rows[0].join('|') === 'Router|eth1|Switch|Gi0/1|Rame|1G||', JSON.stringify(csv?.rows));
});

await check('le etichette di porte e VLAN non si coprono e restano visibili', async () => {
  // uno switch con otto cavi a ventaglio verso apparati vicini, due cavi paralleli e uno cortissimo
  const nodes = [{ id: 'nsw', kind: 'switch', name: 'Switch', x: 0, y: 0, ports: 24 }];
  const links = [];
  for (let i = 0; i < 8; i++) {
    nodes.push({ id: `npc${i}`, kind: 'pc', name: `PC ${i}`, x: -560 + i * 160, y: 190 });
    links.push({ id: `lpc${i}`, a: 'nsw', b: `npc${i}`, pa: `Gi1/0/${i + 1}`, pb: 'eth0', speed: '1G', vlan: String(10 + i) });
  }
  nodes.push({ id: 'nnas', kind: 'nas', name: 'NAS', x: 400, y: -40 });
  links.push({ id: 'lagg1', a: 'nsw', b: 'nnas', pa: 'Gi1/0/23', pb: 'LAN1', speed: '10G', vlan: '20' });
  links.push({ id: 'lagg2', a: 'nsw', b: 'nnas', pa: 'Gi1/0/24', pb: 'LAN2', speed: '10G', vlan: '20' });
  nodes.push({ id: 'nap', kind: 'ap', name: 'AP', x: 0, y: -90 });
  links.push({ id: 'lshort', a: 'nsw', b: 'nap', pa: 'Gi1/0/9', pb: 'PoE', vlan: '10,20,30' });
  await reset({ v: 1, current: 'mlab', maps: [{ id: 'mlab', name: 'Etichette', nodes, links, zones: [], view: { x: 640, y: 240, k: 1 } }] });
  await frame();
  // la pillola, non il gruppo: il filo che lega un'etichetta lontana al suo cavo puo' passare sotto le altre
  const boxes = await page.$$eval('.dm-labels .dm-chip', (els) => els.map((el) => {
    const r = el.querySelector('rect').getBoundingClientRect();
    return { id: el.closest('[data-id]').dataset.id, cls: el.getAttribute('class'), shown: el.getAttribute('display') !== 'none', x: r.x, y: r.y, w: r.width, h: r.height };
  }));
  const expected = links.length * 3;
  const shown = boxes.filter((b) => b.shown);
  assert(shown.length === expected, `etichette visibili ${shown.length} su ${expected}`);
  const cards = await page.$$eval('.dm-node-card', (els) => els.map((el) => el.getBoundingClientRect()).map((r) => ({ x: r.x, y: r.y, w: r.width, h: r.height })));
  const hit = (a, b) => a.x < b.x + b.w - 0.5 && a.x + a.w > b.x + 0.5 && a.y < b.y + b.h - 0.5 && a.y + a.h > b.y + 0.5;
  const clashes = [];
  for (let i = 0; i < shown.length; i++) {
    for (let j = i + 1; j < shown.length; j++) if (hit(shown[i], shown[j])) clashes.push(`${shown[i].id}/${shown[j].id}`);
    for (const card of cards) if (hit(shown[i], card)) clashes.push(`${shown[i].id} su un apparato`);
  }
  assert(clashes.length === 0, clashes.join(', '));
  await page.screenshot({ path: `${out}etichette.png` });
});

await check('incollare un elenco di indirizzi crea gli apparati', async () => {
  await reset(two);
  await page.click('.dm-canvas', { position: { x: 900, y: 650 } });
  await page.evaluate(() => dm.paste('server 10.1.1.5\nnas.lab.lan, 10.1.1.6\nniente qui'));
  const hostsOnMap = (await map()).nodes.map((n) => n.host).filter(Boolean).sort();
  assert(hostsOnMap.join(',') === '10.0.0.1,10.1.1.5,10.1.1.6,nas.lab.lan', hostsOnMap.join(','));
});

await check('piu mappe nella nota: nuova rinomina e torna', async () => {
  await reset(two);
  await page.click('.dm-map-button');
  await page.click('.dm-menu-row:has-text("Nuova mappa")');
  await page.keyboard.press(`${mod}+a`);
  await page.keyboard.type('Sede due');
  await page.keyboard.press('Enter');
  let doc = JSON.parse(await page.evaluate(() => dm.doc()));
  assert(doc.maps.length === 2 && doc.maps[1].name === 'Sede due' && doc.current === doc.maps[1].id, JSON.stringify(doc.maps.map((m) => m.name)));
  await page.click('.dm-map-button');
  await page.click('.dm-menu-row:has-text("Prova")');
  doc = JSON.parse(await page.evaluate(() => dm.doc()));
  assert(doc.current === 'mtest', doc.current);
  assert(await page.isVisible('[data-id="nrouter"]'), 'la prima mappa non si vede');
});

await check('sola lettura: niente si modifica', async () => {
  await reset(two, { readonly: true });
  await page.click('.dm-kinds .dm-kind-tile:has-text("PC")');
  await page.click('[data-id="nswitch"] .dm-node-card');
  await page.keyboard.press('Delete');
  const m = await map();
  assert(m.nodes.length === 2, 'modificata in sola lettura');
  assert(await page.isDisabled('.dm-inspector [data-field="name"]'), 'il campo del nome non e\' bloccato');
  assert(await page.isVisible('.dm-chip:has-text("Sola lettura")'), 'manca l avviso');
});

await check('i tasti della finestra arrivano alla finestra', async () => {
  await reset(two);
  await page.click('.dm-canvas', { position: { x: 900, y: 650 } });
  await page.keyboard.press('F9');
  await page.keyboard.press(`${mod}+l`);
  const keys = (await messages()).filter((x) => x.t === 'key').map((x) => x.key);
  assert(keys.join(',') === 'sidebar,lock', keys.join(','));
});

await check('lo zoom con Ctrl e la rotella resta nei limiti', async () => {
  await reset(two);
  await page.mouse.move(700, 400);
  await page.keyboard.down('Control');
  for (let i = 0; i < 30; i++) await page.mouse.wheel(0, -200);
  await page.keyboard.up('Control');
  const k = await page.evaluate(() => dm.scene.view.k);
  assert(k <= 3 && k > 1.5, String(k));
  const doc = JSON.parse(await page.evaluate(() => dm.doc()));
  assert(Math.abs(doc.maps[0].view.k - k) < 0.01, 'la vista non e\' nel documento');
});

await check('la ricerca spegne quello che non corrisponde', async () => {
  await reset(two);
  await page.fill('.dm-search', 'switch');
  await page.waitForTimeout(100);
  const router = await page.getAttribute('[data-id="nrouter"]', 'class');
  await frame();
  const sw = await page.getAttribute('[data-id="nswitch"]', 'class');
  assert(router.includes('dim') && !sw.includes('dim'), `${router} / ${sw}`);
  await page.keyboard.press('Escape');
});

await check('documenti rovinati non rompono la mappa', async () => {
  await reset({ v: 1, maps: [{ id: 'X', nodes: [{ id: 'n1', kind: 'tostapane', x: 'a', name: 5 }, { id: 'n1' }, null], links: [{ a: 'n1', b: 'manca' }, { a: 'n1', b: 'n1' }], zones: [{ w: -5 }] }, 'boh'] });
  const doc = JSON.parse(await page.evaluate(() => dm.doc()));
  const m = doc.maps[0];
  assert(m.nodes.length === 3 && m.nodes[0].kind === 'other' && m.nodes[0].x === 0, JSON.stringify(m.nodes));
  assert(new Set(m.nodes.map((n) => n.id)).size === 3, 'identificativi doppi');
  assert(m.links.length === 0 && m.zones.length === 1 && m.zones[0].w >= 160, JSON.stringify(m));
  assert(doc.maps.length === 2, 'mappe');
});

await check('dm.clear dimentica tutto', async () => {
  await reset(two);
  await page.evaluate((list) => dm.hosts(list), hosts);
  await page.evaluate(() => dm.clear());
  const doc = JSON.parse(await page.evaluate(() => dm.doc()));
  assert(doc.maps.length === 1 && doc.maps[0].nodes.length === 0, JSON.stringify(doc));
  assert((await page.$$('.dm-node')).length === 0 && (await page.$$('.dm-host')).length === 0, 'resta qualcosa sullo schermo');
});

assert(errors.length === 0, `errori nella pagina: ${errors.join(' | ')}`);
await browser.close();
if (failed) {
  console.log(`${failed} prove fallite`);
  process.exit(1);
}
console.log('tutte le prove passate');
