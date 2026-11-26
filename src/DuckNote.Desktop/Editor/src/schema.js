// Lo schema della nota: una riga per nodo, come nel file della nota (vedi SheetJson.cs).
// Il tipo di una riga e' il tipo del nodo: paragraph, heading, quote, bullet, number, todo,
// rule, code_block. Gli elenchi sono righe "bullet" una dopo l'altra, non nodi annidati:
// Invio fa una riga normale, Maiusc+Invio un'altra voce, e la numerazione la fa il CSS.
import { Schema } from 'prosemirror-model';
import { tableNodes } from 'prosemirror-tables';

const lineNode = (cls, extra = {}) => ({
  group: 'block line',
  content: 'inline*',
  defining: true,
  parseDOM: [{ tag: `p.${cls}`, ...extra.parse }],
  toDOM: extra.toDOM ?? (() => ['p', { class: cls }, 0]),
  ...extra.node,
});

export const schema = new Schema({
  nodes: {
    doc: { content: 'block+' },

    paragraph: {
      group: 'block line',
      content: 'inline*',
      parseDOM: [{ tag: 'p' }, { tag: 'div', priority: 10 }],
      toDOM: () => ['p', 0],
    },

    heading: {
      group: 'block line',
      content: 'inline*',
      defining: true,
      attrs: { level: { default: 1 } },
      parseDOM: [1, 2, 3, 4, 5, 6].map((level) => ({ tag: `h${level}`, attrs: { level: Math.min(level, 3) } })),
      toDOM: (node) => [`h${node.attrs.level}`, 0],
    },

    quote: lineNode('dn-quote'),
    bullet: lineNode('dn-bullet'),
    number: lineNode('dn-number'),

    todo: lineNode('dn-todo', {
      node: { attrs: { done: { default: false } } },
      parse: { getAttrs: (dom) => ({ done: dom.getAttribute('data-done') === 'true' }) },
      toDOM: (node) => ['p', { class: 'dn-todo', 'data-done': node.attrs.done ? 'true' : 'false' }, 0],
    }),

    rule: {
      group: 'block',
      atom: true,
      selectable: true,
      parseDOM: [{ tag: 'hr' }],
      toDOM: () => ['hr', { class: 'dn-rule' }],
    },

    code_block: {
      group: 'block',
      content: 'text*',
      marks: '',
      code: true,
      defining: true,
      parseDOM: [{ tag: 'pre', preserveWhitespace: 'full' }],
      toDOM: () => ['pre', { class: 'dn-code', spellcheck: 'false' }, ['code', 0]],
    },

    ...tableNodes({ tableGroup: 'block', cellContent: 'line+', cellAttributes: {} }),

    text: { group: 'inline' },

    hard_break: {
      group: 'inline',
      inline: true,
      selectable: false,
      parseDOM: [{ tag: 'br' }],
      toDOM: () => ['br'],
    },

    // un disegno, un'immagine o un grafico in mezzo al testo, che il testo gira attorno;
    // i byte non stanno nel documento ma nella nota (vedi insets.js)
    inset: {
      group: 'inline',
      inline: true,
      atom: true,
      draggable: true,
      attrs: { id: {}, kind: { default: 'chart' }, width: { default: 360 }, height: { default: 230 } },
      parseDOM: [{
        tag: 'span.dn-inset[data-id]',
        getAttrs: (dom) => ({
          id: dom.getAttribute('data-id'),
          kind: dom.getAttribute('data-kind') || 'chart',
          width: Number(dom.getAttribute('data-width')) || 360,
          height: Number(dom.getAttribute('data-height')) || 230,
        }),
      }],
      toDOM: (node) => ['span', {
        class: 'dn-inset',
        'data-id': node.attrs.id,
        'data-kind': node.attrs.kind,
        'data-width': node.attrs.width,
        'data-height': node.attrs.height,
      }],
    },
  },

  marks: {
    link: {
      attrs: { href: {} },
      inclusive: false,
      parseDOM: [{ tag: 'a[href]', getAttrs: (dom) => ({ href: dom.getAttribute('href') }) }],
      toDOM: (mark) => ['a', { href: mark.attrs.href, title: `${mark.attrs.href}  (${isMac() ? '⌘' : 'Ctrl'}+clic per aprire)` }, 0],
    },
    bold: {
      parseDOM: [
        { tag: 'strong' },
        { tag: 'b', getAttrs: (node) => node.style.fontWeight !== 'normal' && null },
        { style: 'font-weight', getAttrs: (value) => /^(bold(er)?|[6-9]\d{2,})$/.test(value) && null },
      ],
      toDOM: () => ['strong', 0],
    },
    italic: {
      parseDOM: [{ tag: 'i' }, { tag: 'em' }, { style: 'font-style=italic' }],
      toDOM: () => ['em', 0],
    },
    underline: {
      parseDOM: [{ tag: 'u' }, { style: 'text-decoration=underline' }],
      toDOM: () => ['u', 0],
    },
    strike: {
      parseDOM: [{ tag: 's' }, { tag: 'del' }, { tag: 'strike' }, { style: 'text-decoration=line-through' }],
      toDOM: () => ['s', 0],
    },
    highlight: {
      parseDOM: [{ tag: 'mark' }],
      toDOM: () => ['mark', 0],
    },
    code: {
      parseDOM: [{ tag: 'code' }],
      toDOM: () => ['code', { class: 'dn-mono' }, 0],
    },
    // il colore e' un token del tema: cambia da solo con chiaro e scuro
    colour: {
      attrs: { token: {} },
      parseDOM: [{ tag: 'span[data-colour]', getAttrs: (dom) => ({ token: dom.getAttribute('data-colour') }) }],
      toDOM: (mark) => ['span', { 'data-colour': mark.attrs.token, style: `color: var(--c-${mark.attrs.token})` }, 0],
    },
  },
});

export function isMac() {
  return /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent);
}

// i tipi di riga che si possono dare dalla barra, col nome che usa il C#
export const looks = {
  text: { type: 'paragraph' },
  h1: { type: 'heading', attrs: { level: 1 } },
  h2: { type: 'heading', attrs: { level: 2 } },
  h3: { type: 'heading', attrs: { level: 3 } },
  quote: { type: 'quote' },
  bullet: { type: 'bullet' },
  number: { type: 'number' },
  todo: { type: 'todo' },
};

export function lookOf(node) {
  if (!node) return 'text';
  switch (node.type.name) {
    case 'heading': return `h${node.attrs.level}`;
    case 'quote':
    case 'bullet':
    case 'number':
    case 'todo':
      return node.type.name;
    case 'code_block': return 'code';
    default: return 'text';
  }
}
