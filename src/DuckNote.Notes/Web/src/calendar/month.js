// La griglia del mese: sei settimane da lunedi' a domenica, in ogni giorno le voci che ci stanno
// (prima quelle di tutto il giorno, poi per ora) e "+N" per le altre. Un clic sceglie il giorno,
// un doppio clic ci aggiunge una voce; una voce che non si ripete si sposta trascinandola.

import { h, icon, fill, toggleClass } from '../common/dom.js';
import { ui as uiIcons } from '../common/icons.js';
import { KIND, WEEKDAYS_SHORT, addDays, partsOf, makeDay, weekday, occurrences } from './model.js';

const PILL = 19;
const HEAD = 27;

export const toneOf = (item) => (item.cert ? 'Orange' : KIND[item.kind]?.tone ?? 'Blue');

export class Month {
  /** actions: { pick(day), open(occurrence), create(day), move(item, day) } */
  constructor(state, actions) {
    this.state = state;
    this.actions = actions;
    this.grid = h('div', { class: 'dc-grid', role: 'grid', 'aria-label': 'Mese' });
    this.el = h('section', { class: 'dc-month' },
      h('div', { class: 'dc-weekdays', 'aria-hidden': 'true' }, WEEKDAYS_SHORT.map((name, i) => h('span', { class: i >= 5 ? 'weekend' : '' }, name))),
      this.grid);
    this.lines = 3;
    new ResizeObserver(() => {
      const lines = this.fitLines();
      if (lines !== this.lines) {
        this.lines = lines;
        this.render();
      }
    }).observe(this.grid);

    this.grid.addEventListener('click', (event) => {
      const cell = event.target.closest('.dc-cell');
      if (cell && !event.target.closest('.dc-pill')) actions.pick(cell.dataset.day);
    });
    this.grid.addEventListener('dblclick', (event) => {
      const cell = event.target.closest('.dc-cell');
      if (cell && !event.target.closest('.dc-pill') && !state.readonly) actions.create(cell.dataset.day);
    });
  }

  /**
   * Sposta la scelta su un giorno gia' in griglia senza rifarla (un doppio clic deve trovare la
   * stessa cella del primo clic); false se il giorno non c'e'.
   */
  select(day) {
    const target = this.grid.querySelector(`.dc-cell[data-day="${day}"]`);
    if (!target) return false;
    for (const cell of this.grid.querySelectorAll('.dc-cell.selected')) {
      cell.classList.remove('selected');
      cell.setAttribute('aria-selected', 'false');
    }
    target.classList.add('selected');
    target.setAttribute('aria-selected', 'true');
    return true;
  }

  /** Quante voci entrano in un giorno, con la misura di adesso. */
  fitLines() {
    const height = this.grid.clientHeight / 6;
    return Math.max(1, Math.floor((height - HEAD) / PILL));
  }

  /** Il primo giorno della griglia: il lunedi' della settimana del primo del mese. */
  firstDay() {
    const [y, m] = partsOf(`${this.state.month}-01`);
    const first = makeDay(y, m, 1);
    return addDays(first, -weekday(first));
  }

  render() {
    const { state } = this;
    const first = this.firstDay();
    const last = addDays(first, 41);
    const byDay = new Map();
    for (const occurrence of occurrences(state.doc, first, last)) {
      if (!byDay.has(occurrence.date)) byDay.set(occurrence.date, []);
      byDay.get(occurrence.date).push(occurrence);
    }

    const cells = [];
    for (let i = 0; i < 42; i++) {
      const day = addDays(first, i);
      const list = byDay.get(day) ?? [];
      const d = partsOf(day)[2];
      const room = list.length > this.lines ? this.lines - 1 : this.lines;
      const shown = list.slice(0, Math.max(0, room));
      const more = list.length - shown.length;
      const cell = h('div', {
        class: 'dc-cell',
        role: 'gridcell',
        dataset: { day },
        'aria-label': `${d}`,
        'aria-selected': String(day === state.selected),
      },
      h('div', { class: 'dc-cell-head' }, h('span', { class: 'dc-num' }, String(d))),
      h('div', { class: 'dc-items' },
        shown.map((occurrence) => this.pill(occurrence)),
        more > 0 ? h('button', { class: 'dc-more', type: 'button', onclick: () => this.actions.pick(day) }, `+${more}`) : null));
      toggleClass(cell, 'other', `${day.slice(0, 7)}` !== state.month);
      toggleClass(cell, 'today', day === state.today);
      toggleClass(cell, 'selected', day === state.selected);
      toggleClass(cell, 'weekend', i % 7 >= 5);
      toggleClass(cell, 'past', day < state.today);
      cells.push(cell);
    }
    fill(this.grid, cells);
  }

  pill(occurrence) {
    const { item } = occurrence;
    const allDay = !item.time;
    const pill = h('button', {
      class: `dc-pill${allDay ? ' allday' : ''}${occurrence.done ? ' done' : ''}${item.cert ? ' cert' : ''}`,
      type: 'button',
      style: { '--tone': `var(--c-${toneOf(item)})`, '--tone-soft': `var(--t-${toneOf(item)}-soft)` },
      title: `${item.time ? `${item.time} ` : ''}${item.title || 'Senza titolo'}`,
      dataset: { key: occurrence.key },
    },
    allDay ? (item.cert ? icon(uiIcons.lock, 10) : null) : h('span', { class: 'dc-pill-dot' }),
    item.time ? h('span', { class: 'dc-pill-time' }, item.time) : null,
    h('span', { class: 'dc-pill-title' }, item.title || 'Senza titolo'));
    pill.addEventListener('pointerdown', (event) => this.press(event, occurrence, pill));
    return pill;
  }

  /** Un clic apre la voce; trascinata (se non si ripete e non e' un certificato) cambia giorno. */
  press(event, occurrence, pill) {
    if (event.button !== 0) return;
    const { item } = occurrence;
    const movable = !this.state.readonly && !item.cert && !item.repeat;
    const x0 = event.clientX;
    const y0 = event.clientY;
    let ghost = null;
    let target = null;

    const move = (e) => {
      if (!movable) return;
      if (!ghost && Math.hypot(e.clientX - x0, e.clientY - y0) < 5) return;
      if (!ghost) {
        ghost = pill.cloneNode(true);
        ghost.classList.add('dc-ghost');
        ghost.style.width = `${pill.offsetWidth}px`;
        document.body.append(ghost);
        pill.classList.add('dragging');
      }
      ghost.style.transform = `translate(${e.clientX + 8}px, ${e.clientY - 9}px)`;
      const cell = document.elementFromPoint(e.clientX, e.clientY)?.closest?.('.dc-cell');
      if (target && target !== cell) target.classList.remove('drop');
      target = cell ?? null;
      target?.classList.add('drop');
    };
    const up = (e) => {
      cleanup();
      if (!ghost) {
        if (Math.hypot(e.clientX - x0, e.clientY - y0) < 5) this.actions.open(occurrence);
        return;
      }
      ghost.remove();
      pill.classList.remove('dragging');
      target?.classList.remove('drop');
      const day = target?.dataset.day;
      if (day && day !== item.date) this.actions.move(item, day);
    };
    const cancel = () => {
      cleanup();
      ghost?.remove();
      pill.classList.remove('dragging');
      target?.classList.remove('drop');
    };
    const cleanup = () => {
      window.removeEventListener('pointermove', move, true);
      window.removeEventListener('pointerup', up, true);
      window.removeEventListener('pointercancel', cancel, true);
    };
    window.addEventListener('pointermove', move, true);
    window.addEventListener('pointerup', up, true);
    window.addEventListener('pointercancel', cancel, true);
    event.preventDefault();
  }
}
