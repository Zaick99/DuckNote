// I blocchi di codice colorati come nella versione WPF (CodeLexer.cs): il linguaggio si
// indovina dal testo, poi parole chiave, testi, numeri, commenti e chiamate prendono il loro
// colore. Anche qui sono decorazioni: il testo del codice resta testo.
import { Plugin, PluginKey } from 'prosemirror-state';
import { Decoration, DecorationSet } from 'prosemirror-view';

const words = (text) => new Set(text.split(/\s+/).filter(Boolean));
const lower = (set) => new Set([...set].map((w) => w.toLowerCase()));

const languages = {
  csharp: {
    signs: ['using System', 'namespace ', 'public class', 'private ', 'void ', 'string ', '=> ', 'var '],
    keywords: words('abstract async await base bool break byte case catch char class const continue decimal default ' +
      'delegate do double else enum event explicit extern false finally fixed float for foreach get goto ' +
      'if implicit in int interface internal is lock long namespace new null object operator out override ' +
      'params private protected public readonly record ref return sealed set short sizeof static string ' +
      'struct switch this throw true try typeof uint ulong unchecked unsafe ushort using var virtual void ' +
      'volatile when where while yield'),
    line: ['//'], block: true,
  },
  python: {
    signs: ['def ', 'import ', 'elif ', 'self.', 'print(', '__init__', 'None', 'True'],
    keywords: words('and as assert async await break class continue def del elif else except False finally for from ' +
      'global if import in is lambda None nonlocal not or pass raise return self True try while with yield'),
    line: ['#'], triple: true,
  },
  javascript: {
    signs: ['function ', 'const ', 'let ', '=>', 'console.', 'require(', 'export ', 'null'],
    keywords: words('async await break case catch class const continue debugger default delete do else export extends ' +
      'false finally for from function if import in instanceof let new null of return static super switch ' +
      'this throw true try typeof undefined var void while with yield'),
    line: ['//'], block: true,
  },
  rust: {
    signs: ['fn ', 'let mut', 'impl ', 'pub ', '->', '::', '&str', 'match '],
    keywords: words('as async await break const continue crate dyn else enum extern false fn for if impl in let loop ' +
      'match mod move mut pub ref return self Self static struct super trait true type unsafe use where while'),
    line: ['//'], block: true,
  },
  powershell: {
    signs: ['$', 'Get-', 'Set-', 'param(', '-eq ', 'Write-', 'foreach (', '.ps1'],
    keywords: lower(words('begin break catch continue do dynamicparam else elseif end exit filter finally for foreach from ' +
      'function if in param process return switch throw trap try until while')),
    caseless: true, line: ['#'],
  },
  sql: {
    signs: ['select ', 'from ', 'where ', 'join ', 'insert into', 'update ', 'group by', 'order by'],
    keywords: lower(words('add all alter and as asc between by case cast create delete desc distinct drop else end exists ' +
      'from full group having in inner insert into is join left like limit not null on or order outer ' +
      'primary right select set table then union update values when where with')),
    caseless: true, line: ['--'], block: true,
  },
  shell: {
    signs: ['#!/bin/', 'echo ', 'fi\n', 'done\n', 'grep ', 'chmod ', 'sudo ', 'mkdir '],
    keywords: words('case do done elif else esac fi for function if in local read return select then until while'),
    line: ['#'],
  },
};

export function guessLanguage(code) {
  if (!code.trim()) return null;
  const lowered = code.toLowerCase();
  let best = null;
  let most = 0;
  for (const [name, lang] of Object.entries(languages)) {
    const score = lang.signs.filter((sign) => lowered.includes(sign.toLowerCase())).length;
    if (score > most) { best = name; most = score; }
  }
  return most >= 2 ? best : null;
}

const inWord = (c) => /[\p{L}\p{N}_$-]/u.test(c);
const isLetter = (c) => /[\p{L}_$@]/u.test(c);

// i pezzi di una riga: [{from, to, ink}] con ink keyword|quote|number|comment|call
function lexLine(line, lang, carry) {
  const spans = [];
  let at = 0;
  const push = (length, ink) => {
    length = Math.max(1, length);
    if (ink !== 'plain') spans.push({ from: at, to: at + length, ink });
    at += length;
  };
  const upto = (skip, shut, open) => {
    const end = line.indexOf(shut, Math.min(at + skip, line.length));
    carry.state = end < 0 ? open : null;
    return end < 0 ? line.length - at : end + shut.length - at;
  };
  while (at < line.length) {
    if (carry.state === 'comment') { push(upto(0, '*/', 'comment'), 'comment'); continue; }
    if (carry.state === 'quote') { push(upto(0, '"""', 'quote'), 'quote'); continue; }
    const rest = line.slice(at);
    if (lang?.block && rest.startsWith('/*')) { push(upto(2, '*/', 'comment'), 'comment'); continue; }
    if ((lang?.line ?? ['//']).some((mark) => rest.startsWith(mark))) { push(line.length - at, 'comment'); continue; }
    if (lang?.triple && rest.startsWith('"""')) { push(upto(3, '"""', 'quote'), 'quote'); continue; }
    const here = line[at];
    if (here === '"' || here === '\'') {
      let walk = at + 1;
      while (walk < line.length && line[walk] !== here) walk += line[walk] === '\\' && walk + 1 < line.length ? 2 : 1;
      push(Math.min(walk + 1, line.length) - at, 'quote');
      continue;
    }
    if (/[0-9]/.test(here) && !(at > 0 && inWord(line[at - 1]))) {
      let walk = at;
      while (walk < line.length && /[A-Za-z0-9._]/.test(line[walk])) walk++;
      push(walk - at, 'number');
      continue;
    }
    if (!isLetter(here)) { push(1, 'plain'); continue; }
    let walk = at;
    while (walk < line.length && inWord(line[walk])) walk++;
    const word = line.slice(at, walk);
    if (lang && lang.keywords.has(lang.caseless ? word.toLowerCase() : word)) { push(walk - at, 'keyword'); continue; }
    let after = walk;
    while (after < line.length && line[after] === ' ') after++;
    push(walk - at, after < line.length && line[after] === '(' ? 'call' : 'plain');
  }
  return spans;
}

export function lexCode(code) {
  const lang = languages[guessLanguage(code)] ?? null;
  const carry = { state: null };
  const spans = [];
  let offset = 0;
  for (const line of code.split('\n')) {
    for (const span of lexLine(line, lang, carry)) spans.push({ from: offset + span.from, to: offset + span.to, ink: span.ink });
    offset += line.length + 1;
  }
  return spans;
}

function decorateCode(node, pos) {
  return lexCode(node.textContent).map((span) =>
    Decoration.inline(pos + 1 + span.from, pos + 1 + span.to, { class: `dn-ink-${span.ink}` }));
}

function build(doc) {
  const decorations = [];
  doc.descendants((node, pos) => {
    if (node.type.name === 'code_block') {
      decorations.push(...decorateCode(node, pos));
      return false;
    }
    return !node.isTextblock;
  });
  return DecorationSet.create(doc, decorations);
}

const codeKey = new PluginKey('code');

export function codePlugin() {
  return new Plugin({
    key: codeKey,
    state: {
      init: (_, state) => build(state.doc),
      apply(tr, deco) {
        if (!tr.docChanged) return deco;
        let mapped = deco.map(tr.mapping, tr.doc);
        const blocks = new Map();
        tr.mapping.maps.forEach((map, i) => {
          map.forEach((oldStart, oldEnd, newStart, newEnd) => {
            const rest = tr.mapping.slice(i + 1);
            const from = Math.max(0, rest.map(newStart, -1));
            const to = Math.min(tr.doc.content.size, rest.map(newEnd, 1));
            tr.doc.nodesBetween(from, Math.max(from, to), (node, pos) => {
              if (node.type.name === 'code_block') { blocks.set(pos, node); return false; }
              return !node.isTextblock;
            });
          });
        });
        for (const [pos, node] of blocks) {
          mapped = mapped.remove(mapped.find(pos, pos + node.nodeSize));
          mapped = mapped.add(tr.doc, decorateCode(node, pos));
        }
        return mapped;
      },
    },
    props: { decorations: (state) => codeKey.getState(state) },
  });
}
