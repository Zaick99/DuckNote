// Il pannello a destra: il giorno scelto con le sue voci, quello che e' rimasto indietro e quello
// che arriva; oppure i risultati della ricerca, la scheda di un certificato, l'editor.

import { h, icon } from '../common/dom.js';
import { ui as uiIcons } from '../common/icons.js';
import {
  addDays, partsOf, occurrences, allItems, nextDate, longDay, shortDay, relative, repeatLabel,
  reminderLabel, MONTHS, WEEKDAYS, weekday, dayNumber, KIND,
} from './model.js';
import { kindIcon } from './editor.js';
import { toneOf } from './month.js';

const UPCOMING_DAYS = 14;
const LATE_DAYS = 30;

/** Una riga di voce: la spunta (da fare e scadenze), l'ora, il titolo e cosa c'e' attorno. */
export function itemRow(occurrence, state, actions, { showDate = false } = {}) {
  const { item } = occurrence;
  const checkable = item.kind === 'todo' || item.kind === 'deadline';
  const thisYear = partsOf(state.today)[0];
  const host = item.link ? state.hostState(item.link) : null;
  const bits = [
    showDate ? h('span', { class: 'dc-row-date' }, `${shortDay(occurrence.date, thisYear)} · ${relative(occurrence.date, state.today)}`) : null,
    h('span', {}, item.time || 'tutto il giorno'),
    item.repeat ? h('span', { class: 'dc-row-bit', title: repeatLabel(item, thisYear) }, icon(uiIcons.repeat, 11)) : null,
    item.remind.length ? h('span', { class: 'dc-row-bit', title: item.remind.map((m) => reminderLabel(m, !item.time)).join(', ') }, icon(uiIcons.bell, 11)) : null,
    item.link ? h('span', { class: 'dc-row-bit link' }, host ? h('span', { class: `dm-dot st-${host}` }) : icon(item.link.kind === 'node' ? uiIcons.map : uiIcons.network, 11), item.link.name || item.link.host) : null,
  ];
  const tone = toneOf(item);
  const row = h('div', {
    class: `dc-row${occurrence.done ? ' done' : ''}${occurrence.date < state.today && checkable && !occurrence.done ? ' late' : ''}`,
    role: 'button',
    tabindex: '0',
    dataset: { key: occurrence.key },
    style: { '--tone': `var(--c-${tone})`, '--tone-soft': `var(--t-${tone}-soft)` },
  },
  checkable
    ? h('button', {
      class: `dc-check${occurrence.done ? ' on' : ''}`,
      type: 'button',
      title: occurrence.done ? 'Da fare di nuovo' : item.cert ? 'Rinnovato' : 'Fatto',
      'aria-label': occurrence.done ? 'Da fare di nuovo' : 'Fatto',
      disabled: state.readonly,
      onclick: (event) => {
        event.stopPropagation();
        actions.done(occurrence, !occurrence.done);
      },
    }, occurrence.done ? icon(uiIcons.check, 11) : null)
    : h('span', { class: 'dc-row-icon' }, icon(item.cert ? uiIcons.lock : kindIcon(item.kind), 13)),
  h('div', { class: 'dc-row-text' },
    h('div', { class: 'dc-row-title' }, item.title || 'Senza titolo'),
    h('div', { class: 'dc-row-sub' }, bits)));
  row.addEventListener('click', () => actions.open(occurrence));
  row.addEventListener('keydown', (event) => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      actions.open(occurrence);
    }
  });
  return row;
}

/** Il giorno scelto, poi in ritardo e in arrivo. */
export function dayView(state, actions) {
  const day = state.selected;
  const [y, m, d] = partsOf(day);
  const list = occurrences(state.doc, day, day);
  const isToday = day === state.today;

  const head = h('div', { class: 'dc-day-head' },
    h('div', { class: `dc-day-num${isToday ? ' today' : ''}` }, String(d)),
    h('div', { class: 'dc-day-titles' },
      h('div', { class: 'dc-day-name' }, WEEKDAYS[weekday(day)]),
      h('div', { class: 'dc-day-sub' }, `${MONTHS[m - 1]} ${y} · ${relative(day, state.today)}`)),
    state.readonly ? null : h('button', { class: 'dm-button small primary', type: 'button', title: 'Una voce nuova in questo giorno (N)', onclick: () => actions.create(day) },
      icon(uiIcons.add, 12), h('span', {}, 'Aggiungi')));

  const body = list.length
    ? h('div', { class: 'dc-list' }, list.map((occurrence) => itemRow(occurrence, state, actions)))
    : h('div', { class: 'dc-empty' }, state.readonly ? 'Niente in questo giorno.' : 'Niente in questo giorno. Doppio clic su un giorno, o Aggiungi, per scrivere un appuntamento, una cosa da fare o una scadenza.');

  // in ritardo: da fare e scadenze non fatte fino a 30 giorni fa
  const late = occurrences(state.doc, addDays(state.today, -LATE_DAYS), addDays(state.today, -1))
    .filter((o) => !o.done && (o.item.kind === 'todo' || o.item.kind === 'deadline'));
  const ahead = occurrences(state.doc, addDays(state.today, 1), addDays(state.today, UPCOMING_DAYS)).filter((o) => !o.done);

  const sections = [];
  if (late.length) {
    sections.push(h('div', { class: 'dc-section-title late' }, h('span', {}, 'In ritardo'), h('span', { class: 'dm-count' }, String(late.length))));
    sections.push(h('div', { class: 'dc-list' }, late.map((o) => itemRow(o, state, actions, { showDate: true }))));
  }
  sections.push(h('div', { class: 'dc-section-title' }, h('span', {}, `Prossimi ${UPCOMING_DAYS} giorni`), h('span', { class: 'dm-count' }, ahead.length ? String(ahead.length) : '')));
  sections.push(ahead.length
    ? h('div', { class: 'dc-list' }, ahead.slice(0, 60).map((o) => itemRow(o, state, actions, { showDate: true })))
    : h('div', { class: 'dc-empty small' }, 'Niente in arrivo.'));

  return h('div', { class: 'dc-view' }, head, body, h('div', { class: 'dc-sections' }, sections));
}

/** I risultati della ricerca: ogni voce con la sua prossima volta. */
export function searchView(state, actions) {
  const q = state.query.trim().toLowerCase();
  const items = allItems(state.doc).filter((item) => [item.title, item.notes, item.link?.host, item.link?.name]
    .some((value) => String(value ?? '').toLowerCase().includes(q)));
  const found = items.map((item) => {
    const date = nextDate(item, state.today);
    return { item, date, key: `${item.id}@${date}`, done: item.done.includes(date) };
  }).sort((a, b) => Math.abs(dayNumber(a.date) - dayNumber(state.today)) - Math.abs(dayNumber(b.date) - dayNumber(state.today)));

  return h('div', { class: 'dc-view' },
    h('div', { class: 'dc-section-title' }, h('span', {}, `Ricerca «${state.query.trim()}»`), h('span', { class: 'dm-count' }, String(found.length))),
    found.length
      ? h('div', { class: 'dc-list' }, found.slice(0, 200).map((o) => itemRow(o, state, actions, { showDate: true })))
      : h('div', { class: 'dc-empty' }, 'Nessuna voce corrisponde.'));
}

/**
 * La scheda di una voce, da leggere: quando, ogni quanto, i promemoria, a cosa e' legata, gli
 * appunti. Si modifica solo dalla matita.
 */
export function detailView(state, occurrence, actions) {
  const { item } = occurrence;
  const tone = toneOf(item);
  const checkable = item.kind === 'todo' || item.kind === 'deadline';
  const thisYear = partsOf(state.today)[0];
  const host = item.link ? state.hostState(item.link) : null;
  const late = checkable && !occurrence.done && occurrence.date < state.today;

  const when = `${longDay(occurrence.date)}${item.time ? `, alle ${item.time}` : ', tutto il giorno'}`;
  const facts = [
    ['Quando', h('span', {}, when, h('span', { class: `dc-detail-when${late ? ' late' : ''}` }, ` · ${relative(occurrence.date, state.today)}`))],
    item.repeat ? ['Ripete', repeatLabel(item, thisYear)] : null,
    ['Promemoria', item.remind.length ? item.remind.map((m) => reminderLabel(m, !item.time)).join(', ') : 'nessuno'],
    item.link ? ['Collegato a', h('button', {
      class: 'dc-detail-link',
      type: 'button',
      title: item.link.kind === 'node' ? 'Mostralo sulla mappa' : 'Mostralo nella pagina Rete',
      onclick: () => actions.reveal(item.link),
    },
    host ? h('span', { class: `dm-dot st-${host}` }) : icon(item.link.kind === 'node' ? uiIcons.map : uiIcons.network, 12),
    h('span', { class: 'dc-link-name' }, item.link.name || item.link.host),
    item.link.name && item.link.host ? h('span', { class: 'dc-link-host' }, item.link.host) : null)] : null,
  ].filter(Boolean);

  return h('div', { class: 'dc-view dc-detail' },
    h('div', { class: 'dc-detail-head' },
      h('div', { class: 'dc-detail-tile', style: { '--tone': `var(--c-${tone})`, '--tone-soft': `var(--t-${tone}-soft)` } }, icon(kindIcon(item.kind), 20)),
      h('div', { class: 'dc-detail-titles' },
        h('div', { class: `dc-detail-title${occurrence.done ? ' done' : ''}` }, item.title || 'Senza titolo'),
        h('div', { class: 'dc-detail-sub' }, KIND[item.kind]?.label ?? 'Appuntamento', occurrence.done ? ' · fatto' : '')),
      state.readonly ? null : h('button', {
        class: 'dm-tool dm-press dc-edit',
        type: 'button',
        title: 'Modifica (E)',
        'aria-label': 'Modifica',
        onclick: () => actions.edit(occurrence),
      }, icon(uiIcons.pen, 15))),
    h('dl', { class: 'dc-facts' }, facts.map(([label, value]) => [h('dt', {}, label), h('dd', {}, value)])),
    item.notes ? h('div', { class: 'dm-row' }, h('span', { class: 'dm-label' }, 'Appunti'), h('div', { class: 'dc-notes' }, item.notes)) : null,
    h('div', { class: 'dc-editor-buttons' },
      checkable && !state.readonly ? h('button', {
        class: `dm-button small${occurrence.done ? '' : ' primary'}`,
        type: 'button',
        onclick: () => actions.done(occurrence, !occurrence.done),
      }, icon(uiIcons.check, 12), h('span', {}, occurrence.done ? 'Da fare di nuovo' : 'Fatto')) : null,
      h('span', { class: 'dm-spacer' }),
      h('button', { class: 'dm-button small', type: 'button', onclick: () => actions.back() }, 'Chiudi')));
}

/** La scheda di un certificato: quando scade, chi e', e cosa farne. */
export function certView(state, occurrence, actions) {
  const { item } = occurrence;
  const seen = item.cert;
  const days = dayNumber(item.date) - dayNumber(state.today);
  const when = days < 0 ? `scaduto da ${-days} ${-days === 1 ? 'giorno' : 'giorni'}` : days === 0 ? 'scade oggi' : `fra ${days} ${days === 1 ? 'giorno' : 'giorni'}`;
  const tone = days < 0 ? 'Red' : days <= 30 ? 'Orange' : 'Green';
  const host = state.hostState(item.link);
  const thisYear = partsOf(state.today)[0];

  const facts = [
    ['Scadenza', `${longDay(item.date)}`],
    ['Soggetto', seen.subject],
    ['Emesso da', seen.issuer],
    ['Avvisi', state.doc.certs.remind.length ? state.doc.certs.remind.map((m) => reminderLabel(m, true)).join(', ') : 'nessuno'],
  ].filter(([, value]) => value);

  return h('div', { class: 'dc-view' },
    h('div', { class: 'dc-cert-head' },
      h('div', { class: 'dc-cert-tile', style: { '--tone': `var(--c-${tone})`, '--tone-soft': `var(--t-${tone}-soft)` } }, icon(uiIcons.lock, 20)),
      h('div', { class: 'dc-cert-titles' },
        h('div', { class: 'dc-cert-title' }, `Certificato di ${seen.host}`),
        h('div', { class: 'dc-cert-sub' }, host ? h('span', { class: `dm-dot st-${host}` }) : null, h('span', {}, `${shortDay(item.date, thisYear)} · ${when}`)))),
    h('dl', { class: 'dc-facts' }, facts.map(([label, value]) => [h('dt', {}, label), h('dd', {}, value)])),
    h('div', { class: 'dm-insp-actions' },
      h('button', {
        class: `dm-button small${occurrence.done ? '' : ' primary'}`,
        type: 'button',
        disabled: state.readonly,
        onclick: () => actions.done(occurrence, !occurrence.done),
      }, icon(uiIcons.check, 12), h('span', {}, occurrence.done ? 'Non ancora rinnovato' : 'Rinnovato')),
      h('button', { class: 'dm-button small', type: 'button', onclick: () => actions.reveal(item.link) }, icon(uiIcons.network, 12), h('span', {}, 'Apri in Rete')),
      h('button', { class: 'dm-button small', type: 'button', onclick: () => actions.remind(item.link, seen) }, icon(uiIcons.add, 12), h('span', {}, 'Promemoria mio')),
      h('button', { class: 'dm-button small danger', type: 'button', disabled: state.readonly, onclick: () => actions.ignore(seen.host) }, h('span', {}, 'Ignora questo host'))),
    h('div', { class: 'dm-hint' }, 'La data arriva dall\'ultima analisi di DuckNote: quando il certificato viene rinnovato, la prossima analisi la sposta da sola.'),
    h('div', { class: 'dc-editor-buttons' }, h('span', { class: 'dm-spacer' }), h('button', { class: 'dm-button', type: 'button', onclick: () => actions.back() }, 'Chiudi')));
}

/** Le impostazioni dei certificati: accese o spente, quando avvisare, gli host ignorati. */
export function certSettings(state, actions) {
  const certs = state.doc.certs;
  const offered = [43200, 20160, 10080, 1440, 0];
  const on = h('input', { type: 'checkbox', class: 'dc-switch', checked: !certs.off, disabled: state.readonly });
  on.addEventListener('change', () => actions.certs({ off: !on.checked }));
  const chips = h('div', { class: 'dm-chips' }, offered.map((minutes) => h('button', {
    class: `dm-choice${certs.remind.includes(minutes) ? ' on' : ''}`,
    type: 'button',
    disabled: state.readonly || certs.off,
    onclick: () => {
      const next = certs.remind.includes(minutes) ? certs.remind.filter((m) => m !== minutes) : [...certs.remind, minutes];
      actions.certs({ remind: next });
    },
  }, minutes === 0 ? 'Il giorno stesso' : minutes % 10080 === 0 ? `${minutes / 10080} ${minutes === 10080 ? 'settimana' : 'settimane'} prima` : minutes % 1440 === 0 ? `${minutes / 1440} ${minutes === 1440 ? 'giorno' : 'giorni'} prima` : `${minutes} minuti prima`)));
  const ignored = certs.ignored.length
    ? h('div', { class: 'dc-ignored' }, certs.ignored.map((host) => h('div', { class: 'dc-ignored-row' },
      h('span', { class: 'dc-ignored-host' }, host),
      h('button', { class: 'dm-link-button', type: 'button', disabled: state.readonly, onclick: () => actions.unignore(host) }, 'Riprendi'))))
    : h('div', { class: 'dm-hint' }, 'Nessuno.');
  return h('div', { class: 'dm-pop dc-cert-settings' },
    h('label', { class: 'dc-switch-label strong' }, on, h('span', {}, 'Scadenze dei certificati dalle analisi')),
    h('div', { class: 'dm-hint' }, `${certs.known.length} ${certs.known.length === 1 ? 'certificato visto' : 'certificati visti'}. Le date arrivano dagli host con HTTPS analizzati da DuckNote.`),
    h('div', { class: 'dm-row' }, h('span', { class: 'dm-label' }, 'Avvisa'), chips),
    h('div', { class: 'dm-row' }, h('span', { class: 'dm-label' }, `Host ignorati${certs.ignored.length ? ` (${certs.ignored.length})` : ''}`), ignored));
}

