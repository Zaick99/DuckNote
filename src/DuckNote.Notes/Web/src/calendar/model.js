// Il calendario della nota, nel formato di Agenda.cs (calendario.json) e con le stesse regole:
// le ripetizioni, le scadenze dei certificati come voci virtuali, i cambiamenti (op). La finestra
// tiene il calendario vero e applica gli stessi cambiamenti; la pagina li applica subito anche
// per conto suo, per non aspettare, e poi riceve dalla finestra il calendario aggiornato.

export const KINDS = [
  { id: 'event', label: 'Appuntamento', tone: 'Blue' },
  { id: 'todo', label: 'Da fare', tone: 'Green' },
  { id: 'deadline', label: 'Scadenza', tone: 'Red' },
  { id: 'note', label: 'Appunto', tone: 'Purple' },
];
export const KIND = Object.fromEntries(KINDS.map((kind) => [kind.id, kind]));

export const CERT_PREFIX = 'cert:';
export const CERT_DEFAULTS = [43200, 10080, 1440];
/** I promemoria delle voci di tutto il giorno suonano alle 9. */
export const ALL_DAY_AT = '09:00';

const MOST_ITEMS = 5000;
const MOST_CERTS = 500;
const MOST_DATES = 2000;
const MOST_REMINDERS = 8;
const LONGEST_REMINDER = 525600;

// ------------------------------------------------------------------ giorni (sempre "AAAA-MM-GG")

const DAY = /^(\d{4})-(\d{2})-(\d{2})$/;
const MS = 86400000;

/** Il giorno se e' scritto bene ed esiste (anni 1900-2200), altrimenti null. */
export function parseDay(text) {
  const m = DAY.exec(String(text ?? ''));
  if (!m) return null;
  const [y, mo, d] = [Number(m[1]), Number(m[2]), Number(m[3])];
  if (y < 1900 || y > 2200 || mo < 1 || mo > 12 || d < 1 || d > daysIn(y, mo)) return null;
  return `${m[1]}-${m[2]}-${m[3]}`;
}

const pad = (n, width = 2) => String(n).padStart(width, '0');
export const makeDay = (y, m, d) => `${pad(y, 4)}-${pad(m)}-${pad(d)}`;
export const partsOf = (day) => day.split('-').map(Number);
export const daysIn = (y, m) => new Date(Date.UTC(y, m, 0)).getUTCDate();
export const dayNumber = (day) => {
  const [y, m, d] = partsOf(day);
  return Math.floor(Date.UTC(y, m - 1, d) / MS);
};
const fromNumber = (n) => {
  const date = new Date(n * MS);
  return makeDay(date.getUTCFullYear(), date.getUTCMonth() + 1, date.getUTCDate());
};
export const addDays = (day, n) => fromNumber(dayNumber(day) + n);

/** Come DateOnly.AddMonths: un giorno che non c'e' nel mese diventa l'ultimo del mese. */
export function addMonths(day, n) {
  const [y, m, d] = partsOf(day);
  const total = y * 12 + (m - 1) + n;
  const year = Math.floor(total / 12);
  const month = total - year * 12 + 1;
  return makeDay(year, month, Math.min(d, daysIn(year, month)));
}

/** 0 = lunedi' ... 6 = domenica. */
export const weekday = (day) => (((dayNumber(day) + 3) % 7) + 7) % 7;

/** Il giorno di oggi nel fuso del computer. */
export function localToday(now = new Date()) {
  return makeDay(now.getFullYear(), now.getMonth() + 1, now.getDate());
}

const TIME = /^([01]\d|2[0-3]):([0-5]\d)$/;
export const parseTime = (text) => (TIME.test(String(text ?? '')) ? String(text) : '');

// ------------------------------------------------------------------ lettura

const str = (value) => (typeof value === 'string' ? value : '');
const isId = (id) => /^[a-z0-9]{2,24}$/.test(id);

function clean(text, most) {
  const value = str(text).replace(/[\u0000-\u001f\u007f-\u009f]/g, '').trim();
  return value.length > most ? value.slice(0, most) : value;
}

function cleanNotes(text) {
  const value = str(text).replace(/\r\n/g, '\n').replace(/[\u0000-\u0008\u000b-\u001f\u007f-\u009f]/g, '').trim();
  return value.length > 4000 ? value.slice(0, 4000) : value;
}

/** Come Agenda.Reminders: minuti fra 0 e un anno, senza doppi, dal piu' lontano. */
export function reminders(list) {
  if (!Array.isArray(list)) return [];
  const seen = new Set();
  for (const value of list) {
    if (Number.isInteger(value) && value >= 0 && value <= LONGEST_REMINDER) seen.add(value);
  }
  return [...seen].sort((a, b) => b - a).slice(0, MOST_REMINDERS);
}

export function newId() {
  const bytes = new Uint8Array(5);
  crypto.getRandomValues(bytes);
  return `e${[...bytes].map((b) => b.toString(16).padStart(2, '0')).join('')}`;
}

function readLink(link) {
  if (!link || typeof link !== 'object') return null;
  const host = clean(link.host, 253);
  const name = clean(link.name, 120);
  if (link.kind === 'node') {
    const node = clean(link.node, 40);
    return node ? { kind: 'node', host, map: clean(link.map, 40), node, name } : null;
  }
  return host ? { kind: 'host', host, map: '', node: '', name } : null;
}

/** Una voce dal file o dall'editor; null se non ha un giorno valido. */
export function normalizeItem(raw) {
  if (!raw || typeof raw !== 'object') return null;
  const date = parseDay(raw.date);
  if (!date) return null;
  const repeat = ['day', 'week', 'month', 'year'].includes(raw.repeat) ? raw.repeat : '';
  const until = repeat ? parseDay(raw.until) : null;
  const days = (list) => (Array.isArray(list) ? list.slice(0, MOST_DATES).map(parseDay).filter((day) => day && day >= date) : []);
  return {
    id: isId(str(raw.id)) ? raw.id : newId(),
    kind: KIND[raw.kind] ? raw.kind : 'event',
    title: clean(raw.title, 200),
    date,
    time: parseTime(raw.time),
    notes: cleanNotes(raw.notes),
    remind: reminders(raw.remind),
    repeat,
    every: Number.isInteger(raw.every) ? Math.min(99, Math.max(1, raw.every)) : 1,
    until: until && until >= date ? until : '',
    done: [...new Set(days(raw.done))].sort(),
    skip: repeat ? [...new Set(days(raw.skip))].sort() : [],
    link: readLink(raw.link),
  };
}

/** Il calendario come lo manda la finestra (o rovinato: allora vuoto). */
export function normalizeDoc(raw) {
  const doc = { items: [], certs: { off: false, remind: [...CERT_DEFAULTS], known: [], ignored: [], done: [] } };
  if (!raw || typeof raw !== 'object') return doc;
  const ids = new Set();
  for (const value of Array.isArray(raw.items) ? raw.items.slice(0, MOST_ITEMS) : []) {
    const item = normalizeItem(value);
    if (!item) continue;
    if (ids.has(item.id)) item.id = newId();
    ids.add(item.id);
    doc.items.push(item);
  }
  const certs = raw.certs && typeof raw.certs === 'object' ? raw.certs : {};
  doc.certs.off = certs.off === true;
  if (Array.isArray(certs.remind)) doc.certs.remind = reminders(certs.remind);
  const hosts = new Set();
  for (const seen of Array.isArray(certs.known) ? certs.known.slice(0, MOST_CERTS) : []) {
    const host = clean(seen?.host, 253);
    const date = parseDay(seen?.date);
    if (!host || !date || hosts.has(host.toLowerCase())) continue;
    hosts.add(host.toLowerCase());
    doc.certs.known.push({ host, date, subject: clean(seen.subject, 300), issuer: clean(seen.issuer, 300), name: clean(seen.name, 120) });
  }
  doc.certs.ignored = [...new Set((Array.isArray(certs.ignored) ? certs.ignored : []).map((host) => clean(host, 253)).filter(Boolean))];
  doc.certs.done = [...new Set((Array.isArray(certs.done) ? certs.done : []).filter((key) => typeof key === 'string' && key.includes('@') && key.length <= 270))];
  return doc;
}

/** La voce come va nel file e alla finestra (i campi vuoti non si scrivono, come in Agenda.cs). */
export function writeItem(item) {
  const out = { id: item.id, kind: item.kind, title: item.title, date: item.date };
  if (item.time) out.time = item.time;
  if (item.notes) out.notes = item.notes;
  if (item.remind.length) out.remind = [...item.remind];
  if (item.repeat) {
    out.repeat = item.repeat;
    if (item.every > 1) out.every = item.every;
    if (item.until) out.until = item.until;
    if (item.skip.length) out.skip = [...item.skip];
  }
  if (item.done.length) out.done = [...item.done];
  if (item.link) {
    const link = { kind: item.link.kind };
    for (const key of ['host', 'map', 'node', 'name']) if (item.link[key]) link[key] = item.link[key];
    out.link = link;
  }
  return out;
}

// ------------------------------------------------------------------ le volte

/** I giorni di una voce fra from e to (compresi), senza quelli tolti: le stesse regole di Agenda.Dates. */
export function dates(item, from, to) {
  const out = [];
  const start = item.date;
  const last = item.until && item.until < to ? item.until : to;
  if (last < from || start > last) return out;
  const every = Math.min(99, Math.max(1, item.every || 1));
  const skip = new Set(item.skip);
  const guard = MOST_DATES * 4;

  if (item.repeat === 'day' || item.repeat === 'week') {
    const step = every * (item.repeat === 'week' ? 7 : 1);
    const startN = dayNumber(start);
    let k = start >= from ? 0 : Math.ceil((dayNumber(from) - startN) / step);
    for (let i = 0; i < guard; i++, k++) {
      const day = fromNumber(startN + k * step);
      if (day > last) break;
      if (!skip.has(day)) out.push(day);
    }
    return out;
  }

  if (item.repeat === 'month' || item.repeat === 'year') {
    const months = every * (item.repeat === 'year' ? 12 : 1);
    const [fy, fm] = partsOf(from);
    const [sy, sm] = partsOf(start);
    const gap = (fy - sy) * 12 + fm - sm;
    let k = Math.max(0, Math.trunc(gap / months) - 1);
    for (let i = 0; i < guard; i++, k++) {
      const day = addMonths(start, k * months);
      if (day > last) break;
      if (day >= from && !skip.has(day)) out.push(day);
    }
    return out;
  }

  if (start >= from && start <= last) out.push(start);
  return out;
}

export const certKey = (host, day) => `${host.toLowerCase()}@${day}`;

/** Le scadenze dei certificati come voci (tutto il giorno, coi promemoria dei certificati, legate all'host). */
export function certItems(doc) {
  if (doc.certs.off) return [];
  const ignored = new Set(doc.certs.ignored.map((host) => host.toLowerCase()));
  const done = new Set(doc.certs.done.map((key) => key.toLowerCase()));
  return doc.certs.known.filter((seen) => !ignored.has(seen.host.toLowerCase())).map((seen) => ({
    id: CERT_PREFIX + seen.host.toLowerCase(),
    kind: 'deadline',
    title: `Scade il certificato di ${seen.host}`,
    date: seen.date,
    time: '',
    notes: [seen.subject, seen.issuer ? `Emesso da ${seen.issuer}` : ''].filter(Boolean).join('\n'),
    remind: [...doc.certs.remind],
    repeat: '',
    every: 1,
    until: '',
    done: done.has(certKey(seen.host, seen.date)) ? [seen.date] : [],
    skip: [],
    link: { kind: 'host', host: seen.host, map: '', node: '', name: seen.name },
    cert: seen,
  }));
}

export const allItems = (doc) => doc.items.concat(certItems(doc));

/** Tutte le volte fra due giorni: per giorno, poi quelle di tutto il giorno, poi per ora e titolo. */
export function occurrences(doc, from, to, items = allItems(doc)) {
  const out = [];
  for (const item of items) {
    for (const date of dates(item, from, to)) {
      out.push({ item, date, key: `${item.id}@${date}`, done: item.done.includes(date) });
    }
  }
  return out.sort((a, b) => (a.date < b.date ? -1 : a.date > b.date ? 1 : 0)
    || (a.item.time ? 1 : 0) - (b.item.time ? 1 : 0)
    || a.item.time.localeCompare(b.item.time)
    || a.item.title.localeCompare(b.item.title, 'it', { sensitivity: 'base' }));
}

/** La prossima volta di una voce da un giorno in poi (o l'ultima, se e' tutto passato). */
export function nextDate(item, from) {
  const ahead = dates(item, from, addDays(from, 3660));
  if (ahead.length) return ahead[0];
  const before = dates(item, item.date, addDays(from, -1));
  return before.length ? before[before.length - 1] : item.date;
}

// ------------------------------------------------------------------ cambiamenti

/** Lo stesso di Agenda.Apply: torna true se il calendario e' cambiato. */
export function applyOp(doc, op) {
  switch (op?.op) {
    case 'put': {
      if (str(op.item?.id).startsWith(CERT_PREFIX)) return false;
      const item = normalizeItem(op.item);
      if (!item) return false;
      const at = doc.items.findIndex((other) => other.id === item.id);
      if (at >= 0) doc.items[at] = item;
      else if (doc.items.length < MOST_ITEMS) doc.items.push(item);
      else return false;
      return true;
    }
    case 'remove': {
      const before = doc.items.length;
      doc.items = doc.items.filter((other) => other.id !== op.id);
      return doc.items.length !== before;
    }
    case 'skip': {
      const item = doc.items.find((other) => other.id === op.id);
      const day = parseDay(op.date);
      if (!item?.repeat || !day || item.skip.includes(day)) return false;
      item.done = item.done.filter((d) => d !== day);
      item.skip = [...item.skip, day].sort();
      return true;
    }
    case 'done': {
      const day = parseDay(op.date);
      return day ? setDone(doc, str(op.id), day, op.on === true) : false;
    }
    case 'ignore': {
      const host = clean(op.host, 253);
      if (!host) return false;
      const has = doc.certs.ignored.some((h) => h.toLowerCase() === host.toLowerCase());
      if (op.on === true) {
        if (has) return false;
        doc.certs.ignored.push(host);
        return true;
      }
      if (!has) return false;
      doc.certs.ignored = doc.certs.ignored.filter((h) => h.toLowerCase() !== host.toLowerCase());
      return true;
    }
    case 'certs': {
      let changed = false;
      if (typeof op.off === 'boolean' && op.off !== doc.certs.off) {
        doc.certs.off = op.off;
        changed = true;
      }
      if (Array.isArray(op.remind)) {
        const minutes = reminders(op.remind);
        if (minutes.join() !== doc.certs.remind.join()) {
          doc.certs.remind = minutes;
          changed = true;
        }
      }
      return changed;
    }
    default:
      return false;
  }
}

function setDone(doc, id, day, on) {
  if (id.startsWith(CERT_PREFIX)) {
    const host = id.slice(CERT_PREFIX.length);
    const seen = doc.certs.known.find((c) => c.host.toLowerCase() === host.toLowerCase());
    if (!seen) return false;
    const key = certKey(seen.host, day);
    const has = doc.certs.done.some((k) => k.toLowerCase() === key);
    if (on === has) return false;
    doc.certs.done = on ? [...doc.certs.done, key] : doc.certs.done.filter((k) => k.toLowerCase() !== key);
    return true;
  }
  const item = doc.items.find((other) => other.id === id);
  if (!item || item.done.includes(day) === on) return false;
  item.done = on ? [...item.done, day].sort() : item.done.filter((d) => d !== day);
  return true;
}

// ------------------------------------------------------------------ parole

export const MONTHS = ['gennaio', 'febbraio', 'marzo', 'aprile', 'maggio', 'giugno', 'luglio', 'agosto', 'settembre', 'ottobre', 'novembre', 'dicembre'];
export const WEEKDAYS = ['lunedì', 'martedì', 'mercoledì', 'giovedì', 'venerdì', 'sabato', 'domenica'];
export const WEEKDAYS_SHORT = ['lun', 'mar', 'mer', 'gio', 'ven', 'sab', 'dom'];

/** "mercoledì 14 aprile 2027" */
export function longDay(day) {
  const [y, m, d] = partsOf(day);
  return `${WEEKDAYS[weekday(day)]} ${d} ${MONTHS[m - 1]} ${y}`;
}

/** "14 apr", con l'anno se non e' quello di riferimento. */
export function shortDay(day, thisYear) {
  const [y, m, d] = partsOf(day);
  return `${d} ${MONTHS[m - 1].slice(0, 3)}${y !== thisYear ? ` ${y}` : ''}`;
}

/** "oggi", "domani", "ieri", "fra 5 giorni", "3 giorni fa". */
export function relative(day, today) {
  const n = dayNumber(day) - dayNumber(today);
  if (n === 0) return 'oggi';
  if (n === 1) return 'domani';
  if (n === -1) return 'ieri';
  return n > 0 ? `fra ${n} giorni` : `${-n} giorni fa`;
}

/** "Al momento", "15 minuti prima", "1 giorno prima alle 9"... */
export function reminderLabel(minutes, allDay) {
  if (allDay) {
    if (minutes === 0) return 'Il giorno stesso, alle 9';
    if (minutes % 10080 === 0) return `${minutes / 10080 === 1 ? '1 settimana' : `${minutes / 10080} settimane`} prima`;
    if (minutes % 1440 === 0) return `${minutes / 1440 === 1 ? 'Il giorno prima' : `${minutes / 1440} giorni prima`}, alle 9`;
  }
  if (minutes === 0) return 'Al momento';
  if (minutes % 10080 === 0) return `${minutes / 10080 === 1 ? '1 settimana' : `${minutes / 10080} settimane`} prima`;
  if (minutes % 1440 === 0) return `${minutes / 1440 === 1 ? '1 giorno' : `${minutes / 1440} giorni`} prima`;
  if (minutes % 60 === 0) return `${minutes / 60 === 1 ? '1 ora' : `${minutes / 60} ore`} prima`;
  return minutes === 1 ? '1 minuto prima' : `${minutes} minuti prima`;
}

/** "Ogni 2 settimane, fino al 30 giu 2027" */
export function repeatLabel(item, thisYear) {
  if (!item.repeat) return '';
  const unit = { day: ['giorno', 'giorni'], week: ['settimana', 'settimane'], month: ['mese', 'mesi'], year: ['anno', 'anni'] }[item.repeat];
  const every = item.every > 1 ? `Ogni ${item.every} ${unit[1]}` : `Ogni ${unit[0]}`;
  return item.until ? `${every}, fino al ${shortDay(item.until, thisYear)}` : every;
}
