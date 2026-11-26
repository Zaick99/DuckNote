// Gli indirizzi scritti nel testo (IPv4, IPv6, nomi di dominio), colorati col loro stato:
// verde raggiungibile, rosso spento, grigio mai provato. Le stesse regole di HostTokens.cs.
// Il colore e' una decorazione: il documento non cambia, quindi Annulla non lo vede mai.
import { Plugin, PluginKey } from 'prosemirror-state';
import { Decoration, DecorationSet } from 'prosemirror-view';

const Octet = '(?:25[0-5]|2[0-4]\\d|1\\d\\d|[1-9]?\\d)';
const Hex = '[0-9A-Fa-f]{1,4}';
const W = '[\\p{L}\\p{N}_]';

const ipv4 = new RegExp(`(?<![\\p{L}\\p{N}_.])(?:${Octet}\\.){3}${Octet}(?:/\\d{1,2})?(?!${W})(?!\\.\\d)`, 'gu');

const ipv6 = new RegExp('(?<![0-9A-Fa-f:.])(?:' + [
  `(?:${Hex}:){7}${Hex}`,
  `(?:${Hex}:){1,7}:`,
  `(?:${Hex}:){1,6}:${Hex}`,
  `(?:${Hex}:){1,5}(?::${Hex}){1,2}`,
  `(?:${Hex}:){1,4}(?::${Hex}){1,3}`,
  `(?:${Hex}:){1,3}(?::${Hex}){1,4}`,
  `(?:${Hex}:){1,2}(?::${Hex}){1,5}`,
  `${Hex}:(?::${Hex}){1,6}`,
  `:(?::${Hex}){1,7}`,
  `(?:${Hex}:){1,6}:(?:${Octet}\\.){3}${Octet}`,
  `::(?:[Ff]{4}(?::0{1,4})?:)?(?:${Octet}\\.){3}${Octet}`,
].join('|') + ')(?:%[A-Za-z0-9_.-]+)?(?![0-9A-Fa-f:])(?!\\.\\d)', 'gu');

const fqdn = new RegExp(`(?<![\\p{L}\\p{N}_.@-])(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\\.)+[A-Za-z]{2,24}(?![\\p{L}\\p{N}_-])`, 'gu');

const fileSuffixes = new Set((
  'md txt rtf exe dll msi png jpg jpeg gif svg webp ico bmp tiff css js jsx ts tsx py rb go rs java ' +
  'cs cpp hpp sh bash zsh bat cmd xml json yaml yml toml ini cfg conf log lock env bak tmp swp old ' +
  'orig csv tsv pdf doc docx xls xlsx ppt pptx zip tar gz bz xz rar iso img bin dat db sqlite sql ' +
  'html htm xaml razor vue svelte mp wav flac ogg ttf otf woff eot').split(' '));

export function isDomain(name) {
  if (name.length > 253) return false;
  const suffix = name.slice(name.lastIndexOf('.') + 1);
  return !fileSuffixes.has(suffix.toLowerCase()) && (suffix === suffix.toLowerCase() || suffix === suffix.toUpperCase());
}

// [{start, end, value}] in ordine; IPv6 prima di IPv4 e IPv4 prima dei nomi
export function findHosts(text) {
  if (!text || !text.trim()) return [];
  const found = [];
  for (const pattern of [ipv6, ipv4, fqdn]) {
    pattern.lastIndex = 0;
    for (const match of text.matchAll(pattern)) {
      if (pattern === fqdn && !isDomain(match[0])) continue;
      const token = { start: match.index, end: match.index + match[0].length, value: match[0] };
      if (!found.some((other) => token.start < other.end && token.end > other.start)) found.push(token);
    }
  }
  return found.sort((a, b) => a.start - b.start);
}

export const hostKey = new PluginKey('hosts');

// Il testo di un blocco con la posizione di ogni carattere nel documento. I riquadri e gli a capo
// spezzano le parole: un indirizzo non passa sopra a un disegno.
function blockText(node, pos) {
  let text = '';
  const at = [];
  node.forEach((child, offset) => {
    if (child.isText) {
      for (let i = 0; i < child.text.length; i++) at.push(pos + 1 + offset + i);
      text += child.text;
    } else {
      at.push(pos + 1 + offset);
      text += '\n';
    }
  });
  return { text, at };
}

function decorateBlock(node, pos, states, hidden, typing) {
  if (!node.isTextblock || node.type.spec.code) return [];
  const { text, at } = blockText(node, pos);
  const decorations = [];
  for (const token of findHosts(text)) {
    const from = at[token.start];
    const to = at[token.end - 1] + 1;
    // la parola che si sta scrivendo non e' ancora un indirizzo (cursore dentro o subito dopo)
    if (typing != null && typing > from && typing <= to) continue;
    const key = token.value.toLowerCase();
    if (hidden.has(key)) continue;
    const state = states[key] ?? states[token.value] ?? 'unknown';
    decorations.push(Decoration.inline(from, to, { class: `dn-host dn-host-${state}`, 'data-host': token.value }, { host: token.value }));
  }
  return decorations;
}

function build(doc, states, hidden, typing) {
  const decorations = [];
  doc.descendants((node, pos) => {
    if (node.isTextblock) {
      decorations.push(...decorateBlock(node, pos, states, hidden, typing));
      return false;
    }
    return true;
  });
  return DecorationSet.create(doc, decorations);
}

// i blocchi di testo che una transazione ha toccato, nel documento nuovo
function touched(tr, extra) {
  const ranges = [];
  tr.mapping.maps.forEach((map, i) => {
    map.forEach((oldStart, oldEnd, newStart, newEnd) => {
      const rest = tr.mapping.slice(i + 1);
      ranges.push([rest.map(newStart, -1), rest.map(newEnd, 1)]);
    });
  });
  for (const pos of extra) if (pos != null) ranges.push([pos, pos]);
  const blocks = new Map();
  for (const [from, to] of ranges) {
    const start = Math.max(0, Math.min(from, tr.doc.content.size));
    const end = Math.max(start, Math.min(to, tr.doc.content.size));
    tr.doc.nodesBetween(start, end, (node, pos) => {
      if (node.isTextblock) {
        blocks.set(pos, node);
        return false;
      }
      return true;
    });
  }
  return blocks;
}

// Quanto si aspetta, a parola ferma, prima che la parola sotto al cursore diventi un indirizzo:
// e' l'impostazione "Una parola diventa host dopo (ms)" della versione WPF (5000 di serie).
let settleDelay = 5000;

export function setSettleDelay(ms) {
  if (Number.isFinite(ms)) settleDelay = Math.max(500, Math.min(30000, ms));
}

// L'indirizzo che si sta ancora scrivendo (cursore dentro o subito dopo), se ce n'e' uno: la
// finestra non lo mette in tabella finche' non si ferma o il cursore non se ne va.
export function typingHost(state) {
  const typing = hostKey.getState(state)?.typing;
  if (typing == null) return null;
  const $pos = state.doc.resolve(Math.max(0, Math.min(typing, state.doc.content.size)));
  const node = $pos.parent;
  if (!node.isTextblock || node.type.spec.code) return null;
  const { text, at } = blockText(node, $pos.before($pos.depth));
  for (const token of findHosts(text)) {
    const from = at[token.start];
    const to = at[token.end - 1] + 1;
    if (typing > from && typing <= to) return token.value;
  }
  return null;
}

export function hostsPlugin() {
  return new Plugin({
    key: hostKey,
    state: {
      init: (_, state) => ({ states: {}, hidden: new Set(), typing: null, deco: build(state.doc, {}, new Set(), null) }),
      apply(tr, prev, oldState, newState) {
        const meta = tr.getMeta(hostKey);
        if (meta?.states || meta?.hidden) {
          const states = meta.states ?? prev.states;
          const hidden = meta.hidden ? new Set(meta.hidden.map((h) => h.toLowerCase())) : prev.hidden;
          return { states, hidden, typing: prev.typing, deco: build(newState.doc, states, hidden, prev.typing) };
        }
        if (meta?.settle) {
          if (prev.typing == null) return prev;
          const blocks = touched(tr, [prev.typing]);
          let deco = prev.deco;
          for (const [pos, node] of blocks) {
            deco = deco.remove(deco.find(pos, pos + node.nodeSize));
            deco = deco.add(newState.doc, decorateBlock(node, pos, prev.states, prev.hidden, null));
          }
          return { ...prev, typing: null, deco };
        }
        if (!tr.docChanged && !tr.selectionSet) return prev;

        // chi scrive tiene aperta la parola sotto al cursore; chi si sposta la chiude
        const typing = tr.docChanged ? newState.selection.head : null;
        let deco = prev.deco.map(tr.mapping, tr.doc);
        const oldTyping = prev.typing == null ? null : tr.mapping.map(prev.typing);
        const blocks = tr.docChanged || typing !== prev.typing ? touched(tr, [oldTyping, typing]) : new Map();
        for (const [pos, node] of blocks) {
          deco = deco.remove(deco.find(pos, pos + node.nodeSize));
          deco = deco.add(newState.doc, decorateBlock(node, pos, prev.states, prev.hidden, typing));
        }
        return { states: prev.states, hidden: prev.hidden, typing, deco };
      },
    },
    props: {
      decorations: (state) => hostKey.getState(state).deco,
    },
    view(view) {
      let timer = null;
      return {
        update(view, prevState) {
          if (view.state.doc === prevState.doc) return;
          clearTimeout(timer);
          timer = setTimeout(() => {
            if (!view.isDestroyed) view.dispatch(view.state.tr.setMeta(hostKey, { settle: true }));
          }, settleDelay);
        },
        destroy() { clearTimeout(timer); },
      };
    },
  });
}

// l'indirizzo sotto a un punto del documento (per il menu del tasto destro)
export function hostAt(state, pos) {
  const deco = hostKey.getState(state).deco.find(pos, pos);
  const hit = deco.find((d) => d.from <= pos && d.to >= pos);
  return hit?.spec?.host ?? null;
}
