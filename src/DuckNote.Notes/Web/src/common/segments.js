// Le linguette con la pillola che scorre, come i segmentati delle app (Seg.Slide su Windows,
// Segments.Well sul Mac): la mappa e il calendario le usano uguali.

import { h, icon } from './dom.js';
import { tween, Curves, stop } from './motion.js';

/**
 * Le linguette con la pillola: la pillola si sposta sotto la voce scelta con la molla delle app
 * (520 ms in uscita elastica, la larghezza in 380 ms esponenziale).
 */
export function segments(items, onChange) {
  const pill = h('span', { class: 'dm-segment-pill', 'aria-hidden': 'true' });
  const wrap = h('div', { class: 'dm-segment', role: 'radiogroup' }, pill);
  const buttons = new Map();
  let value = null;
  let x = 0;
  let width = 0;
  for (const item of items) {
    const button = h('button', {
      class: 'dm-segment-item',
      type: 'button',
      role: 'radio',
      title: item.title,
      onclick: () => {
        if (value !== item.id) {
          set(item.id);
          onChange(item.id);
        }
      },
    }, item.icon ? icon(item.icon, 14) : null, h('span', {}, item.label));
    buttons.set(item.id, button);
    wrap.append(button);
  }
  const place = (immediate) => {
    const button = buttons.get(value);
    if (!button || !button.offsetWidth) return;
    const toX = button.offsetLeft;
    const toW = button.offsetWidth;
    if (immediate || !width) {
      stop(pill, 'x');
      stop(pill, 'w');
      x = toX;
      width = toW;
      pill.style.transform = `translateX(${x}px)`;
      pill.style.width = `${width}px`;
      return;
    }
    tween(pill, 'x', x, toX, 520, (v) => {
      x = v;
      pill.style.transform = `translateX(${v}px)`;
    }, null, Curves.elasticOut(1, 8));
    tween(pill, 'w', width, toW, 380, (v) => {
      width = v;
      pill.style.width = `${Math.max(0, v)}px`;
    }, null, Curves.expoOut(4));
  };
  const set = (id, immediate = false) => {
    value = id;
    for (const [key, button] of buttons) {
      button.classList.toggle('on', key === id);
      button.setAttribute('aria-checked', String(key === id));
    }
    place(immediate);
  };
  new ResizeObserver(() => place(true)).observe(wrap);
  return { el: wrap, set, get value() { return value; } };
}
