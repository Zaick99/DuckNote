// I comandi della nota: la tastiera (Invio, Maiusc+Invio, Backspace, le scorciatoie della
// barra), i segni scritti a inizio riga ("# ", "- ", "1. "...) e i comandi che arrivano dalla
// barra della finestra. Le regole sono quelle della versione WPF:
//  - Invio fa una riga nuova pulita: niente titolo, elenco, citazione, codice, grassetto o colore;
//  - Maiusc+Invio fa una riga nuova che mantiene la formattazione (la voce dopo, la riga di codice dopo);
//  - Backspace all'inizio di una riga con un tipo toglie il tipo, non incolla la riga a quella sopra;
//  - un segno senza selezione vale per la parola sotto il cursore, o per quel che si scrive dopo.
import { TextSelection, NodeSelection, Selection, Plugin } from 'prosemirror-state';
import { toggleMark, chainCommands, deleteSelection, joinBackward, selectNodeBackward, exitCode, newlineInCode } from 'prosemirror-commands';
import { undo, redo, undoDepth, redoDepth } from 'prosemirror-history';
import { inputRules, InputRule, textblockTypeInputRule, undoInputRule } from 'prosemirror-inputrules';
import { goToNextCell, isInTable, addRowAfter, deleteRow, addColumnAfter, deleteColumn, deleteTable } from 'prosemirror-tables';
import { Fragment } from 'prosemirror-model';
import { schema, looks, lookOf } from './schema.js';

const n = schema.nodes;
const m = schema.marks;

// ------------------------------------------------------------------ Invio

function inCode(state) {
  return state.selection.$from.parent.type === n.code_block;
}

// "---", "***" o "___" e Invio: una separazione
function ruleFromDashes(state, dispatch) {
  const { $from, empty } = state.selection;
  const block = $from.parent;
  if (!empty || block.type !== n.paragraph || $from.parentOffset !== block.content.size) return false;
  if (!/^\s*(-{3,}|\*{3,}|_{3,})\s*$/.test(block.textContent)) return false;
  if (dispatch) {
    const start = $from.before();
    const tr = state.tr.replaceWith(start, start + block.nodeSize, [n.rule.create(), n.paragraph.create()]);
    tr.setSelection(TextSelection.create(tr.doc, start + 2));
    dispatch(tr.scrollIntoView());
  }
  return true;
}

export function plainEnter(state, dispatch) {
  if (inCode(state)) {
    // Invio esce dal blocco di codice: la riga dopo e' testo (quella vuota che c'e' gia', se c'e')
    const { $from } = state.selection;
    if (dispatch) {
      const after = $from.after();
      const next = state.doc.resolve(after).nodeAfter;
      const tr = state.tr;
      if (!(next?.type === n.paragraph && next.content.size === 0)) tr.insert(after, n.paragraph.create());
      tr.setSelection(TextSelection.create(tr.doc, after + 1));
      dispatch(tr.scrollIntoView());
    }
    return true;
  }
  if (ruleFromDashes(state, dispatch)) return true;
  if (!dispatch) return true;

  let tr = state.tr;
  if (!state.selection.empty) tr = tr.deleteSelection();
  const { $from } = tr.selection;
  const block = $from.parent;

  if (!block.isTextblock) return false;

  // all'inizio di una riga piena: una riga pulita sopra, e questa resta com'e'
  if ($from.parentOffset === 0 && block.content.size > 0) {
    tr.insert($from.before(), n.paragraph.create());
    dispatch(tr.scrollIntoView());
    return true;
  }

  tr.split($from.pos, 1, [{ type: n.paragraph }]);
  tr.setStoredMarks([]);
  dispatch(tr.scrollIntoView());
  return true;
}

export function keepEnter(state, dispatch) {
  if (inCode(state)) return newlineInCode(state, dispatch);
  if (!dispatch) return true;
  let tr = state.tr;
  if (!state.selection.empty) tr = tr.deleteSelection();
  const { $from } = tr.selection;
  const block = $from.parent;
  if (!block.isTextblock) return false;
  const marks = state.storedMarks ?? $from.marks();
  const attrs = block.type === n.todo ? { done: false } : block.attrs;
  tr.split($from.pos, 1, [{ type: block.type, attrs }]);
  tr.setStoredMarks(marks);
  dispatch(tr.scrollIntoView());
  return true;
}

// ------------------------------------------------------------------ Backspace

// all'inizio di una riga con un tipo il tipo se ne va (e il testo resta dov'e')
function unformatAtStart(state, dispatch) {
  const { $from, empty } = state.selection;
  if (!empty || $from.parentOffset !== 0) return false;
  const block = $from.parent;
  if (block.type === n.paragraph || !block.isTextblock) return false;
  if (block.type === n.code_block) {
    if (dispatch) {
      const lines = block.textContent.split('\n');
      const paragraphs = lines.map((line) => n.paragraph.create(null, line ? schema.text(line) : null));
      const start = $from.before();
      const tr = state.tr.replaceWith(start, start + block.nodeSize, paragraphs);
      tr.setSelection(TextSelection.create(tr.doc, start + 1));
      dispatch(tr.scrollIntoView());
    }
    return true;
  }
  if (dispatch) dispatch(state.tr.setBlockType($from.pos, $from.pos, n.paragraph).scrollIntoView());
  return true;
}

// ------------------------------------------------------------------ segni sul testo

// la parola sotto il cursore (null se il cursore non e' in una parola)
// La parola sotto al cursore. Per i segni vale solo col cursore dentro alla parola, come in
// Word e nella versione WPF: in fondo a una parola appena scritta in grassetto, Ctrl+B smette
// il grassetto per quel che segue e non lo toglie alla parola. Il collegamento invece prende
// anche la parola appena finita (edge).
function wordAt(state, edge = false) {
  const { $from } = state.selection;
  const block = $from.parent;
  if (!block.isTextblock) return null;
  const text = block.textBetween(0, block.content.size, '\u0000', '\u0000');
  const at = $from.parentOffset;
  const isWord = (c) => c && /[\p{L}\p{N}_.\-:@/]/u.test(c);
  if (!isWord(text[at - 1]) && !isWord(text[at])) return null;
  let start = at;
  let end = at;
  while (start > 0 && isWord(text[start - 1])) start--;
  while (end < text.length && isWord(text[end])) end++;
  // niente punti o due punti alle estremita': la fine di una frase non e' parte della parola
  while (start < end && /[.:\-]/.test(text[start])) start++;
  while (end > start && /[.:\-]/.test(text[end - 1])) end--;
  if (start === end) return null;
  if (!edge && (at <= start || at >= end)) return null;
  const base = $from.start();
  return { from: base + start, to: base + end };
}

// dove va un riquadro: al cursore, ma mai a meta' di una parola (spezzerebbe un indirizzo,
// che smetterebbe di essere riconosciuto): col cursore dentro a una parola, al suo inizio
export function insetSpot(state) {
  const { empty, from } = state.selection;
  if (!empty) return null;
  return wordAt(state)?.from ?? from;
}

// dove vale un segno: la selezione (senza gli spazi in fondo, come in Word), o la parola sotto al cursore
function target(state, edge = false) {
  const { from, to, empty } = state.selection;
  if (!empty) {
    let end = to;
    const text = state.doc.textBetween(from, to, '\n', '\u0000');
    const trailing = text.length - text.trimEnd().length;
    if (trailing > 0 && trailing < text.length) end = to - trailing;
    return { from, to: end };
  }
  return wordAt(state, edge);
}

export function hasMark(state, type) {
  const range = target(state);
  if (!range) return !!type.isInSet(state.storedMarks ?? state.selection.$from.marks());
  return state.doc.rangeHasMark(range.from, range.to, type);
}

export function toggle(type, attrs = null) {
  return (state, dispatch) => {
    const range = target(state);
    if (!range) return toggleMark(type, attrs)(state, dispatch);
    if (dispatch) {
      const tr = state.tr;
      if (state.doc.rangeHasMark(range.from, range.to, type)) tr.removeMark(range.from, range.to, type);
      else tr.addMark(range.from, range.to, type.create(attrs));
      dispatch(tr);
    }
    return true;
  };
}

export function colour(token) {
  return (state, dispatch) => {
    const range = target(state);
    const automatic = !token || token === 'Label';
    if (!range) {
      if (dispatch) {
        const marks = (state.storedMarks ?? state.selection.$from.marks()).filter((mark) => mark.type !== m.colour);
        dispatch(state.tr.setStoredMarks(automatic ? marks : [...marks, m.colour.create({ token })]));
      }
      return true;
    }
    if (dispatch) {
      const tr = state.tr.removeMark(range.from, range.to, m.colour);
      if (!automatic) tr.addMark(range.from, range.to, m.colour.create({ token }));
      dispatch(tr);
    }
    return true;
  };
}

export function link(href, text) {
  return (state, dispatch) => {
    const range = target(state, true);
    if (!dispatch) return true;
    const tr = state.tr;
    if (!href) {
      // togli il collegamento dove sta il cursore
      const $pos = state.selection.$from;
      const found = linkRange(state, $pos.pos);
      if (found) tr.removeMark(found.from, found.to, m.link);
      dispatch(tr);
      return true;
    }
    const mark = m.link.create({ href });
    const existing = linkRange(state, state.selection.from);
    if (existing && state.selection.empty) {
      tr.removeMark(existing.from, existing.to, m.link).addMark(existing.from, existing.to, mark);
      if (text && text !== state.doc.textBetween(existing.from, existing.to)) {
        tr.insertText(text, existing.from, existing.to).addMark(existing.from, existing.from + text.length, mark);
      }
    } else if (range && (!text || text === state.doc.textBetween(range.from, range.to))) {
      tr.addMark(range.from, range.to, mark);
    } else {
      const words = text || href;
      const from = state.selection.from;
      tr.replaceSelectionWith(schema.text(words, [mark]), false);
      tr.setSelection(TextSelection.create(tr.doc, from + words.length));
      tr.removeStoredMark(m.link);
    }
    dispatch(tr.scrollIntoView());
    return true;
  };
}

export function linkRange(state, pos) {
  const $pos = state.doc.resolve(pos);
  const block = $pos.parent;
  if (!block.isTextblock) return null;
  let found = null;
  const base = $pos.start();
  let offset = 0;
  block.forEach((child) => {
    const start = base + offset;
    const end = start + child.nodeSize;
    const mark = m.link.isInSet(child.marks);
    if (mark && pos >= start && pos <= end) {
      if (found && found.href === mark.attrs.href && found.to === start) found.to = end;
      else if (!found) found = { from: start, to: end, href: mark.attrs.href };
    } else if (mark && found && found.href === mark.attrs.href && found.to === start) {
      found.to = end;
    }
    offset += child.nodeSize;
  });
  return found;
}

export function linkAt(state) {
  const mark = m.link.isInSet(state.selection.$from.marks()) ?? m.link.isInSet(state.selection.$to.marks());
  return mark ? mark.attrs.href : null;
}

// ------------------------------------------------------------------ tipo di riga

function selectedBlocks(state) {
  const { from, to } = state.selection;
  const blocks = [];
  state.doc.nodesBetween(from, to, (node, pos) => {
    if (node.isTextblock) {
      blocks.push({ node, pos });
      return false;
    }
    return true;
  });
  return blocks;
}

export function currentLook(state) {
  return lookOf(state.selection.$from.parent);
}

// le righe selezionate prendono un tipo; se lo avevano gia' tutte tornano testo normale
export function setLook(look) {
  return (state, dispatch) => {
    const wanted = looks[look] ?? looks.text;
    const type = n[wanted.type];
    const blocks = selectedBlocks(state).filter((b) => b.node.type !== n.code_block);
    if (blocks.length === 0) return false;
    const all = look !== 'text' && blocks.every((b) => lookOf(b.node) === look);
    if (dispatch) {
      const tr = state.tr;
      for (const { node, pos } of blocks) {
        const next = all ? n.paragraph : type;
        const attrs = all ? null : wanted.attrs ?? (next === n.todo ? { done: false } : null);
        if (node.type !== next || JSON.stringify(node.attrs) !== JSON.stringify(attrs ?? {})) {
          tr.setNodeMarkup(tr.mapping.map(pos), next, attrs);
        }
      }
      dispatch(tr.scrollIntoView());
    }
    return true;
  };
}

// righe selezionate -> un blocco di codice; un blocco di codice -> le sue righe
export function toggleCode(state, dispatch) {
  const { $from } = state.selection;
  if ($from.parent.type === n.code_block) {
    if (dispatch) {
      const block = $from.parent;
      const start = $from.before();
      const lines = block.textContent.split('\n').map((line) => n.paragraph.create(null, line ? schema.text(line) : null));
      const tr = state.tr.replaceWith(start, start + block.nodeSize, lines);
      tr.setSelection(TextSelection.create(tr.doc, start + 1));
      dispatch(tr.scrollIntoView());
    }
    return true;
  }
  const blocks = selectedBlocks(state);
  if (blocks.length === 0 || blocks.some((b) => b.node.type === n.code_block)) return false;
  if ($from.node(-1)?.type?.spec?.tableRole) return false;
  if (dispatch) {
    const first = blocks[0].pos;
    const last = blocks[blocks.length - 1];
    const text = blocks.map((b) => b.node.textContent).join('\n');
    const tr = state.tr.replaceWith(first, last.pos + last.node.nodeSize, n.code_block.create(null, text ? schema.text(text) : null));
    tr.setSelection(TextSelection.create(tr.doc, first + 1 + text.length));
    dispatch(tr.scrollIntoView());
  }
  return true;
}

// dove mettere un blocco nuovo: al posto della riga vuota dove sta il cursore, o sotto la riga
function blockSpot(state) {
  const { $from } = state.selection;
  if ($from.depth === 1 && $from.parent.type === n.paragraph && $from.parent.content.size === 0) {
    return { from: $from.before(1), to: $from.after(1) };
  }
  const after = $from.depth > 0 ? $from.after(1) : $from.pos;
  return { from: after, to: after };
}

export function insertRule(state, dispatch) {
  const { $from } = state.selection;
  if ($from.node(-1)?.type?.spec?.tableRole) return false;
  if (dispatch) {
    const spot = blockSpot(state);
    const tr = state.tr.replaceWith(spot.from, spot.to, [n.rule.create(), n.paragraph.create()]);
    tr.setSelection(TextSelection.create(tr.doc, spot.from + 2));
    dispatch(tr.scrollIntoView());
  }
  return true;
}

// La pagina finisce sempre con una riga di testo (dopo una tabella o un blocco di codice ci si
// deve poter scrivere), e il cursore sta sempre in una riga: un clic nello spazio sotto al
// testo lo porta in fondo all'ultima, non in un punto dove i comandi non valgono.
export function tidyPlugin() {
  return new Plugin({
    appendTransaction(transactions, oldState, state) {
      let tr = null;
      const last = state.doc.lastChild;
      if (transactions.some((t) => t.docChanged) && last && !last.type.spec.group?.split(' ').includes('line')) {
        tr = state.tr.insert(state.doc.content.size, n.paragraph.create());
      }
      const selection = (tr ?? state).selection;
      if (selection instanceof TextSelection && !selection.$head.parent.inlineContent) {
        tr = tr ?? state.tr;
        tr.setSelection(Selection.near(tr.doc.resolve(Math.min(selection.head, tr.doc.content.size)), -1));
      }
      return tr;
    },
  });
}

export function withTrailingLine(doc) {
  const last = doc.lastChild;
  if (last && last.type.spec.group?.split(' ').includes('line')) return doc;
  return doc.copy(doc.content.append(Fragment.from(n.paragraph.create())));
}

// toglie segni e tipo di riga dalla selezione (o dalla parola sotto al cursore)
export function clearFormatting(state, dispatch) {
  if (!dispatch) return true;
  const tr = state.tr;
  const range = target(state);
  if (range) {
    for (const type of Object.values(m)) {
      if (type !== m.link) tr.removeMark(range.from, range.to, type);
    }
  }
  tr.setStoredMarks([]);
  for (const { node, pos } of selectedBlocks(state)) {
    if (node.type !== n.paragraph && node.type !== n.code_block) tr.setNodeMarkup(tr.mapping.map(pos), n.paragraph);
  }
  dispatch(tr);
  return true;
}

// ------------------------------------------------------------------ tabelle

export function insertTable(state, dispatch, columns = 3, rows = 2) {
  const { $from } = state.selection;
  if (isInTable(state)) return false;
  if (dispatch) {
    const header = Array.from({ length: columns }, (_, i) => n.table_header.create(null, n.paragraph.create(null, schema.text(`Colonna ${i + 1}`))));
    const body = Array.from({ length: rows - 1 }, () =>
      n.table_row.create(null, Array.from({ length: columns }, () => n.table_cell.create(null, n.paragraph.create()))));
    const table = n.table.create(null, [n.table_row.create(null, header), ...body]);
    const spot = blockSpot(state);
    const after = spot.from;
    const tr = state.tr.replaceWith(spot.from, spot.to, [table, n.paragraph.create()]);
    // il cursore nella prima cella del corpo (o dell'intestazione)
    let target = null;
    tr.doc.nodesBetween(after, after + table.nodeSize, (node, pos) => {
      if (target == null && node.type === n.table_cell) target = pos + 2;
      return target == null;
    });
    tr.setSelection(TextSelection.create(tr.doc, target ?? after + 4));
    dispatch(tr.scrollIntoView());
  }
  return true;
}

export const tableCommands = { addRowAfter, deleteRow, addColumnAfter, deleteColumn, deleteTable };

// ------------------------------------------------------------------ spunte

export function toggleTodo(pos) {
  return (state, dispatch) => {
    const node = state.doc.nodeAt(pos);
    if (!node || node.type !== n.todo) return false;
    if (dispatch) dispatch(state.tr.setNodeMarkup(pos, null, { done: !node.attrs.done }));
    return true;
  };
}

// ------------------------------------------------------------------ segni scritti a inizio riga

function onlyFrom(types, rule) {
  // la regola vale solo se la riga e' di uno dei tipi dati (di solito: testo normale)
  return new InputRule(rule.match, (state, match, start, end) => {
    const block = state.doc.resolve(start).parent;
    if (!types.includes(block.type)) return null;
    return rule.handler(state, match, start, end);
  });
}

export function markdownRules() {
  const plain = [n.paragraph];
  return inputRules({
    rules: [
      onlyFrom(plain, textblockTypeInputRule(/^###\s$/, n.heading, { level: 3 })),
      onlyFrom(plain, textblockTypeInputRule(/^##\s$/, n.heading, { level: 2 })),
      onlyFrom(plain, textblockTypeInputRule(/^#\s$/, n.heading, { level: 1 })),
      onlyFrom(plain, textblockTypeInputRule(/^[-*]\s$/, n.bullet)),
      onlyFrom(plain, textblockTypeInputRule(/^1\.\s$/, n.number)),
      onlyFrom([n.paragraph, n.bullet], textblockTypeInputRule(/^\[ ?\]\s$/, n.todo, { done: false })),
      onlyFrom(plain, textblockTypeInputRule(/^>\s$/, n.quote)),
      onlyFrom(plain, textblockTypeInputRule(/^```\s$/, n.code_block)),
    ],
  });
}

// ------------------------------------------------------------------ la tastiera

export function editorKeys(actions) {
  const mod = 'Mod-';
  return {
    Enter: chainCommands(plainEnter),
    'Shift-Enter': keepEnter,
    Backspace: chainCommands(undoInputRule, deleteSelection, unformatAtStart, joinBackward, selectNodeBackward),
    'Mod-Backspace': chainCommands(undoInputRule, deleteSelection, unformatAtStart, joinBackward, selectNodeBackward),
    Tab: (state, dispatch) => (isInTable(state) ? goToNextCell(1)(state, dispatch) : inCode(state) ? (dispatch && dispatch(state.tr.insertText('    ')), true) : false),
    'Shift-Tab': (state, dispatch) => (isInTable(state) ? goToNextCell(-1)(state, dispatch) : false),
    'Mod-Enter': exitCode,

    [`${mod}z`]: undo,
    [`${mod}y`]: redo,
    [`${mod}Shift-z`]: redo,

    [`${mod}b`]: toggle(m.bold),
    [`${mod}i`]: toggle(m.italic),
    [`${mod}u`]: toggle(m.underline),
    [`${mod}Shift-x`]: toggle(m.strike),
    [`${mod}Alt-h`]: toggle(m.highlight),
    [`${mod}e`]: toggle(m.code),
    [`${mod}Space`]: clearFormatting,

    [`${mod}Alt-0`]: setLook('text'),
    [`${mod}Alt-1`]: setLook('h1'),
    [`${mod}Alt-2`]: setLook('h2'),
    [`${mod}Alt-3`]: setLook('h3'),
    [`${mod}Shift-8`]: setLook('bullet'),
    [`${mod}Shift-7`]: setLook('number'),
    [`${mod}Shift-9`]: setLook('todo'),
    [`${mod}Shift-q`]: setLook('quote'),
    [`${mod}Shift-l`]: setLook('bullet'),
    [`${mod}Shift-n`]: setLook('text'),
    [`${mod}Alt-c`]: toggleCode,
    [`${mod}Shift--`]: insertRule,
    [`${mod}k`]: () => { actions.askLink(); return true; },
    // scorciatoie dell'editor del browser per formati che la nota non usa: non fanno niente
    [`${mod}Shift-=`]: () => true,
    [`${mod}=`]: () => true,
  };
}

// ------------------------------------------------------------------ lo stato per la barra

export function toolbarState(state) {
  const marks = {};
  for (const [name, type] of Object.entries(m)) {
    if (name !== 'link' && name !== 'colour') marks[name] = hasMark(state, type);
  }
  return {
    look: currentLook(state),
    marks,
    link: linkAt(state),
    inTable: isInTable(state),
    canUndo: undoDepth(state) > 0,
    canRedo: redoDepth(state) > 0,
    selection: !state.selection.empty,
    node: state.selection instanceof NodeSelection ? state.selection.node.type.name : null,
  };
}
