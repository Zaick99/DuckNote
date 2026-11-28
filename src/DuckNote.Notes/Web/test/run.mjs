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
// le prove misurano e trascinano subito dopo aver aperto: le entrate animate diventano istantanee
// ("riduci movimento"); le prove delle animazioni le riaccendono
await page.emulateMedia({ reducedMotion: 'reduce' });
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
  await page.evaluate(([d, o]) => dm.load(d, o), [doc, { mode: 'edit', ...options }]);
  await page.evaluate(() => dm.outbox());
  await page.mouse.move(5, 5);
};
const center = async (selector) => {
  const box = await page.locator(selector).first().boundingBox();
  assert(box, `${selector} non c'e'`);
  return { x: box.x + box.width / 2, y: box.y + box.height / 2 };
};
// il clic su un apparato va sul suo corpo, all'altezza dell'icona: a destra c'e' il pannello
// delle porte, e ogni presa e' un bersaglio suo (il riquadro della porta col mouse sopra)
const body = (id) => center(`[data-id="${id}"] .dm-node-tile`);
const clickNode = async (id, { modifiers = [] } = {}) => {
  const at = await body(id);
  for (const key of modifiers) await page.keyboard.down(key);
  await page.mouse.click(at.x, at.y);
  for (const key of modifiers) await page.keyboard.up(key);
};
const dblclickNode = async (id) => {
  const at = await body(id);
  await page.mouse.dblclick(at.x, at.y);
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
  const label = await page.textContent('.dm-link-labels[data-id="lone"] .dm-link-speed');
  assert(label === '10G', label);
  // due VLAN: e' un trunk, con la seconda linea chiara sotto
  assert(await page.evaluate(() => document.querySelector('.dm-link[data-id="lone"]').classList.contains('trunk')), 'il cavo non e\' un trunk');
  // la porta 8 dello switch adesso e' occupata nel pannello frontale
  await clickNode('nswitch');
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
  await clickNode('nrouter');
  await clickNode('nswitch', { modifiers: ['Shift'] });
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

await check('i gruppi della libreria mostrano i loro host e Aggiungi tutti resta nel gruppo', async () => {
  await reset(two);
  const grouped = [
    { ...hosts[0], groups: ['nota', 'gufficio'] },
    { ...hosts[1], groups: ['gufficio'] },
    { ...hosts[2], groups: ['glab'] },
    { ...hosts[3], groups: [] },
  ];
  const groups = [{ id: 'gufficio', name: 'Ufficio', monitor: true }, { id: 'glab', name: 'Laboratorio' }];
  await page.evaluate(([list, g]) => dm.hosts(list, g), [grouped, groups]);
  await page.click('.dm-group-pick');
  const labels = await page.$$eval('.dm-menu .dm-menu-row', (els) => els.map((e) => e.textContent));
  assert(labels.some((t) => t.startsWith('Ufficio') && t.endsWith('2')) && labels.some((t) => t.startsWith('Nota') && t.endsWith('1')), labels.join('|'));
  await page.click('.dm-menu .dm-menu-row:has-text("Ufficio")');
  let rows = await page.$$eval('.dm-host .dm-host-name', (els) => els.map((e) => e.textContent));
  assert(rows.sort().join(',') === 'gw,stampante.lan', rows.join(','));
  assert((await page.textContent('.dm-group-label')) === 'Ufficio', 'etichetta del gruppo');
  await page.click('.dm-library .dm-link-button:has-text("Aggiungi tutti")');
  const m = await map();
  assert(m.nodes.length === 3 && m.nodes.some((n) => n.host === '10.0.0.20') && !m.nodes.some((n) => n.host === 'nas.casa.lan'), JSON.stringify(m.nodes.map((n) => n.host)));
  // "Ricerca": chi non sta nella nota e in nessun gruppo
  await page.click('.dm-group-pick');
  await page.click('.dm-menu .dm-menu-row:has-text("Ricerca")');
  rows = await page.$$eval('.dm-host .dm-host-name', (els) => els.map((e) => e.textContent));
  assert(rows.join(',') === '172.16.0.9', rows.join(','));
  // un gruppo che sparisce riporta a tutti gli host
  await page.click('.dm-group-pick');
  await page.click('.dm-menu .dm-menu-row:has-text("Laboratorio")');
  await page.evaluate((list) => dm.hosts(list, []), grouped);
  assert((await page.textContent('.dm-group-label')) === 'Tutti i gruppi', 'gruppo sparito');
  assert((await page.$$('.dm-host')).length === 4, 'tutti gli host');
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
  await clickNode('nswitch');
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
  await dblclickNode('nrouter');
  await page.keyboard.press(`${mod}+a`);
  await page.keyboard.type('Router principale con spazi');
  await page.keyboard.press('Enter');
  const node = (await map()).nodes.find((n) => n.id === 'nrouter');
  assert(node.name === 'Router principale con spazi', node.name);
  await frame();
  const shown = await page.textContent('[data-id="nrouter"] .dm-node-tabname');
  assert(shown.startsWith('Router princip'), shown);
});

await check('un indirizzo scritto entra solo quando ci si ferma', async () => {
  await reset(two);
  await clickNode('nswitch');
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

await check('le velocita sui cavi non si coprono e le porte si accendono sul pannello', async () => {
  // uno switch con otto cavi a ventaglio verso apparati vicini, due cavi paralleli e uno cortissimo
  const nodes = [{ id: 'nsw', kind: 'switch', name: 'Switch', x: 0, y: 0, ports: 24 }];
  const links = [];
  for (let i = 0; i < 8; i++) {
    nodes.push({ id: `npc${i}`, kind: 'pc', name: `PC ${i}`, x: -1120 + i * 300, y: 260 });
    links.push({ id: `lpc${i}`, a: 'nsw', b: `npc${i}`, pa: `Gi1/0/${i + 1}`, pb: 'eth0', speed: '1G', vlan: String(10 + i) });
  }
  nodes.push({ id: 'nnas', kind: 'nas', name: 'NAS', x: 760, y: -40 });
  links.push({ id: 'lagg1', a: 'nsw', b: 'nnas', pa: 'Gi1/0/23', pb: 'LAN1', speed: '10G', vlan: '20' });
  links.push({ id: 'lagg2', a: 'nsw', b: 'nnas', pa: 'Gi1/0/24', pb: 'LAN2', speed: '10G', vlan: '20' });
  nodes.push({ id: 'nap', kind: 'ap', name: 'AP', x: 0, y: -200 });
  links.push({ id: 'lshort', a: 'nsw', b: 'nap', pa: 'Gi1/0/9', pb: 'PoE', vlan: '10,20,30' });
  await reset({ v: 1, current: 'mlab', maps: [{ id: 'mlab', name: 'Etichette', nodes, links, zones: [], view: { x: 600, y: 260, k: 0.5 } }] });
  await frame();
  const boxes = await page.$$eval('.dm-link-labels', (els) => els.map((el) => {
    const r = el.querySelector('.dm-link-speed').getBoundingClientRect();
    return { id: el.dataset.id, text: el.textContent, shown: getComputedStyle(el).display !== 'none' && r.width > 0, x: r.x, y: r.y, w: r.width, h: r.height };
  }));
  // la velocita' sta accanto al capo di arrivo di ogni cavo che ce l'ha (il cavo dell'AP non ce l'ha)
  const shown = boxes.filter((b) => b.shown);
  assert(shown.length === 10 && shown.filter((b) => b.text === '1G').length === 8 && shown.filter((b) => b.text === '10G').length === 2, JSON.stringify(shown.map((b) => `${b.id}:${b.text}`)));
  // ogni porta nominata si accende sul pannello, coi trattini della sua VLAN; quelle libere restano spente
  const lit = await page.$$eval('[data-id="nsw"] .dm-jack.used', (els) => els.map((el) => Number(el.dataset.port)).sort((x, y) => x - y));
  assert(lit.join(',') === '1,2,3,4,5,6,7,8,9,23,24', `porte accese: ${lit}`);
  const bars = await page.$$eval('[data-id="nsw"] .dm-jack.used[data-port="9"] rect[width="4"]', (els) => els.map((el) => el.getAttribute('height')));
  assert(bars.join(',') === '4,4,4', `trattini della porta trunk: ${bars}`);
  const cards = await page.$$eval('.dm-node-card, .dm-node-tab', (els) => els.map((el) => el.getBoundingClientRect()).map((r) => ({ x: r.x, y: r.y, w: r.width, h: r.height })));
  const hit = (a, b) => a.x < b.x + b.w - 0.5 && a.x + a.w > b.x + 0.5 && a.y < b.y + b.h - 0.5 && a.y + a.h > b.y + 0.5;
  const clashes = [];
  for (let i = 0; i < shown.length; i++) {
    for (let j = i + 1; j < shown.length; j++) if (hit(shown[i], shown[j])) clashes.push(`${shown[i].id}/${shown[j].id}`);
    for (const card of cards) if (hit(shown[i], card)) clashes.push(`${shown[i].id} su un apparato`);
  }
  assert(clashes.length === 0, clashes.join(', '));
  await page.screenshot({ path: `${out}etichette.png` });
});

await check('porte su ogni apparato: scelte rapide, eth0 e il riquadro della porta', async () => {
  const doc = {
    v: 1,
    current: 'mport',
    maps: [{
      id: 'mport',
      name: 'Porte',
      nodes: [
        { id: 'nsw', kind: 'switch', name: 'Switch', x: 0, y: 0, ports: 8, sfp: 2 },
        { id: 'npc', kind: 'pc', name: 'Workstation', x: 40, y: 260 },
        { id: 'nsrv', kind: 'server', name: 'Server', x: 760, y: 0, ports: 2 },
      ],
      links: [
        { id: 'lpc', a: 'nsw', b: 'npc', pa: 'Gi0/3', pb: 'eth0', speed: '1G', vlan: '10' },
        { id: 'lsrv', a: 'nsw', b: 'nsrv', pa: 'Te1/0/1', pb: 'eth1', speed: '10G', vlan: '10,20' },
      ],
      zones: [{ id: 'zoff', name: 'Uffici', vlan: '10', tone: 'Green', x: -40, y: 200, w: 400, h: 200 }],
      view: { x: 220, y: 120, k: 1 },
    }],
  };
  await reset(doc);
  await frame();
  // il PC non ha porte: la scheda le offre, con le scelte rapide del tipo
  await clickNode('npc');
  assert(await page.isVisible('.dm-inspector [data-field="ports"]'), 'il PC non ha il campo delle porte');
  const picks = await page.$$eval('.dm-inspector .dm-pick[data-pick^="ports-"]', (els) => els.map((el) => el.textContent));
  assert(picks.join(',') === '–,1,2', `scelte del PC: ${picks}`);
  await page.click('.dm-inspector [data-pick="ports-2"]');
  await frame();
  assert((await map()).nodes.find((n) => n.id === 'npc').ports === 2, 'la scelta rapida non scrive le porte');
  // eth0 e' la prima presa (il PC conta da zero), Gi0/3 la terza dello switch, Te1/0/1 la prima SFP
  const lit = async (id) => page.$$eval(`[data-id="${id}"] .dm-jack.used`, (els) => els.map((el) => `${el.dataset.kind}:${el.dataset.port}`).sort());
  assert((await lit('npc')).join(',') === 'rj45:1', `PC: ${await lit('npc')}`);
  assert((await lit('nsw')).join(',') === 'cage:1,rj45:3', `switch: ${await lit('nsw')}`);
  assert((await lit('nsrv')).join(',') === 'rj45:2', `server: ${await lit('nsrv')}`);
  // il pannello nella scheda dello switch conta anche le SFP
  await clickNode('nsw');
  const cells = await page.$$eval('.dm-port-cell.on', (els) => els.map((el) => el.textContent));
  assert(cells.join(',') === '3,S1', `celle accese: ${cells}`);
  await page.keyboard.press('Escape');
  // il riquadro della porta col mouse sopra: numero, nome, chi c'e' dall'altra parte, VLAN
  await page.hover('[data-id="nsw"] .dm-jack.used[data-port="3"]');
  await page.waitForSelector('.dm-port-tip:not([hidden])');
  const tip = await page.textContent('.dm-port-tip');
  assert(tip.includes('Porta 3') && tip.includes('Gi0/3') && tip.includes('Workstation') && tip.includes('VLAN 10 · Uffici'), tip);
  await page.hover('[data-id="nsw"] .dm-jack[data-kind="cage"][data-port="1"]');
  const sfp = await page.textContent('.dm-port-tip');
  assert(sfp.includes('SFP 1') && sfp.includes('Server') && sfp.includes('taggate 10, 20'), sfp);
  await page.mouse.move(5, 5);
  assert(!(await page.isVisible('.dm-port-tip')), 'il riquadro resta');
});

await check('collegamenti VPN col logo e il colore del servizio', async () => {
  const doc = {
    v: 1,
    current: 'mvpn',
    maps: [{
      id: 'mvpn',
      name: 'Sedi',
      nodes: [
        { id: 'nfw', kind: 'firewall', name: 'Sede', x: 0, y: 0, ports: 4 },
        { id: 'nnas', kind: 'nas', name: 'NAS casa', x: 0, y: 320, ports: 2 },
        { id: 'nvps', kind: 'server', name: 'VPS', x: 520, y: 320 },
      ],
      links: [
        { id: 'lts', a: 'nfw', b: 'nnas', kind: 'vpn', vpn: 'tailscale', pa: 'tailscale0', pb: 'tailscale0' },
        { id: 'lwg', a: 'nfw', b: 'nvps', kind: 'vpn', vpn: 'wireguard', pa: 'wg0', pb: 'wg0', speed: '1G' },
      ],
      zones: [],
      view: { x: 260, y: 120, k: 1 },
    }],
  };
  await reset(doc, { mode: 'view' });
  await frame();
  // il colore del logo, a pallini, e il logo a meta' strada
  const look = await page.$$eval('.dm-link', (els) => Object.fromEntries(els.map((el) => [el.dataset.id, `${el.dataset.vpn} ${el.querySelector('.dm-link-line').getAttribute('stroke')} ${el.querySelector('.dm-link-line').getAttribute('stroke-linecap')}`])));
  assert(look.lts === 'tailscale var(--vpn-tailscale) round' && look.lwg === 'wireguard var(--vpn-wireguard) round', JSON.stringify(look));
  const badges = await page.$$eval('.dm-link-labels', (els) => els.map((el) => [el.dataset.id, el.querySelectorAll('.dm-link-badge path').length, getComputedStyle(el).display !== 'none']));
  assert(badges.every(([, paths, on]) => paths > 0 && on), JSON.stringify(badges));
  // wg0 e tailscale0 sono interfacce, non prese: il pannello del firewall resta spento
  assert((await page.$$('[data-id="nfw"] .dm-jack.used')).length === 0, 'una VPN ha acceso una presa');
  // la legenda: una riga per servizio, col logo
  const legend = await page.$$eval('.dm-legend-row', (els) => els.map((el) => el.dataset.legend));
  assert(legend.includes('vpn-tailscale') && legend.includes('vpn-wireguard') && !legend.includes('style-dotted'), legend.join(' '));
  // la scheda del collegamento: si cambia il servizio e cambia il colore
  await page.click('.dm-edit-toggle');
  await page.click('[data-id="lts"] .dm-link-hit', { force: true });
  assert((await page.textContent('.dm-inspector .dm-insp-title')) === 'Collegamento VPN', 'titolo della scheda');
  assert(await page.isVisible('.dm-inspector .dm-vpn.on[data-vpn="tailscale"]'), 'Tailscale non e\' scelto');
  await page.click('.dm-inspector .dm-vpn[data-vpn="netbird"]');
  await frame();
  assert((await map()).links.find((l) => l.id === 'lts').vpn === 'netbird', 'il servizio non cambia');
  assert((await page.getAttribute('.dm-link[data-id="lts"] .dm-link-line', 'stroke')) === 'var(--vpn-netbird)', 'il colore non cambia');
  // un collegamento non VPN non mostra i servizi
  await page.click('.dm-inspector .dm-choice.k-eth');
  await frame();
  assert(!(await page.isVisible('.dm-inspector .dm-vpns')), 'i servizi restano su un cavo');
  assert((await page.getAttribute('.dm-link[data-id="lts"]', 'data-vpn')) === '', 'il cavo resta della VPN');
  await page.keyboard.press(`${mod}+z`);
  // l'esportazione porta il logo e il colore per esteso
  await messages();
  await page.click('button:has-text("Esporta")');
  await page.click('.dm-menu-row:has-text("Disegno SVG")');
  await page.click('button:has-text("Esporta")');
  await page.click('.dm-menu-row:has-text("Elenco cavi")');
  const sent = (await messages()).filter((x) => x.t === 'export');
  const svg = sent.find((x) => x.format === 'svg')?.text ?? '';
  // Annulla ha rimesso NetBird: arancio NetBird e rosso WireGuard, senza variabili
  assert(svg.includes('#f68330') && svg.includes('#88171a') && !svg.includes('var(--'), 'colori dei servizi mancanti nell esportazione');
  const rows = sent.find((x) => x.format === 'csv')?.rows ?? [];
  assert(rows.some((row) => row.includes('VPN · NetBird')) && rows.some((row) => row.includes('VPN · WireGuard')), JSON.stringify(rows));
});

await check('togliere un dato dalla scheda lo toglie anche dalla mappa', async () => {
  const visible = (selector) => page.$eval(selector, (el) => getComputedStyle(el).display !== 'none' && el.getBoundingClientRect().width > 0);
  const far = two.maps[0].nodes.map((n) => (n.id === 'nswitch' ? { ...n, y: 320, ports: 8 } : { ...n }));
  await reset({ ...two, maps: [{ ...two.maps[0], nodes: far, links: [{ id: 'lone', a: 'nrouter', b: 'nswitch', speed: '1G', vlan: '10', pa: 'eth1', pb: 'Gi0/1' }],
    zones: [{ id: 'zlan', name: 'LAN', cidr: '10.0.0.0/24', vlan: '10', x: -40, y: -60, w: 300, h: 460 }] }] });
  await frame();
  const stroke = () => page.getAttribute('.dm-link[data-id="lone"] .dm-link-line', 'stroke');
  // Gi0/1 e' la porta 1 dello switch: il cavo ci si attacca e la accende; la VLAN sola colora il cavo
  assert(await visible('.dm-link-labels[data-id="lone"]') && (await page.textContent('.dm-link-labels[data-id="lone"]')) === '1G', 'velocita\' mancante all inizio');
  assert(await page.isVisible('[data-id="nswitch"] .dm-jack.used[data-port="1"]'), 'la porta 1 non si e\' accesa');
  assert((await stroke()).includes('--vt-tone-'), `il cavo access non ha il colore della VLAN: ${await stroke()}`);

  await page.click('[data-id="lone"] .dm-link-hit', { force: true });
  await page.selectOption('.dm-inspector select', '');
  await frame();
  assert(!(await visible('.dm-link-labels[data-id="lone"]')), 'la velocita\' e\' rimasta');
  await page.fill('.dm-inspector [data-field="vlan"]', '');
  await page.fill('.dm-inspector [data-field="pa"]', '');
  await page.fill('.dm-inspector [data-field="pb"]', '');
  await frame();
  assert(!(await stroke()).includes('--vt-tone-'), 'il colore della VLAN e\' rimasto sul cavo');
  assert((await page.$$('[data-id="nswitch"] .dm-jack.used')).length === 0, 'la porta 1 e\' rimasta accesa');

  // la zona: senza rete e senza VLAN resta solo il nome
  await page.evaluate(() => dm.store.select(['zlan']));
  await page.fill('.dm-inspector [data-field="cidr"]', '');
  await page.fill('.dm-inspector [data-field="vlan"]', '');
  await frame();
  assert((await page.textContent('[data-id="zlan"] .dm-zone-sub')) === '', 'la rete della zona e\' rimasta');

  // l'apparato: senza indirizzo niente pallino, la seconda riga torna il tipo; senza porte il pannello sparisce
  await page.evaluate(() => dm.store.select(['nrouter']));
  await page.fill('.dm-inspector [data-field="host"]', '');
  await page.keyboard.press('Tab');
  await frame();
  assert((await page.getAttribute('[data-id="nrouter"]', 'class')).includes('st-none'), 'il pallino di stato e\' rimasto');
  await page.evaluate(() => dm.store.select(['nswitch']));
  assert(await page.isVisible('.dm-ports-grid'), 'pannello delle porte mancante');
  await page.fill('.dm-inspector [data-field="ports"]', '0');
  await page.keyboard.press('Tab');
  await frame();
  assert(!(await page.isVisible('.dm-ports-grid')), 'il pannello delle porte e\' rimasto');
  const doc = await map();
  const link = doc.links[0];
  assert(!link.speed && !link.vlan && !link.pa && !link.pb && doc.zones[0].cidr === '' && doc.nodes.find((n) => n.id === 'nrouter').host === '', JSON.stringify(doc));
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

await check('Promemoria nella scheda dell apparato chiede una voce del calendario', async () => {
  await reset(two);
  await page.click('[data-id="nrouter"]');
  await page.click('.dm-inspector .dm-button:has-text("Promemoria")');
  const ask = (await messages()).find((m) => m.t === 'remind');
  assert(ask && ask.node === 'nrouter' && ask.map === 'mtest' && ask.host === '10.0.0.1' && ask.name === 'Router', JSON.stringify(ask));
});

await check('dm.reveal trova un apparato anche in un altra mappa', async () => {
  const other = { id: 'maltra', name: 'Laboratorio', nodes: [{ id: 'nlab', kind: 'server', name: 'Proxmox', host: '192.168.50.5', x: 0, y: 0 }], links: [], zones: [] };
  await reset({ ...two, maps: [two.maps[0], other] });
  assert(await page.evaluate(() => dm.reveal('192.168.50.5')), 'non trovato');
  const doc = JSON.parse(await page.evaluate(() => dm.doc()));
  assert(doc.current === 'maltra', doc.current);
  assert(await page.evaluate(() => [...dm.store.sel].join()) === 'nlab', 'non scelto');
  assert(!(await page.evaluate(() => dm.reveal('10.9.9.9'))), 'trovato uno che non c e');
});

await check('sola lettura: niente si modifica', async () => {
  await reset(two, { readonly: true });
  assert(!(await page.isVisible('.dm-library')), 'la libreria si vede');
  assert(await page.isDisabled('.dm-edit-toggle'), 'Modifica mappa si puo\' premere');
  await clickNode('nswitch');
  await page.keyboard.press('Delete');
  await page.keyboard.press('e');
  const m = await map();
  assert(m.nodes.length === 2, 'modificata in sola lettura');
  assert(await page.isDisabled('.dm-inspector [data-field="name"]'), 'il campo del nome non e\' bloccato');
  assert(await page.isVisible('.dm-chip:has-text("Sola lettura")'), 'manca l avviso');
  assert(!(await page.isVisible('.dm-toolbar .dm-segment')) && !(await page.isVisible('.dm-tool:has-text("Zona")')), 'gli strumenti si vedono');
});

await check('la mappa si apre da guardare e Modifica mappa apre gli strumenti', async () => {
  await reset(two, { mode: undefined });
  assert(await page.evaluate(() => document.querySelector('.dm-app').classList.contains('viewing')), 'non e\' in vista');
  assert(!(await page.isVisible('.dm-library')) && !(await page.isVisible('.dm-tool:has-text("Zona")')), 'libreria o strumenti in vista');
  assert((await page.textContent('.dm-edit-toggle')).includes('Modifica mappa'), 'manca Modifica mappa');
  // in vista trascinare un apparato sposta la visuale, non l'apparato
  const before = (await map()).nodes.find((n) => n.id === 'nswitch');
  const view = await page.evaluate(() => ({ ...dm.scene.view }));
  await drag(await center('[data-id="nswitch"] .dm-node-card'), { x: 480, y: 520 });
  const after = (await map()).nodes.find((n) => n.id === 'nswitch');
  const moved = await page.evaluate(() => ({ ...dm.scene.view }));
  assert(after.x === before.x && after.y === before.y, 'l apparato si e\' spostato in vista');
  assert(Math.abs(moved.x - view.x) > 20, 'la vista non si e\' spostata');
  // la scheda in vista: si legge, non si scrive; il Promemoria resta
  assert(await page.isVisible('.dm-inspector') && await page.isDisabled('.dm-inspector [data-field="name"]'), 'la scheda non e\' in sola lettura');
  assert(await page.isEnabled('.dm-inspector .dm-button:has-text("Promemoria")'), 'il Promemoria e\' spento');
  assert(!(await page.isVisible('.dm-inspector .dm-button:has-text("Elimina")')), 'Elimina in vista');

  await page.click('.dm-edit-toggle');
  assert(await page.isVisible('.dm-library') && await page.isVisible('.dm-tool:has-text("Zona")'), 'Modifica non apre libreria e strumenti');
  assert((await page.textContent('.dm-edit-toggle')).includes('Fine'), 'manca Fine');
  assert(await page.isEnabled('.dm-inspector [data-field="name"]'), 'la scheda resta bloccata');
  const box = await center('[data-id="nswitch"] .dm-node-card');
  await drag(box, { x: box.x + 96, y: box.y });
  assert((await map()).nodes.find((n) => n.id === 'nswitch').x !== before.x, 'in modifica l apparato non si sposta');

  // E torna a guardare
  await page.click('.dm-canvas', { position: { x: 300, y: 640 } });
  await page.keyboard.press('e');
  assert(await page.evaluate(() => document.querySelector('.dm-app').classList.contains('viewing')), 'E non torna alla vista');
});

await check('una mappa vuota si apre gia\' in modifica', async () => {
  await reset(null, { mode: undefined });
  assert(!(await page.evaluate(() => document.querySelector('.dm-app').classList.contains('viewing'))), 'vuota ma in vista');
  assert(await page.isVisible('.dm-library') && await page.isVisible('.dm-empty'), 'manca l invito');
});

await check('Reti e Legenda raccontano la mappa e accendono una VLAN', async () => {
  const doc = {
    v: 1,
    current: 'mnet',
    maps: [{
      id: 'mnet',
      name: 'Reti',
      nodes: [
        { id: 'ncore', kind: 'switch', name: 'core', host: '10.0.0.2', x: 0, y: 0, ports: 8 },
        { id: 'npc1', kind: 'pc', name: 'pc1', host: '10.0.10.21', x: -240, y: 240 },
        { id: 'nprn', kind: 'printer', name: 'stampante', host: '10.0.10.20', x: 0, y: 240 },
        { id: 'nnas', kind: 'nas', name: 'nas', x: 240, y: 240 },
      ],
      links: [
        { id: 'lpc1', a: 'ncore', b: 'npc1', pa: 'Gi0/1', vlan: '10' },
        { id: 'lprn', a: 'ncore', b: 'nprn', pa: 'Gi0/2', vlan: '10' },
        { id: 'lnas', a: 'ncore', b: 'nnas', pa: 'Gi0/3', vlan: '30,40' },
      ],
      zones: [
        { id: 'zoff', name: 'Uffici', cidr: '10.0.10.0/24', vlan: '10', tone: 'Green', x: -280, y: 200, w: 480, h: 160 },
        { id: 'zrack', name: 'Rack', x: 200, y: 200, w: 300, h: 160 },
      ],
      view: { x: 600, y: 200, k: 1 },
    }],
  };
  const hosts = [
    { key: '10.0.0.2', ip: '10.0.0.2', state: 'up' },
    { key: '10.0.10.21', ip: '10.0.10.21', state: 'up' },
    { key: '10.0.10.20', ip: '10.0.10.20', state: 'down' },
  ];
  await reset(doc, { mode: 'view', hosts });
  await frame();
  const nets = await page.$$eval('.dm-net', (els) => els.map((el) => el.textContent));
  // la zona Uffici con la sua VLAN e la rete, poi le VLAN 30 e 40 del trunk, che non hanno zona; il Rack non e' una rete
  assert(nets.length === 3 && nets[0].includes('VLAN 10') && nets[0].includes('Uffici') && nets[0].includes('10.0.10.0/24')
    && nets[1].includes('VLAN 30') && nets[2].includes('VLAN 40'), JSON.stringify(nets));
  const legend = await page.$$eval('.dm-legend-row', (els) => els.map((el) => el.dataset.legend));
  // una VLAN sola (porta access), due VLAN (trunk), i trattini delle VLAN sulle porte del core;
  // nessuna velocita' scritta e un tratto solo: quelle righe non servono
  assert(legend.join(' ') === 'access trunk port-vlans', legend.join(' '));
  // la stampante e' giu': il suo cavo e' rosso tratteggiato, quello di pc1 nel verde della zona
  const lines = await page.$$eval('.dm-link', (els) => Object.fromEntries(els.map((el) => [el.dataset.id, `${el.querySelector('.dm-link-line').getAttribute('stroke')} ${el.querySelector('.dm-link-line').getAttribute('stroke-dasharray') ?? ''}`])));
  assert(lines.lprn.startsWith('var(--vt-crit) 6 5') && lines.lpc1.startsWith('var(--vt-tone-Green)'), JSON.stringify(lines));
  await page.click('.dm-net:has-text("Uffici")');
  assert((await page.evaluate(() => dm.scene.filter.vlan)) === 10 && (await page.textContent('.dm-toolbar .dm-tool:has-text("VLAN")')).includes('VLAN 10'), 'la VLAN non si accende');
  await frame();
  assert(await page.evaluate(() => document.querySelector('[data-id="lnas"]').classList.contains('dim')), 'il trunk senza VLAN 10 resta acceso');
  await page.click('.dm-net:has-text("Uffici")');
  assert((await page.evaluate(() => dm.scene.filter.vlan)) === null, 'la VLAN non si spegne');
  // in modifica i riquadri lasciano il posto alla libreria e agli strumenti
  await page.click('.dm-edit-toggle');
  assert(!(await page.isVisible('.dm-overview')), 'i riquadri restano in modifica');
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

await check('la mappa arriva a onda e la pagina rientra in fretta', async () => {
  await page.emulateMedia({ reducedMotion: 'no-preference' });
  try {
    await page.evaluate(() => dm.shown(false));
    await reset({ ...two, maps: [{ ...two.maps[0], links: [{ id: 'lone', a: 'nrouter', b: 'nswitch', pa: 'eth1', pb: 'Gi0/1' }] }] }, { mode: 'view' });
    // subito dopo l'apertura gli apparati stanno ancora arrivando, il cavo dietro di loro
    const early = await page.evaluate(() => ({
      router: Number(document.querySelector('[data-id="nrouter"] .dm-node-body').style.opacity || 1),
      cable: Number(document.querySelector('.dm-links [data-id="lone"]').style.opacity || 1),
    }));
    assert(early.router < 1 && early.cable < 1, `niente onda: ${JSON.stringify(early)}`);
    await page.waitForTimeout(1100);
    const late = await page.evaluate(() => ({
      body: document.querySelector('[data-id="nswitch"] .dm-node-body').getAttribute('transform'),
      opacity: document.querySelector('[data-id="nswitch"] .dm-node-body').style.opacity,
      cable: document.querySelector('.dm-links [data-id="lone"]').style.opacity,
    }));
    assert(late.body === null && late.opacity === '' && late.cable === '', `l onda non finisce: ${JSON.stringify(late)}`);

    // nascosta dalla finestra: il contenuto si spegne; aperta un'altra nota intanto, l'onda aspetta
    await page.evaluate(() => dm.hidden());
    assert(await page.evaluate(() => document.querySelector('.dm-app').classList.contains('dm-away')), 'nascosta ma accesa');
    await page.evaluate((d) => dm.load(d, { mode: 'view' }), two);
    await page.waitForTimeout(150);
    assert(!(await page.evaluate(() => document.querySelector('[data-id="nrouter"] .dm-node-body').style.opacity)), 'l onda e\' partita a pagina nascosta');
    await page.evaluate(() => dm.shown(true));
    const back = await page.evaluate(() => ({
      arrive: document.querySelector('.dm-app').classList.contains('dm-arrive'),
      away: document.querySelector('.dm-app').classList.contains('dm-away'),
      wave: Number(document.querySelector('[data-id="nrouter"] .dm-node-body').style.opacity || 1) < 1,
    }));
    assert(back.arrive && !back.away && back.wave, JSON.stringify(back));
    await page.waitForTimeout(1100);
    assert(!(await page.evaluate(() => document.querySelector('.dm-app').classList.contains('dm-arrive'))), 'la dissolvenza resta');
  } finally {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.evaluate(() => dm.shown(false));
  }
});

assert(errors.length === 0, `errori nella pagina: ${errors.join(' | ')}`);
await browser.close();
if (failed) {
  console.log(`${failed} prove fallite`);
  process.exit(1);
}
console.log('tutte le prove passate');
