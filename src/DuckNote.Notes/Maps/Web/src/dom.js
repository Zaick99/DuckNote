// Piccoli attrezzi per costruire la pagina senza framework.

const SVG_NS = 'http://www.w3.org/2000/svg';

/** Un elemento HTML: h('button', { class: 'x', onclick }, 'testo', figlio...). */
export function h(tag, attrs = {}, ...children) {
  const el = document.createElement(tag);
  apply(el, attrs);
  append(el, children);
  return el;
}

/** Un elemento SVG, con gli stessi attributi. */
export function s(tag, attrs = {}, ...children) {
  const el = document.createElementNS(SVG_NS, tag);
  apply(el, attrs);
  append(el, children);
  return el;
}

function apply(el, attrs) {
  for (const [key, value] of Object.entries(attrs ?? {})) {
    if (value == null || value === false) continue;
    if (key.startsWith('on') && typeof value === 'function') el.addEventListener(key.slice(2), value);
    else if (key === 'style' && typeof value === 'object') {
      for (const [name, v] of Object.entries(value)) {
        if (name.startsWith('--')) el.style.setProperty(name, v);
        else el.style[name] = v;
      }
    }
    else if (key === 'dataset') Object.assign(el.dataset, value);
    else if (key === 'text') el.textContent = value;
    else el.setAttribute(key, value === true ? '' : String(value));
  }
}

function append(el, children) {
  for (const child of children.flat(Infinity)) {
    if (child == null || child === false) continue;
    el.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }
}

/** Sostituisce i figli di un elemento, saltando quelli che non ci sono (null, false). */
export function fill(el, ...children) {
  el.replaceChildren(...children.flat(Infinity).filter((child) => child != null && child !== false));
}

/** Un'icona a tratto (griglia 16x16 o 24x24), che prende il colore del testo intorno. */
export function icon(path, size = 16, grid = 16) {
  return s('svg', { class: 'dm-icon', width: size, height: size, viewBox: `0 0 ${grid} ${grid}`, 'aria-hidden': 'true' },
    s('path', { d: path }));
}

/** Imposta un attributo solo se cambia: il disegno si rifa' spesso, il DOM no. */
export function attr(el, name, value) {
  const text = value == null ? null : String(value);
  if (el.getAttribute(name) === text) return;
  if (text == null) el.removeAttribute(name);
  else el.setAttribute(name, text);
}

export function text(el, value) {
  const t = value ?? '';
  if (el.textContent !== t) el.textContent = t;
}

export function toggleClass(el, name, on) {
  if (el.classList.contains(name) !== !!on) el.classList.toggle(name, !!on);
}

// ------------------------------------------------------------------ misure del testo

const ruler = document.createElement('canvas').getContext('2d');
const widths = new Map();

export function measure(value, font) {
  const key = font + '\u0000' + value;
  let width = widths.get(key);
  if (width === undefined) {
    ruler.font = font;
    width = ruler.measureText(value).width;
    if (widths.size > 4000) widths.clear();
    widths.set(key, width);
  }
  return width;
}

/** Il testo tagliato con i puntini perche' stia in `max` pixel. */
export function fit(value, font, max) {
  const t = value ?? '';
  if (measure(t, font) <= max) return t;
  let low = 0;
  let high = t.length;
  while (low < high) {
    const mid = (low + high + 1) >> 1;
    if (measure(t.slice(0, mid) + '…', font) <= max) low = mid;
    else high = mid - 1;
  }
  return low > 0 ? t.slice(0, low).trimEnd() + '…' : '…';
}

export function isMac() {
  return /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent);
}

/** Il tasto Comando sul Mac, Ctrl altrove. */
export const modKey = (event) => (isMac() ? event.metaKey : event.ctrlKey);

export const shortcut = (key) => (isMac() ? `⌘${key}` : `Ctrl+${key}`);

/** Il campo di testo che ha il fuoco (li' i tasti sono della scrittura, non della mappa). */
export function typingIn(target) {
  const el = target ?? document.activeElement;
  if (!el || el.classList?.contains('dm-sink')) return false;
  return el.isContentEditable || el.tagName === 'TEXTAREA' || (el.tagName === 'INPUT' && !['checkbox', 'radio', 'button'].includes(el.type)) || el.tagName === 'SELECT';
}
