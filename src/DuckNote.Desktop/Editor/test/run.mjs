// Le prove dell'editor, in Chromium: si scrive come una persona (tastiera vera) e si guarda il
// documento che ne esce. Uso: npm test  (screenshot in test/out/)
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';
import { pageHtml } from './page.mjs';

const require = createRequire(import.meta.url);
const { chromium } = require('playwright');

const out = new URL('./out/', import.meta.url).pathname;
mkdirSync(out, { recursive: true });

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 900, height: 700 } });
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
const doc = () => page.evaluate(() => JSON.parse(dn.doc()));
const types = async () => (await doc()).content.map((b) => b.type + (b.attrs?.level ? b.attrs.level : '') + (b.attrs?.done ? '+' : ''));
const texts = async () => (await doc()).content.map((b) => (b.content ?? []).map((i) => i.text ?? '').join(''));
const reset = async (json) => {
  await page.evaluate((d) => dn.load(d ?? { type: 'doc', content: [{ type: 'paragraph' }] }), json);
  await page.click('.ProseMirror');
};
const mod = process.platform === 'darwin' ? 'Meta' : 'Control';

await check('battitura veloce con Invio resta in ordine', async () => {
  await reset();
  for (let i = 1; i <= 8; i++) {
    await page.keyboard.type(`riga ${i} con testo veloce`, { delay: 0 });
    if (i < 8) await page.keyboard.press('Enter');
  }
  const t = await texts();
  assert(t.length === 8 && t.every((line, i) => line === `riga ${i + 1} con testo veloce`), JSON.stringify(t));
});

await check('indirizzi uno sotto l altro tutti colorati', async () => {
  await reset();
  await page.evaluate(() => dn.hosts({ '10.0.0.1': 'up', '10.0.0.2': 'down' }, []));
  const ips = ['10.0.0.1', '10.0.0.2', '10.0.0.3', 'nas.casa.lan', '192.168.1.254'];
  for (const [i, ip] of ips.entries()) {
    await page.keyboard.type(ip);
    if (i < ips.length - 1) await page.keyboard.press('Enter');
  }
  await page.waitForTimeout(900);
  const hosts = await page.$$eval('.dn-host', (els) => els.map((e) => `${e.textContent}:${[...e.classList].find((c) => c.startsWith('dn-host-'))}`));
  assert(hosts.length === 5, JSON.stringify(hosts));
  assert(hosts.includes('10.0.0.1:dn-host-up') && hosts.includes('10.0.0.2:dn-host-down') && hosts.includes('nas.casa.lan:dn-host-unknown'), JSON.stringify(hosts));
});

await check('tre nomi scritti di fila: i primi subito, l ultimo quando ci si ferma, e la finestra lo sa', async () => {
  await reset();
  await page.evaluate(() => { dn.settle(800); dn.outbox(); });
  await page.keyboard.type('A04523.aouts.it A03344.aouts.it A04394.aouts.it', { delay: 15 });
  await page.waitForTimeout(400);
  const early = await page.$$eval('.dn-host', (els) => els.map((e) => e.textContent));
  const changed = (await page.evaluate(() => dn.outbox())).filter((m) => m.t === 'changed').pop();
  assert(JSON.stringify(early) === JSON.stringify(['A04523.aouts.it', 'A03344.aouts.it']), JSON.stringify(early));
  assert(changed?.typing === 'A04394.aouts.it', JSON.stringify(changed?.typing));
  await page.waitForTimeout(900);
  const late = await page.$$eval('.dn-host', (els) => els.map((e) => e.textContent));
  const settled = (await page.evaluate(() => dn.outbox())).some((m) => m.t === 'settled');
  await page.evaluate(() => dn.settle(700));
  assert(late.length === 3 && settled, JSON.stringify({ late, settled }));
});

await check('la parola che si sta scrivendo non e ancora un indirizzo', async () => {
  await reset();
  await page.keyboard.type('server 10.0.0', { delay: 20 });
  const now = await page.$$eval('.dn-host', (els) => els.length);
  assert(now === 0, `colorato mentre scrivo: ${now}`);
  await page.keyboard.type('.9', { delay: 20 });
  await page.waitForTimeout(900);
  const later = await page.$$eval('.dn-host', (els) => els.map((e) => e.textContent));
  assert(later.length === 1 && later[0] === '10.0.0.9', JSON.stringify(later));
});

await check('elenco: "- " fa la voce, Maiusc+Invio la voce dopo, Invio torna al testo', async () => {
  await reset();
  await page.keyboard.type('- uno');
  await page.keyboard.press('Shift+Enter');
  await page.keyboard.type('due');
  await page.keyboard.press('Enter');
  await page.keyboard.type('testo');
  const t = await types();
  assert(JSON.stringify(t) === JSON.stringify(['bullet', 'bullet', 'paragraph']), JSON.stringify(t));
  assert(JSON.stringify(await texts()) === JSON.stringify(['uno', 'due', 'testo']), JSON.stringify(await texts()));
});

await check('Backspace all inizio di una voce toglie il pallino e non incolla la riga sopra', async () => {
  await reset();
  await page.keyboard.type('prima');
  await page.keyboard.press('Enter');
  await page.keyboard.type('- voce');
  await page.keyboard.press('Home');
  await page.keyboard.press('Backspace');
  assert(JSON.stringify(await types()) === JSON.stringify(['paragraph', 'paragraph']), JSON.stringify(await types()));
  assert(JSON.stringify(await texts()) === JSON.stringify(['prima', 'voce']), JSON.stringify(await texts()));
});

await check('numeri contano da 1 e ripartono dopo una riga diversa', async () => {
  await reset();
  await page.keyboard.type('1. alfa');
  await page.keyboard.press('Shift+Enter');
  await page.keyboard.type('beta');
  await page.keyboard.press('Shift+Enter');
  await page.keyboard.type('gamma');
  assert(JSON.stringify(await types()) === JSON.stringify(['number', 'number', 'number']), JSON.stringify(await types()));
  // il numero lo scrive il CSS: si guarda il disegno (test/out/numeri.png)
  await page.locator('.ProseMirror').screenshot({ path: `${out}numeri.png` });
});

await check('spunta: "[] " la crea e un clic sul quadratino la segna fatta', async () => {
  await reset();
  await page.keyboard.type('[] comprare il cavo');
  const box = await page.$('.dn-todo');
  const rect = await box.boundingBox();
  await page.mouse.click(rect.x + 8, rect.y + rect.height / 2);
  assert(JSON.stringify(await types()) === JSON.stringify(['todo+']), JSON.stringify(await types()));
});

await check('Ctrl+B senza selezione mette in grassetto la parola sotto il cursore', async () => {
  await reset();
  await page.keyboard.type('una parola qui');
  for (let i = 0; i < 6; i++) await page.keyboard.press('ArrowLeft');
  await page.keyboard.press(`${mod}+b`);
  const d = await doc();
  const bold = d.content[0].content.filter((i) => i.marks?.some((m) => m.type === 'bold')).map((i) => i.text);
  assert(JSON.stringify(bold) === JSON.stringify(['parola']), JSON.stringify(d.content[0].content));
});

await check('Ctrl+B in fondo a una parola appena scritta in grassetto smette il grassetto e non lo toglie', async () => {
  await reset();
  await page.keyboard.type('con ');
  await page.keyboard.press(`${mod}+b`);
  await page.keyboard.type('forte');
  await page.keyboard.press(`${mod}+b`);
  await page.keyboard.type(' dopo');
  const d = await doc();
  const parts = d.content[0].content.map((i) => `${(i.marks ?? []).map((m) => m.type).join('+')}:${i.text}`);
  assert(JSON.stringify(parts) === JSON.stringify([':con ', 'bold:forte', ': dopo']), JSON.stringify(parts));
});

await check('Invio dopo il grassetto riparte pulito, Maiusc+Invio lo tiene', async () => {
  await reset();
  await page.keyboard.press(`${mod}+b`);
  await page.keyboard.type('forte');
  await page.keyboard.press('Enter');
  await page.keyboard.type('pulito');
  await page.keyboard.press('Shift+Enter');
  await page.keyboard.type('ancora');
  const d = await doc();
  const marks = d.content.map((b) => (b.content ?? []).map((i) => (i.marks ?? []).map((m) => m.type).join('+')).join('|'));
  assert(JSON.stringify(marks) === JSON.stringify(['bold', '', '']), JSON.stringify(marks));
});

await check('Annulla e Ripeti', async () => {
  await reset();
  await page.keyboard.type('primo');
  await page.waitForTimeout(700);
  await page.keyboard.type(' secondo');
  await page.keyboard.press(`${mod}+z`);
  assert((await texts())[0] === 'primo', JSON.stringify(await texts()));
  await page.keyboard.press(`${mod}+y`);
  assert((await texts())[0] === 'primo secondo', JSON.stringify(await texts()));
});

await check('incollare piu righe da fuori: testo semplice, una riga per riga', async () => {
  await reset();
  await page.evaluate(() => {
    const data = new DataTransfer();
    data.setData('text/plain', 'a 10.0.0.1\nb 10.0.0.2\nc');
    data.setData('text/html', '<b>a</b> 10.0.0.1<br>b 10.0.0.2<br>c');
    dn.view.dom.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
  });
  const t = await texts();
  assert(JSON.stringify(t) === JSON.stringify(['a 10.0.0.1', 'b 10.0.0.2', 'c']), JSON.stringify(t));
  const marks = (await doc()).content[0].content.some((i) => i.marks);
  assert(!marks, 'il grassetto di fuori e passato');
});

await check('"``` " fa un blocco di codice colorato, Maiusc+Invio resta dentro, Invio esce', async () => {
  await reset();
  await page.keyboard.type('``` var x = 1;');
  await page.keyboard.press('Shift+Enter');
  await page.keyboard.type('public void Run() { }');
  await page.keyboard.press('Enter');
  await page.keyboard.type('dopo');
  const t = await types();
  assert(JSON.stringify(t) === JSON.stringify(['code_block', 'paragraph']), JSON.stringify(t));
  const inks = await page.$$eval('[class^="dn-ink-"]', (els) => els.map((e) => e.className));
  assert(inks.includes('dn-ink-keyword'), JSON.stringify(inks));
});

await check('"---" e Invio fanno una separazione', async () => {
  await reset();
  await page.keyboard.type('---');
  await page.keyboard.press('Enter');
  await page.keyboard.type('sotto');
  assert(JSON.stringify(await types()) === JSON.stringify(['rule', 'paragraph']), JSON.stringify(await types()));
});

await check('tabella: Tab passa alla cella dopo', async () => {
  await reset();
  await page.evaluate(() => dn.cmd('table'));
  await page.keyboard.type('a');
  await page.keyboard.press('Tab');
  await page.keyboard.type('b');
  const d = await doc();
  const table = d.content.find((b) => b.type === 'table');
  assert(d.content[0].type === 'table', 'la tabella non ha preso il posto della riga vuota');
  const cells = table.content[1].content.map((c) => c.content[0].content?.[0]?.text ?? '');
  assert(JSON.stringify(cells) === JSON.stringify(['a', 'b', '']), JSON.stringify(cells));
});

await check('cerca: Ctrl+F apre, conta e segna la corrente', async () => {
  await reset();
  await page.keyboard.type('gatto cane gatto topo gatto');
  await page.keyboard.press(`${mod}+f`);
  await page.keyboard.type('gatto');
  const count = await page.textContent('.dn-find-count');
  assert(count === '1/3' || count === '3/3', count);
  await page.keyboard.press('Enter');
  const current = await page.$$eval('.dn-found-current', (els) => els.length);
  assert(current === 1, `correnti: ${current}`);
  await page.keyboard.press('Escape');
  assert(await page.$eval('.dn-findbar', (e) => e.hidden || e.classList.contains('dn-leaving')), 'la barra non si chiude');
});

await check('i tasti della finestra arrivano alla finestra', async () => {
  await reset();
  await page.evaluate(() => dn.outbox());
  await page.keyboard.press('F9');
  await page.keyboard.press(`${mod}+l`);
  const keys = (await page.evaluate(() => dn.outbox())).filter((m) => m.t === 'key').map((m) => m.key);
  assert(JSON.stringify(keys) === JSON.stringify(['sidebar', 'lock']), JSON.stringify(keys));
});

await check('2000 righe: si apre e si scrive senza fermarsi', async () => {
  const content = Array.from({ length: 2000 }, (_, i) => ({ type: 'paragraph', content: [{ type: 'text', text: `Riga ${i}: host 192.168.${i % 250}.${i % 200} e testo di prova` }] }));
  const t0 = Date.now();
  await page.evaluate((c) => dn.load({ type: 'doc', content: c }), content);
  const load = Date.now() - t0;
  await page.click('.ProseMirror');
  const t1 = Date.now();
  await page.keyboard.type('scrivo in fretta in una nota lunga', { delay: 0 });
  const typing = Date.now() - t1;
  console.log(`      apertura ${load} ms, 34 tasti ${typing} ms`);
  assert(load < 4000 && typing < 3000, `troppo lento: ${load} / ${typing}`);
});

await page.evaluate(() => dn.load({ type: 'doc', content: [
  { type: 'heading', attrs: { level: 1 }, content: [{ type: 'text', text: 'DuckNote' }] },
  { type: 'paragraph', content: [{ type: 'text', text: 'Il gateway 192.168.1.1, il NAS nas.casa.lan, ' }, { type: 'text', text: 'grassetto', marks: [{ type: 'bold' }] }, { type: 'text', text: ', ' }, { type: 'text', text: 'evidenziato', marks: [{ type: 'highlight' }] }, { type: 'text', text: ', ' }, { type: 'text', text: 'codice', marks: [{ type: 'code' }] }, { type: 'text', text: ', ' }, { type: 'text', text: 'rosso', marks: [{ type: 'colour', attrs: { token: 'Red' } }] }, { type: 'text', text: ', ' }, { type: 'text', text: 'un link', marks: [{ type: 'link', attrs: { href: 'https://example.com' } }] }] },
  { type: 'heading', attrs: { level: 2 }, content: [{ type: 'text', text: 'Elenchi' }] },
  { type: 'bullet', content: [{ type: 'text', text: 'una voce' }] },
  { type: 'number', content: [{ type: 'text', text: 'primo' }] },
  { type: 'number', content: [{ type: 'text', text: 'secondo' }] },
  { type: 'todo', attrs: { done: true }, content: [{ type: 'text', text: 'fatta' }] },
  { type: 'todo', attrs: { done: false }, content: [{ type: 'text', text: 'da fare' }] },
  { type: 'quote', content: [{ type: 'text', text: 'Una citazione, per le cose dette da altri.' }] },
  { type: 'rule' },
  { type: 'code_block', content: [{ type: 'text', text: 'public void Run()\n{\n    // commento\n    var x = "testo" + 42;\n}' }] },
  { type: 'table', content: [
    { type: 'table_row', content: [{ type: 'table_header', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'Host' }] }] }, { type: 'table_header', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'Stato' }] }] }] },
    { type: 'table_row', content: [{ type: 'table_cell', content: [{ type: 'paragraph', content: [{ type: 'text', text: '10.0.0.1' }] }] }, { type: 'table_cell', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'attivo' }] }] }] }] },
] }, { title: 'Prova', meta: '42 parole  ·  pagina 1 di 1' }));
await page.evaluate(() => dn.hosts({ '192.168.1.1': 'up', 'nas.casa.lan': 'down', '10.0.0.1': 'up' }, []));
await page.waitForTimeout(300);
await page.screenshot({ path: `${out}aspetto.png` });

if (errors.length) { failed++; console.log('ERRORI JS:', errors.join('\n')); }
await browser.close();
console.log(failed ? `${failed} prove fallite` : 'tutte le prove passate');
process.exit(failed ? 1 : 0);
