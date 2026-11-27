// La scheda di una voce: tipo, titolo, giorno e ora (o tutto il giorno), ripetizione, promemoria,
// legame con un host o un apparato della mappa, appunti. Si lavora su una copia: la voce cambia
// solo con Salva.

import { h, icon, fill } from '../common/dom.js';
import { ui as uiIcons } from '../common/icons.js';
import { shake } from '../common/motion.js';
import { KINDS, reminderLabel, parseDay, parseTime, longDay } from './model.js';

const KIND_ICONS = { event: uiIcons.clock, todo: uiIcons.todo, deadline: uiIcons.flag, note: uiIcons.note };
export const kindIcon = (kind) => KIND_ICONS[kind] ?? uiIcons.clock;

/** I promemoria che si offrono: con l'ora, e per le voci di tutto il giorno (alle 9). */
const TIMED = [0, 5, 15, 60, 1440, 10080];
const ALL_DAY = [0, 1440, 10080, 43200];

/** I promemoria di serie per una voce nuova. */
export const DEFAULT_REMIND = { event: [15], todo: [0], deadline: [10080, 1440], note: [] };

const REPEATS = [
  { id: '', label: 'Mai' },
  { id: 'day:1', label: 'Ogni giorno' },
  { id: 'week:1', label: 'Ogni settimana' },
  { id: 'week:2', label: 'Ogni 2 settimane' },
  { id: 'month:1', label: 'Ogni mese' },
  { id: 'month:3', label: 'Ogni 3 mesi' },
  { id: 'month:6', label: 'Ogni 6 mesi' },
  { id: 'year:1', label: 'Ogni anno' },
];
const UNITS = { day: 'giorni', week: 'settimane', month: 'mesi', year: 'anni' };

/**
 * La scheda. draft: la copia della voce; options: { isNew, readonly, pickLink(anchor, choose),
 * save(item), cancel(), remove(anchor), reveal(link) }.
 */
export function editor(draft, options) {
  const readonly = !!options.readonly;
  let timeTouched = !options.isNew;
  let remindTouched = !options.isNew;

  const problem = h('div', { class: 'dc-problem', hidden: true });
  const say = (text, field) => {
    problem.textContent = text;
    problem.hidden = false;
    if (field) {
      field.focus();
      shake(field);
    }
  };

  // ------------------------------------------------ tipo e titolo
  const kindButtons = KINDS.map((kind) => h('button', {
    class: 'dc-kind',
    type: 'button',
    role: 'radio',
    disabled: readonly,
    style: { '--tone': `var(--c-${kind.tone})`, '--tone-soft': `var(--t-${kind.tone}-soft)` },
    onclick: () => chooseKind(kind.id),
  }, h('span', { class: 'dc-kind-icon' }, icon(kindIcon(kind.id), 14)), h('span', {}, kind.label)));
  const kinds = h('div', { class: 'dc-kinds', role: 'radiogroup', 'aria-label': 'Tipo' }, kindButtons);
  const markKind = () => kindButtons.forEach((button, i) => {
    button.classList.toggle('on', KINDS[i].id === draft.kind);
    button.setAttribute('aria-checked', String(KINDS[i].id === draft.kind));
  });
  const chooseKind = (id) => {
    if (draft.kind === id) return;
    draft.kind = id;
    markKind();
    if (options.isNew) {
      // una voce nuova prende i promemoria e l'ora del suo tipo, finche' nessuno li ha toccati
      if (!remindTouched) draft.remind = [...DEFAULT_REMIND[id]];
      if (!timeTouched) {
        draft.time = id === 'event' ? draft.time || '09:00' : '';
        syncTime();
      }
      renderReminders();
    }
  };
  markKind();

  const title = h('input', { class: 'dm-field big', value: draft.title, placeholder: 'Titolo', maxlength: 200, spellcheck: 'true', 'aria-label': 'Titolo' });
  title.addEventListener('input', () => {
    draft.title = title.value;
    problem.hidden = true;
  });

  // ------------------------------------------------ quando
  const date = h('input', { class: 'dm-field', type: 'date', value: draft.date, 'aria-label': 'Giorno' });
  date.addEventListener('change', () => {
    const day = parseDay(date.value);
    if (day) draft.date = day;
    dateHint.textContent = day ? longDay(day) : '';
  });
  const dateHint = h('div', { class: 'dm-hint' }, longDay(draft.date));
  const time = h('input', { class: 'dm-field dc-time', type: 'time', value: draft.time || '09:00', 'aria-label': 'Ora' });
  time.addEventListener('change', () => {
    draft.time = parseTime(time.value) || draft.time;
    timeTouched = true;
  });
  const allDay = h('input', { type: 'checkbox', class: 'dc-switch', checked: !draft.time, 'aria-label': 'Tutto il giorno' });
  allDay.addEventListener('change', () => {
    timeTouched = true;
    draft.time = allDay.checked ? '' : parseTime(time.value) || '09:00';
    syncTime();
    renderReminders();
  });
  const syncTime = () => {
    allDay.checked = !draft.time;
    time.disabled = !draft.time;
    if (draft.time) time.value = draft.time;
  };
  syncTime();

  // ------------------------------------------------ ripetizione
  const repeatId = draft.repeat ? `${draft.repeat}:${draft.every}` : '';
  const choices = REPEATS.some((r) => r.id === repeatId) ? REPEATS : [...REPEATS, { id: repeatId, label: `Ogni ${draft.every} ${UNITS[draft.repeat]}` }];
  const repeat = h('select', { class: 'dm-field', 'aria-label': 'Ripeti' }, choices.map((r) => h('option', { value: r.id, selected: r.id === repeatId }, r.label)));
  const until = h('input', { class: 'dm-field', type: 'date', value: draft.until, 'aria-label': 'Fino al' });
  const untilRow = h('div', { class: 'dm-row', hidden: !draft.repeat }, h('label', { class: 'dm-label' }, 'Fino al (facoltativo)'), until);
  repeat.addEventListener('change', () => {
    const [unit, every] = repeat.value.split(':');
    draft.repeat = unit || '';
    draft.every = Number(every) || 1;
    untilRow.hidden = !draft.repeat;
  });
  until.addEventListener('change', () => {
    draft.until = parseDay(until.value) ?? '';
  });

  // ------------------------------------------------ promemoria
  const reminders = h('div', { class: 'dm-chips' });
  const renderReminders = () => {
    const offered = [...new Set([...(draft.time ? TIMED : ALL_DAY), ...draft.remind])].sort((a, b) => a - b);
    fill(reminders, offered.map((minutes) => h('button', {
      class: `dm-choice${draft.remind.includes(minutes) ? ' on' : ''}`,
      type: 'button',
      disabled: readonly,
      onclick: () => {
        remindTouched = true;
        draft.remind = draft.remind.includes(minutes) ? draft.remind.filter((m) => m !== minutes) : [...draft.remind, minutes].sort((a, b) => b - a);
        renderReminders();
      },
    }, reminderLabel(minutes, !draft.time))));
  };
  renderReminders();

  // ------------------------------------------------ legame
  const linkBox = h('div', { class: 'dc-link-box' });
  const renderLink = () => {
    const link = draft.link;
    if (!link) {
      fill(linkBox, h('button', {
        class: 'dm-button small',
        type: 'button',
        disabled: readonly,
        onclick: (event) => options.pickLink(event.currentTarget, (chosen) => {
          draft.link = chosen;
          renderLink();
        }),
      }, icon(uiIcons.link, 13), h('span', {}, 'Collega a un host o a un apparato')));
      return;
    }
    fill(linkBox, h('div', { class: 'dc-link' },
      h('span', { class: 'dc-link-icon' }, icon(link.kind === 'node' ? uiIcons.map : uiIcons.network, 13)),
      h('button', { class: 'dc-link-text', type: 'button', title: 'Mostralo', onclick: () => options.reveal(link) },
        h('span', { class: 'dc-link-name' }, link.name || link.host),
        link.name && link.host ? h('span', { class: 'dc-link-host' }, link.host) : null),
      readonly ? null : h('button', {
        class: 'dm-tool small',
        type: 'button',
        title: 'Togli il legame',
        'aria-label': 'Togli il legame',
        onclick: () => {
          draft.link = null;
          renderLink();
        },
      }, icon(uiIcons.close, 11))));
  };
  renderLink();

  // ------------------------------------------------ appunti
  const notes = h('textarea', { class: 'dm-field notes', rows: 4, placeholder: 'Appunti, cose da portare, numeri utili...', 'aria-label': 'Appunti', spellcheck: 'true' });
  notes.value = draft.notes;
  notes.addEventListener('input', () => {
    draft.notes = notes.value;
  });

  // ------------------------------------------------ pulsanti
  const save = () => {
    draft.title = title.value.trim();
    if (!draft.title) {
      say('Scrivi un titolo.', title);
      return;
    }
    if (!parseDay(date.value)) {
      say('Scegli un giorno.', date);
      return;
    }
    draft.date = parseDay(date.value);
    if (draft.repeat && draft.until && draft.until < draft.date) {
      say('La ripetizione deve finire dopo il primo giorno.', until);
      return;
    }
    options.save(draft);
  };

  const buttons = h('div', { class: 'dc-editor-buttons' },
    !options.isNew && !readonly ? h('button', { class: 'dm-button danger', type: 'button', onclick: (event) => options.remove(event.currentTarget) }, icon(uiIcons.trash, 13), h('span', {}, 'Elimina')) : null,
    h('span', { class: 'dm-spacer' }),
    h('button', { class: 'dm-button', type: 'button', onclick: () => options.cancel() }, readonly ? 'Chiudi' : 'Annulla'),
    readonly ? null : h('button', { class: 'dm-button primary', type: 'button', onclick: save }, options.isNew ? 'Aggiungi' : 'Salva'));

  for (const field of [title, date, time, allDay, repeat, until, notes]) field.disabled = field.disabled || readonly;

  const form = h('form', { class: 'dc-editor', onsubmit: (event) => { event.preventDefault(); save(); } },
    h('div', { class: 'dc-editor-head' }, h('span', { class: 'dc-editor-title' }, options.isNew ? 'Nuova voce' : 'Modifica'), h('span', { class: 'dm-spacer' })),
    kinds,
    title,
    h('div', { class: 'dm-row' }, h('label', { class: 'dm-label' }, 'Giorno'), date, dateHint),
    h('div', { class: 'dc-time-row' },
      h('label', { class: 'dc-switch-label' }, allDay, h('span', {}, 'Tutto il giorno')),
      time),
    h('div', { class: 'dm-row' }, h('label', { class: 'dm-label' }, 'Ripeti'), repeat),
    untilRow,
    draft.repeat && !options.isNew ? h('div', { class: 'dm-hint' }, 'Le modifiche valgono per tutte le volte.') : null,
    h('div', { class: 'dm-row' }, h('label', { class: 'dm-label' }, h('span', { class: 'dc-label-icon' }, icon(uiIcons.bell, 11)), 'Promemoria'), reminders,
      h('div', { class: 'dm-hint' }, 'Con DuckNote aperto: un avviso nella finestra, e il Dock o la barra delle applicazioni se e\' dietro.')),
    h('div', { class: 'dm-row' }, h('label', { class: 'dm-label' }, 'Collegato a'), linkBox),
    h('div', { class: 'dm-row' }, h('label', { class: 'dm-label' }, 'Appunti'), notes),
    problem,
    buttons);

  form.addEventListener('keydown', (event) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      options.cancel();
    } else if (event.key === 'Enter' && (event.metaKey || event.ctrlKey)) {
      event.preventDefault();
      save();
    } else if (event.key === 'Enter' && event.target === title) {
      event.preventDefault();
      save();
    }
  });

  // una voce nuova: il titolo, col cursore all'inizio se e' gia' scritto (da Rete o dalla mappa)
  const focus = () => {
    if (!options.isNew) return;
    title.focus();
    if (title.value) {
      title.setSelectionRange(0, 0);
      title.scrollLeft = 0;
    }
  };
  return { el: form, focus, title };
}

/**
 * La scelta del legame: gli host che DuckNote conosce e gli apparati delle mappe, con un campo
 * per cercare. choose(link) riceve { kind: 'host'|'node', host, map, node, name }.
 */
export function linkPicker(popups, anchor, { hosts, devices }, choose) {
  const field = h('input', { class: 'dm-field small', type: 'search', placeholder: 'Cerca un host o un apparato', spellcheck: 'false', 'aria-label': 'Cerca' });
  const list = h('div', { class: 'dc-pick-list', role: 'listbox' });
  const el = h('div', { class: 'dm-pop dc-pick' }, field, list);

  const render = () => {
    const q = field.value.trim().toLowerCase();
    const match = (...values) => !q || values.some((value) => String(value ?? '').toLowerCase().includes(q));
    const shownHosts = hosts.filter((host) => match(host.key, host.ip, host.name)).slice(0, 150);
    const shownDevices = devices.filter((device) => match(device.name, device.host, device.mapName)).slice(0, 150);
    const rows = [];
    if (shownDevices.length) {
      rows.push(h('div', { class: 'dm-menu-head' }, 'Apparati della mappa'));
      for (const device of shownDevices) {
        rows.push(h('button', {
          class: 'dc-pick-row',
          type: 'button',
          onclick: () => {
            popups.close();
            choose({ kind: 'node', host: device.host ?? '', map: device.map, node: device.node, name: device.name ?? '' });
          },
        }, icon(uiIcons.map, 13), h('span', { class: 'dc-pick-name' }, device.name || device.host || 'Apparato'),
        h('span', { class: 'dc-pick-sub' }, [device.host, device.mapName].filter(Boolean).join(' · '))));
      }
    }
    if (shownHosts.length) {
      rows.push(h('div', { class: 'dm-menu-head' }, 'Host'));
      for (const host of shownHosts) {
        const key = host.key || host.ip;
        rows.push(h('button', {
          class: 'dc-pick-row',
          type: 'button',
          onclick: () => {
            popups.close();
            choose({ kind: 'host', host: key, map: '', node: '', name: host.name && host.name !== key ? host.name : '' });
          },
        }, h('span', { class: `dm-dot st-${host.state ?? 'unknown'}` }), h('span', { class: 'dc-pick-name' }, host.name || key),
        h('span', { class: 'dc-pick-sub' }, host.name ? key : host.ip !== key ? host.ip : '')));
      }
    }
    if (!rows.length) {
      const typed = field.value.trim();
      rows.push(h('div', { class: 'dc-pick-empty' }, hosts.length || devices.length ? 'Niente che corrisponda.' : 'Nessun host ancora: scrivili nella nota o analizza la rete.'));
      if (typed) {
        rows.push(h('button', {
          class: 'dc-pick-row',
          type: 'button',
          onclick: () => {
            popups.close();
            choose({ kind: 'host', host: typed, map: '', node: '', name: '' });
          },
        }, icon(uiIcons.add, 13), h('span', { class: 'dc-pick-name' }, `Usa «${typed}»`)));
      }
    }
    fill(list, rows);
  };
  field.addEventListener('input', render);
  field.addEventListener('keydown', (event) => {
    if (event.key === 'Enter') {
      event.preventDefault();
      list.querySelector('.dc-pick-row')?.click();
    }
  });
  render();
  popups.open(el, anchor);
  field.focus();
}
