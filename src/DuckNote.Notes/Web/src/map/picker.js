// La scelta di un tipo di apparato: la griglia della libreria, in piccolo, in un popup.

import { h, icon } from '../common/dom.js';
import { kinds as kindIcons } from '../common/icons.js';
import { KINDS } from './model.js';

export function kindPicker(popups, x, y, choose, title = 'Aggiungi un apparato') {
  const grid = h('div', { class: 'dm-kind-grid' });
  for (const kind of KINDS) {
    grid.append(h('button', {
      class: 'dm-kind-tile dm-press',
      type: 'button',
      title: kind.label,
      style: { '--tone': `var(--c-${kind.tone})`, '--tone-soft': `var(--t-${kind.tone}-soft)` },
      onclick: () => {
        popups.close();
        choose(kind.id);
      },
    }, h('span', { class: 'dm-kind-tile-icon' }, icon(kindIcons[kind.id], 20, 24)), h('span', { class: 'dm-kind-tile-label' }, kind.label)));
  }
  const el = h('div', { class: 'dm-pop dm-picker' }, h('div', { class: 'dm-pop-title' }, title), grid);
  popups.open(el, { x, y });
  el.querySelector('button')?.focus({ preventScroll: true });
}
