// Menu, scelte e domande che compaiono sopra alla pagina (mappa, calendario): uno per volta, si chiudono con Esc o
// con un clic fuori. Compaiono come i popup delle app (scala 0,96 -> 1, 200 ms) e se ne vanno
// in 150 ms.

import { h, icon } from './dom.js';
import { ui as uiIcons } from './icons.js';
import { show, hide, shake } from './motion.js';

export class Popups {
  constructor(root) {
    this.root = root;
    this.current = null;
    document.addEventListener('pointerdown', (event) => {
      if (this.current && !this.current.el.contains(event.target) && !this.current.anchor?.contains?.(event.target)) this.close();
    }, true);
    document.addEventListener('keydown', (event) => {
      if (!this.current) return;
      if (event.key === 'Escape') {
        event.preventDefault();
        event.stopPropagation();
        this.close();
      } else if (this.current.keys?.(event)) {
        event.preventDefault();
        event.stopPropagation();
      }
    }, true);
    window.addEventListener('blur', () => this.close());
    window.addEventListener('resize', () => this.close());
  }

  get isOpen() {
    return this.current != null;
  }

  close() {
    const open = this.current;
    if (!open) return;
    this.current = null;
    open.anchor?.classList?.remove('open');
    hide(open.el, { scale: 0.97, dy: 4, done: () => open.el.remove() });
    open.onClose?.();
  }

  /** Mette il popup vicino all'ancora (sotto, allineato a sinistra) o al punto dato. */
  place(el, where) {
    this.root.append(el);
    el.hidden = false;
    const box = el.getBoundingClientRect();
    const margin = 8;
    let x;
    let y;
    if (where instanceof Element) {
      const r = where.getBoundingClientRect();
      x = where.dataset.menuAlign === 'right' ? r.right - box.width : r.left;
      y = r.bottom + 6;
      if (y + box.height > innerHeight - margin) y = r.top - box.height - 6;
    } else {
      x = where.x;
      y = where.y;
      if (y + box.height > innerHeight - margin) y = where.y - box.height;
      if (x + box.width > innerWidth - margin) x = where.x - box.width;
    }
    el.style.left = `${Math.max(margin, Math.min(x, innerWidth - box.width - margin))}px`;
    el.style.top = `${Math.max(margin, Math.min(y, innerHeight - box.height - margin))}px`;
    el.style.transformOrigin = where instanceof Element ? 'top left' : `${where.x - x}px ${where.y - y}px`;
    show(el, { scale: 0.96, dy: 6, ms: 200 });
  }

  open(el, where, extra = {}) {
    this.close();
    const anchor = where instanceof Element ? where : null;
    anchor?.classList.add('open');
    this.current = { el, anchor, ...extra };
    this.place(el, where);
  }

  /**
   * Un menu: voci { label, icon, hint, danger, checked, disabled, action } o 'sep' o
   * { header }. Frecce, Invio ed Esc da tastiera.
   */
  menu(where, items, { width } = {}) {
    const el = h('div', { class: 'dm-pop dm-menu', role: 'menu', style: width ? { width: `${width}px` } : null });
    const rows = [];
    for (const item of items) {
      if (!item) continue;
      if (item === 'sep') {
        el.append(h('div', { class: 'dm-menu-sep', role: 'separator' }));
        continue;
      }
      if (item.header) {
        el.append(h('div', { class: 'dm-menu-head' }, item.header));
        continue;
      }
      const row = h('button', {
        class: `dm-menu-row${item.danger ? ' danger' : ''}${item.checked ? ' checked' : ''}`,
        role: 'menuitem',
        type: 'button',
        disabled: item.disabled,
        onclick: () => {
          this.close();
          item.action?.();
        },
      },
      h('span', { class: 'dm-menu-icon' }, item.checked ? icon(uiIcons.check, 14) : item.icon ? icon(item.icon, 14) : null),
      h('span', { class: 'dm-menu-label' }, item.label),
      item.hint ? h('span', { class: 'dm-menu-hint' }, item.hint) : null);
      row.addEventListener('pointerenter', () => row.focus({ preventScroll: true }));
      rows.push(row);
      el.append(row);
    }
    const enabled = () => rows.filter((row) => !row.disabled);
    this.open(el, where, {
      keys: (event) => {
        const list = enabled();
        const index = list.indexOf(document.activeElement);
        if (event.key === 'ArrowDown') list[(index + 1) % list.length]?.focus();
        else if (event.key === 'ArrowUp') list[(index - 1 + list.length) % list.length]?.focus();
        else return false;
        return true;
      },
    });
    return el;
  }

  /** Una domanda con un campo: Invio conferma, Esc lascia stare. */
  prompt(where, { title, value = '', placeholder = '', ok = 'Va bene', check }) {
    const input = h('input', { class: 'dm-field', value, placeholder, spellcheck: 'false', maxlength: 60 });
    const error = h('div', { class: 'dm-pop-error', hidden: true });
    const confirm = () => {
      const problem = check?.(input.value.trim());
      if (problem) {
        error.textContent = problem;
        error.hidden = false;
        shake(input);
        return;
      }
      this.close();
      this.current = null;
      done(input.value.trim());
    };
    let done = () => {};
    const el = h('div', { class: 'dm-pop dm-ask' },
      h('div', { class: 'dm-pop-title' }, title),
      input,
      error,
      h('div', { class: 'dm-pop-buttons' },
        h('button', { class: 'dm-button dm-press', type: 'button', onclick: () => this.close() }, 'Annulla'),
        h('button', { class: 'dm-button primary dm-press', type: 'button', onclick: confirm }, ok)));
    input.addEventListener('keydown', (event) => {
      if (event.key === 'Enter') {
        event.preventDefault();
        confirm();
      }
    });
    this.open(el, where);
    input.focus();
    input.select();
    return new Promise((resolve) => {
      done = resolve;
    });
  }

  /** Una conferma, per quello che non si annulla. */
  /**
   * Una conferma: Annulla e un pulsante (ok), oppure piu' risposte (choices: { label, value,
   * tone: 'danger' | 'primary' | '' }). Si risolve col valore scelto (true per ok); Annulla, Esc
   * o un clic fuori non la risolvono.
   */
  confirm(where, { title, text, ok = 'Elimina', danger = true, choices = null }) {
    let done = () => {};
    const answers = choices ?? [{ label: ok, value: true, tone: danger ? 'danger' : 'primary' }];
    const el = h('div', { class: 'dm-pop dm-ask' },
      h('div', { class: 'dm-pop-title' }, title),
      text ? h('div', { class: 'dm-pop-text' }, text) : null,
      h('div', { class: 'dm-pop-buttons' },
        h('button', { class: 'dm-button dm-press', type: 'button', onclick: () => this.close() }, 'Annulla'),
        answers.map((answer) => h('button', {
          class: `dm-button dm-press${answer.tone ? ` ${answer.tone}` : ''}`,
          type: 'button',
          onclick: () => {
            this.close();
            done(answer.value);
          },
        }, answer.label))));
    this.open(el, where);
    el.querySelector('.dm-button:last-child')?.focus();
    return new Promise((resolve) => {
      done = resolve;
    });
  }
}
