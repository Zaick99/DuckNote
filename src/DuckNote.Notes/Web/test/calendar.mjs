// Le prove del calendario, in Chromium: si usa come una persona (mouse e tastiera veri) e si
// guardano i cambiamenti che la pagina manda alla finestra. Oggi e' fisso: mercoledi' 14 aprile
// 2027. Uso: node test/calendar.mjs  (screenshot dei fallimenti in test/out/)
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';
import { calendarHtml } from './page.mjs';

const require = createRequire(import.meta.url);
const { chromium } = require('playwright');

const out = new URL('./out/', import.meta.url).pathname;
mkdirSync(out, { recursive: true });

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1240, height: 800 } });
const errors = [];
page.on('pageerror', (e) => errors.push(e.message));
await page.setContent(calendarHtml());

let failed = 0;
async function check(name, fn) {
  try {
    await fn();
    console.log(`ok   ${name}`);
  } catch (e) {
    failed++;
    console.log(`FAIL ${name}: ${e.message}`);
    await page.screenshot({ path: `${out}calendario_${name.replace(/\W+/g, '_')}.png` });
  }
}
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };
const TODAY = '2027-04-14';
const messages = () => page.evaluate(() => dc.outbox());
const ops = async () => (await messages()).filter((m) => m.t === 'op').map((m) => m.op);
const doc = async () => JSON.parse(await page.evaluate(() => dc.doc));
const load = async (data = null, options = {}) => {
  await page.evaluate(([d, o]) => dc.load(d, o), [data, { today: TODAY, ...options }]);
  await messages();
  await page.mouse.move(5, 5);
};
const cell = (day) => `.dc-cell[data-day="${day}"]`;
const pills = (day) => page.$$eval(`${cell(day)} .dc-pill .dc-pill-title`, (els) => els.map((e) => e.textContent));

const item = (fields) => ({ id: `e${Math.random().toString(16).slice(2, 12)}`, kind: 'event', title: 'Voce', date: TODAY, ...fields });

await check('pagina pronta: aprile 2027, oggi segnato, giorno vuoto', async () => {
  await load();
  assert((await page.textContent('.dc-title')) === 'Aprile 2027', await page.textContent('.dc-title'));
  assert((await page.$$('.dc-cell')).length === 42, 'celle');
  assert(await page.getAttribute(cell(TODAY), 'class').then((c) => c.includes('today') && c.includes('selected')), 'oggi');
  assert((await page.textContent('.dc-day-name')) === 'mercoledì', 'giorno nel pannello');
  assert(await page.isVisible('.dc-empty'), 'invito del giorno vuoto');
  // la griglia comincia di lunedi': il 29 marzo
  assert((await page.getAttribute('.dc-cell >> nth=0', 'data-day')) === '2027-03-29', 'primo giorno della griglia');
});

await check('Nuovo apre l editor e Aggiungi manda la voce con ora e promemoria', async () => {
  await load();
  await page.click('.dc-new');
  assert(await page.isVisible('.dc-editor'), 'editor');
  await page.keyboard.type('Riunione con il fornitore');
  await page.click('.dc-editor .dm-button.primary');
  const [op] = await ops();
  assert(op?.op === 'put', JSON.stringify(op));
  assert(op.item.title === 'Riunione con il fornitore' && op.item.date === TODAY && op.item.time === '09:00' && op.item.kind === 'event', JSON.stringify(op.item));
  assert(JSON.stringify(op.item.remind) === '[15]', JSON.stringify(op.item.remind));
  assert((await pills(TODAY)).includes('Riunione con il fornitore'), 'voce nella griglia');
  assert(await page.isVisible('.dc-row:has-text("Riunione con il fornitore")'), 'voce nel pannello');
});

await check('doppio clic su un giorno e Scadenza: tutto il giorno con i suoi promemoria', async () => {
  await load();
  await page.dblclick(cell('2027-04-20'));
  await page.click('.dc-kinds .dc-kind:has-text("Scadenza")');
  assert(await page.isChecked('.dc-editor .dc-switch'), 'tutto il giorno');
  await page.fill('.dc-editor .dm-field.big', 'Scade il dominio');
  await page.keyboard.press('Enter');
  const [op] = await ops();
  assert(op.item.date === '2027-04-20' && op.item.kind === 'deadline' && !op.item.time, JSON.stringify(op.item));
  assert(JSON.stringify(op.item.remind) === '[10080,1440]', JSON.stringify(op.item.remind));
  const cls = await page.getAttribute(`${cell('2027-04-20')} .dc-pill`, 'class');
  assert(cls.includes('allday'), cls);
});

await check('un titolo vuoto non si salva', async () => {
  await load();
  await page.click('.dc-new');
  await page.click('.dc-editor .dm-button.primary');
  assert(await page.isVisible('.dc-problem'), 'messaggio');
  assert((await ops()).length === 0, 'non deve mandare niente');
  await page.keyboard.press('Escape');
  assert(!(await page.isVisible('.dc-editor')), 'Esc chiude');
});

await check('ogni settimana: le volte nel mese e la spunta di una volta', async () => {
  await load({ items: [item({ kind: 'todo', title: 'Backup', date: '2027-04-01', repeat: 'week', remind: [0] })] });
  for (const day of ['2027-04-01', '2027-04-08', '2027-04-15', '2027-04-22', '2027-04-29', '2027-05-06']) {
    assert((await pills(day)).includes('Backup'), `manca il ${day}`);
  }
  assert(!(await pills('2027-03-29')).includes('Backup'), 'prima del primo giorno');
  await page.click(cell('2027-04-08'));
  await page.click('.dc-view > .dc-list .dc-row:has-text("Backup") .dc-check');
  const [op] = await ops();
  assert(op.op === 'done' && op.date === '2027-04-08' && op.on === true, JSON.stringify(op));
  const cls = await page.getAttribute(`${cell('2027-04-08')} .dc-pill`, 'class');
  assert(cls.includes('done'), cls);
});

await check('la scheda di una cosa da fare la segna come fatta', async () => {
  const todo = item({ kind: 'todo', title: 'Toner stampante', date: '2027-04-12' });
  await load({ items: [todo] });
  await page.click(`${cell('2027-04-12')} .dc-pill`);
  assert((await page.textContent('.dc-detail .dc-facts')).includes('2 giorni fa'), 'in ritardo');
  await page.click('.dc-detail .dm-button:has-text("Fatto")');
  const [op] = await ops();
  assert(op.op === 'done' && op.id === todo.id && op.date === '2027-04-12' && op.on === true, JSON.stringify(op));
  assert(await page.isVisible('.dc-detail-title.done') && await page.isVisible('.dc-detail .dm-button:has-text("Da fare di nuovo")'), 'segnata');
});

await check('ogni mese il 31: il 30 aprile e di nuovo il 31 maggio', async () => {
  await load({ items: [item({ title: 'Fattura', date: '2027-01-31', repeat: 'month' })] });
  assert((await pills('2027-03-31')).includes('Fattura'), '31 marzo');
  assert((await pills('2027-04-30')).includes('Fattura'), '30 aprile');
  await page.keyboard.press('PageDown');
  assert((await page.textContent('.dc-title')) === 'Maggio 2027', 'mese dopo');
  assert((await pills('2027-05-31')).includes('Fattura'), '31 maggio');
});

await check('eliminare una volta sola e poi annullare', async () => {
  const weekly = item({ title: 'Stand-up', date: '2027-04-05', time: '09:15', repeat: 'week' });
  await load({ items: [weekly] });
  await page.click(`${cell('2027-04-19')} .dc-pill`);
  await page.click('.dc-detail .dc-edit');
  await page.click('.dc-editor .dm-button.danger');
  await page.click('.dm-menu .dm-menu-row:has-text("Solo la volta")');
  let [op] = await ops();
  assert(op.op === 'skip' && op.id === weekly.id && op.date === '2027-04-19', JSON.stringify(op));
  assert(!(await pills('2027-04-19')).includes('Stand-up') && (await pills('2027-04-26')).includes('Stand-up'), 'solo quella volta');
  await page.click('.dc-toast .dm-link-button');
  [op] = await ops();
  assert(op.op === 'put' && (op.item.skip ?? []).length === 0, JSON.stringify(op));
  assert((await pills('2027-04-19')).includes('Stand-up'), 'tornata');
});

await check('un clic su una voce mostra la scheda, la matita la modifica', async () => {
  const meeting = item({
    title: 'Manutenzione UPS', date: '2027-04-05', time: '18:00', repeat: 'week', remind: [60], notes: 'portare le batterie\nchiave della sala server',
    link: { kind: 'host', host: '192.168.40.10', name: 'Server' },
  });
  await load({ items: [meeting] }, { hosts: [{ key: '192.168.40.10', ip: '192.168.40.10', name: 'Server', state: 'up' }] });
  await page.click(`${cell('2027-04-19')} .dc-pill`);
  assert(await page.isVisible('.dc-detail') && !(await page.isVisible('.dc-editor')), 'scheda, non modifica');
  assert((await page.textContent('.dc-detail-title')) === 'Manutenzione UPS', 'titolo');
  const facts = await page.textContent('.dc-detail .dc-facts');
  assert(facts.includes('lunedì 19 aprile 2027, alle 18:00') && facts.includes('Ogni settimana') && facts.includes('1 ora prima'), facts);
  assert((await page.textContent('.dc-notes')) === 'portare le batterie\nchiave della sala server', 'appunti');
  assert(await page.isVisible('.dc-detail-link .dm-dot.st-up'), 'stato dell host');
  await page.click('.dc-detail-link');
  const reveal = (await messages()).find((m) => m.t === 'reveal');
  assert(reveal?.host === '192.168.40.10', JSON.stringify(reveal));
  // dalla tastiera: E apre la modifica, Annulla torna alla scheda
  await page.keyboard.press('e');
  assert(await page.isVisible('.dc-editor') && (await page.inputValue('.dc-editor .dm-field.big')) === 'Manutenzione UPS', 'modifica');
  await page.click('.dc-editor .dm-button:has-text("Annulla")');
  assert(await page.isVisible('.dc-detail') && !(await page.isVisible('.dc-editor')), 'di nuovo la scheda');
  // la matita, una modifica, Salva: si torna alla scheda aggiornata
  await page.click('.dc-detail .dc-edit');
  await page.fill('.dc-editor .dm-field.big', 'Manutenzione UPS sala server');
  await page.click('.dc-editor .dm-button.primary');
  const [op] = await ops();
  assert(op?.op === 'put' && op.item.title === 'Manutenzione UPS sala server', JSON.stringify(op));
  assert((await page.textContent('.dc-detail-title')) === 'Manutenzione UPS sala server', 'scheda aggiornata');
  await page.keyboard.press('Escape');
  assert(await page.isVisible('.dc-day-head'), 'Esc torna al giorno');
});

await check('trascinare una voce la sposta a un altro giorno', async () => {
  const call = item({ title: 'Chiamata', time: '11:00' });
  await load({ items: [call] });
  const from = await page.locator(`${cell(TODAY)} .dc-pill`).boundingBox();
  const to = await page.locator(cell('2027-04-16')).boundingBox();
  await page.mouse.move(from.x + 20, from.y + 8);
  await page.mouse.down();
  await page.mouse.move(to.x + 40, to.y + 40, { steps: 10 });
  await page.mouse.up();
  const [op] = await ops();
  assert(op?.op === 'put' && op.item.id === call.id && op.item.date === '2027-04-16', JSON.stringify(op));
  assert((await pills('2027-04-16')).includes('Chiamata') && !(await pills(TODAY)).includes('Chiamata'), 'spostata');
});

await check('i certificati delle analisi: scheda, rinnovato, ignora e impostazioni', async () => {
  await load({ items: [], certs: { known: [{ host: 'www.example.com', date: '2027-04-20', subject: 'CN=www.example.com', issuer: 'Example CA' }] } });
  const cls = await page.getAttribute(`${cell('2027-04-20')} .dc-pill`, 'class');
  assert(cls.includes('cert'), cls);
  await page.click(`${cell('2027-04-20')} .dc-pill`);
  assert((await page.textContent('.dc-cert-title')) === 'Certificato di www.example.com', 'titolo');
  assert((await page.textContent('.dc-facts')).includes('Example CA'), 'emittente');
  await page.click('.dc-view .dm-button:has-text("Rinnovato")');
  let [op] = await ops();
  assert(op.op === 'done' && op.id === 'cert:www.example.com' && op.on === true, JSON.stringify(op));
  await page.click('.dc-view .dm-button:has-text("Ignora questo host")');
  [op] = await ops();
  assert(op.op === 'ignore' && op.host === 'www.example.com' && op.on === true, JSON.stringify(op));
  assert((await pills('2027-04-20')).length === 0, 'sparito dalla griglia');
  await page.click('.dc-toolbar .dm-tool:has-text("Certificati")');
  await page.click('.dc-cert-settings .dm-link-button:has-text("Riprendi")');
  [op] = await ops();
  assert(op.op === 'ignore' && op.on === false, JSON.stringify(op));
  await page.click('.dc-cert-settings .dc-switch');
  [op] = await ops();
  assert(op.op === 'certs' && op.off === true, JSON.stringify(op));
  await page.keyboard.press('Escape');
});

await check('il legame con un host o un apparato della mappa', async () => {
  await load(null, {
    hosts: [{ key: '192.168.40.10', ip: '192.168.40.10', name: 'server.example.local', state: 'up' }, { key: 'nas01.example.local', ip: '192.168.40.20', name: 'nas01.example.local', state: 'down' }],
    devices: [{ map: 'm1', mapName: 'Ufficio', node: 'ncore', name: 'Switch core', host: '192.168.10.2', kind: 'switch' }],
  });
  await page.click('.dc-new');
  await page.keyboard.type('Aggiornare il firmware');
  await page.click('.dc-editor .dm-button:has-text("Collega")');
  assert((await page.$$('.dc-pick-row')).length === 3, 'host e apparati');
  await page.keyboard.type('switch');
  await page.keyboard.press('Enter');
  assert((await page.textContent('.dc-link-name')) === 'Switch core', 'legame scelto');
  await page.click('.dc-link-text');
  const reveal = (await messages()).find((m) => m.t === 'node');
  assert(reveal?.node === 'ncore' && reveal.map === 'm1', JSON.stringify(reveal));
  await page.click('.dc-editor .dm-button.primary');
  const [op] = await ops();
  assert(op.item.link?.kind === 'node' && op.item.link.node === 'ncore' && op.item.link.host === '192.168.10.2', JSON.stringify(op.item.link));
  assert((await page.textContent('.dc-row .dc-row-bit.link')).includes('Switch core'), 'legame nella riga');
});

await check('la ricerca trova le voci e Esc la chiude', async () => {
  await load({ items: [item({ title: 'Rinnovo licenze', date: '2027-06-02', notes: 'firewall perimetrale' }), item({ title: 'Pranzo' })] });
  await page.click('.dm-search');
  await page.keyboard.type('firewall');
  assert((await page.textContent('.dc-section-title')).includes('Ricerca'), 'pannello della ricerca');
  const rows = await page.$$eval('.dc-row .dc-row-title', (els) => els.map((e) => e.textContent));
  assert(rows.join(',') === 'Rinnovo licenze', rows.join(','));
  await page.click('.dc-row:has-text("Rinnovo licenze")');
  assert((await page.textContent('.dc-title')) === 'Giugno 2027' && await page.isVisible('.dc-detail'), 'aperta al suo giorno');
  await page.keyboard.press('Escape');
  await page.click('.dm-search');
  await page.keyboard.press('Escape');
  assert(await page.isVisible('.dc-day-head'), 'di nuovo il giorno');
});

await check('in ritardo e in arrivo nel pannello', async () => {
  await load({ items: [
    item({ kind: 'todo', title: 'Toner stampante', date: '2027-04-10' }),
    item({ kind: 'todo', title: 'Gia fatto', date: '2027-04-11', done: ['2027-04-11'] }),
    item({ title: 'Riunione passata', date: '2027-04-12', time: '10:00' }),
    item({ title: 'Dentista', date: '2027-04-18', time: '16:30' }),
  ] });
  const late = await page.$$eval('.dc-section-title.late + .dc-list .dc-row-title', (els) => els.map((e) => e.textContent));
  assert(late.join(',') === 'Toner stampante', late.join(','));
  const ahead = await page.$$eval('.dc-sections .dc-list:last-child .dc-row-title', (els) => els.map((e) => e.textContent));
  assert(ahead.join(',') === 'Dentista', ahead.join(','));
});

await check('tastiera: frecce, pagine, T e N', async () => {
  await load();
  await page.click(cell(TODAY));
  await page.keyboard.press('ArrowRight');
  await page.keyboard.press('ArrowDown');
  assert(await page.getAttribute(cell('2027-04-22'), 'class').then((c) => c.includes('selected')), 'selezione spostata');
  await page.keyboard.press('PageUp');
  assert((await page.textContent('.dc-title')) === 'Marzo 2027', 'mese prima');
  await page.keyboard.press('t');
  assert((await page.textContent('.dc-title')) === 'Aprile 2027' && await page.getAttribute(cell(TODAY), 'class').then((c) => c.includes('selected')), 'oggi');
  await page.keyboard.press('n');
  assert(await page.isVisible('.dc-editor'), 'nuova voce');
  await page.keyboard.press('Escape');
});

await check('una voce aperta da fuori: compose con il legame', async () => {
  await load();
  await page.evaluate(() => dc.compose({ kind: 'deadline', title: 'Rinnovare il certificato di www.example.com', date: '2027-05-03', link: { kind: 'host', host: 'www.example.com' } }));
  assert((await page.inputValue('.dc-editor .dm-field.big')) === 'Rinnovare il certificato di www.example.com', 'titolo');
  assert((await page.inputValue('.dc-editor input[type="date"]')) === '2027-05-03', 'giorno');
  assert((await page.textContent('.dc-title')) === 'Maggio 2027', 'mese');
  assert((await page.textContent('.dc-link-name')) === 'www.example.com', 'legame');
  await page.keyboard.press('Escape');
});

await check('il calendario aggiornato dalla finestra chiude l editor di una voce sparita', async () => {
  const gone = item({ title: 'Da togliere' });
  await load({ items: [gone] });
  await page.click(`${cell(TODAY)} .dc-pill`);
  await page.click('.dc-detail .dc-edit');
  assert(await page.isVisible('.dc-editor'), 'editor aperto');
  await page.evaluate(() => dc.data({ items: [] }));
  assert(!(await page.isVisible('.dc-editor')) && (await pills(TODAY)).length === 0, 'editor chiuso');
});

await check('sola lettura: niente si modifica', async () => {
  await load({ items: [item({ title: 'Ferma' })] }, { readonly: true });
  assert(await page.isVisible('.dm-chip:has-text("Sola lettura")') && await page.isDisabled('.dc-new'), 'Nuovo spento');
  await page.click(`${cell(TODAY)} .dc-pill`);
  assert(await page.isVisible('.dc-detail') && !(await page.isVisible('.dc-detail .dc-edit')), 'la scheda senza matita');
  await page.keyboard.press('e');
  assert(!(await page.isVisible('.dc-editor')), 'niente modifica');
  await page.dblclick(cell('2027-04-21'));
  assert((await ops()).length === 0, 'niente cambiamenti');
});

await check('documenti rovinati non rompono il calendario', async () => {
  await load({ items: [{ id: 'X', date: 'ieri' }, null, 5, { title: 'ok', date: '2027-04-14', kind: 'boh', remind: [-1, 'a', 30] }], certs: 'no' });
  assert((await pills(TODAY)).join() === 'ok', 'una voce buona');
  const d = await doc();
  assert(d.items[0].kind === 'event' && JSON.stringify(d.items[0].remind) === '[30]', JSON.stringify(d.items[0]));
  await load('{ non e json');
});

await check('dc.clear dimentica tutto', async () => {
  await load({ items: [item({ title: 'Segreto' })] }, { hosts: [{ key: 'a', ip: '10.0.0.1', name: 'a', state: 'up' }] });
  await page.evaluate(() => dc.clear());
  assert((await page.$$('.dc-pill')).length === 0 && (await doc()).items.length === 0, 'resta qualcosa');
  assert((await page.evaluate(() => dc.state.hosts.length)) === 0, 'host');
});

assert(errors.length === 0, `errori nella pagina: ${errors.join(' | ')}`);
await browser.close();
if (failed) {
  console.log(`${failed} prove fallite`);
  process.exit(1);
}
console.log('tutte le prove del calendario passate');
