// Cerca e sostituisci nella pagina: le occorrenze evidenziate, quella corrente con una cornice
// d'accento, la barra in alto a destra sopra al testo (sta nella pagina web: niente della
// finestra puo' coprire il WebView).
import { Plugin, PluginKey, TextSelection } from 'prosemirror-state';
import { Decoration, DecorationSet } from 'prosemirror-view';

export const findKey = new PluginKey('find');

function matches(doc, query, caseSensitive) {
  const found = [];
  if (!query) return found;
  const needle = caseSensitive ? query : query.toLocaleLowerCase();
  doc.descendants((node, pos) => {
    if (!node.isTextblock) return true;
    let text = '';
    const at = [];
    node.forEach((child, offset) => {
      if (child.isText) {
        for (let i = 0; i < child.text.length; i++) at.push(pos + 1 + offset + i);
        text += child.text;
      } else {
        at.push(pos + 1 + offset);
        text += '\u0000';
      }
    });
    const hay = caseSensitive ? text : text.toLocaleLowerCase();
    let index = hay.indexOf(needle);
    while (index >= 0 && needle.length > 0) {
      found.push({ from: at[index], to: at[index + needle.length - 1] + 1 });
      index = hay.indexOf(needle, index + needle.length);
    }
    return false;
  });
  return found;
}

function decorate(doc, found, current) {
  return DecorationSet.create(doc, found.map((hit, i) =>
    Decoration.inline(hit.from, hit.to, { class: i === current ? 'dn-found dn-found-current' : 'dn-found' })));
}

export function findPlugin() {
  return new Plugin({
    key: findKey,
    state: {
      init: () => ({ query: '', caseSensitive: false, found: [], current: -1, deco: DecorationSet.empty }),
      apply(tr, prev, _old, state) {
        const meta = tr.getMeta(findKey);
        if (meta) {
          const next = { ...prev, ...meta };
          if ('query' in meta || 'caseSensitive' in meta || meta.refresh) {
            next.found = matches(state.doc, next.query, next.caseSensitive);
            if (!('current' in meta)) {
              // la prima occorrenza dopo il cursore
              const head = state.selection.from;
              const after = next.found.findIndex((hit) => hit.from >= head);
              next.current = next.found.length === 0 ? -1 : after >= 0 ? after : 0;
            }
          }
          next.current = next.found.length === 0 ? -1 : Math.max(0, Math.min(next.current, next.found.length - 1));
          next.deco = decorate(state.doc, next.found, next.current);
          return next;
        }
        if (tr.docChanged && prev.query) {
          const found = matches(state.doc, prev.query, prev.caseSensitive);
          const current = found.length === 0 ? -1 : Math.min(Math.max(prev.current, 0), found.length - 1);
          return { ...prev, found, current, deco: decorate(state.doc, found, current) };
        }
        return prev;
      },
    },
    props: { decorations: (state) => findKey.getState(state).deco },
  });
}

// La barra di ricerca: si apre con Ctrl/Cmd+F (o dalla barra della finestra), Invio va avanti,
// Maiusc+Invio indietro, Esc chiude e torna al testo.
export class FindBar {
  constructor(view, host) {
    this.view = view;
    this.root = document.createElement('div');
    this.root.className = 'dn-findbar';
    this.root.hidden = true;
    this.root.innerHTML = `
      <div class="dn-find-row">
        <span class="dn-find-icon" aria-hidden="true"></span>
        <input class="dn-find-input" type="search" placeholder="Cerca nella pagina" spellcheck="false" aria-label="Cerca nella pagina">
        <span class="dn-find-count" aria-live="polite"></span>
        <button class="dn-find-btn" data-act="prev" title="Precedente (Maiusc+Invio)" aria-label="Precedente">‹</button>
        <button class="dn-find-btn" data-act="next" title="Successiva (Invio)" aria-label="Successiva">›</button>
        <button class="dn-find-btn dn-find-toggle" data-act="case" title="Maiuscole e minuscole contano" aria-pressed="false">Aa</button>
        <button class="dn-find-btn dn-find-toggle" data-act="swap" title="Sostituisci" aria-pressed="false">⇄</button>
        <button class="dn-find-btn" data-act="close" title="Chiudi (Esc)" aria-label="Chiudi">✕</button>
      </div>
      <div class="dn-find-row dn-swap-row" hidden>
        <input class="dn-swap-input" type="text" placeholder="Sostituisci con" spellcheck="false" aria-label="Sostituisci con">
        <button class="dn-find-text" data-act="one">Sostituisci</button>
        <button class="dn-find-text" data-act="all">Tutte</button>
      </div>`;
    host.appendChild(this.root);
    this.input = this.root.querySelector('.dn-find-input');
    this.swap = this.root.querySelector('.dn-swap-input');
    this.count = this.root.querySelector('.dn-find-count');
    this.caseBtn = this.root.querySelector('[data-act="case"]');
    this.swapBtn = this.root.querySelector('[data-act="swap"]');
    this.swapRow = this.root.querySelector('.dn-swap-row');

    this.input.addEventListener('input', () => this.search());
    this.input.addEventListener('keydown', (e) => this.key(e));
    this.swap.addEventListener('keydown', (e) => this.key(e));
    this.root.addEventListener('mousedown', (e) => { if (e.target.closest('button')) e.preventDefault(); });
    this.root.addEventListener('click', (e) => {
      const act = e.target.closest('button')?.dataset.act;
      if (act === 'prev') this.hop(-1);
      else if (act === 'next') this.hop(1);
      else if (act === 'case') { this.toggle(this.caseBtn); this.search(); }
      else if (act === 'swap') { this.toggle(this.swapBtn); this.swapRow.hidden = !this.swapRow.hidden; if (!this.swapRow.hidden) this.swap.focus(); }
      else if (act === 'close') this.close();
      else if (act === 'one') this.replaceOne();
      else if (act === 'all') this.replaceAll();
    });
  }

  get isOpen() { return !this.root.hidden; }

  toggle(button) {
    const on = button.getAttribute('aria-pressed') !== 'true';
    button.setAttribute('aria-pressed', on ? 'true' : 'false');
  }

  open(seed) {
    this.root.hidden = false;
    this.root.classList.remove('dn-leaving');
    this.root.classList.add('dn-entering');
    if (seed && seed.length <= 120 && !seed.includes('\n')) this.input.value = seed;
    this.input.focus();
    this.input.select();
    this.search();
  }

  close() {
    if (this.root.hidden) return;
    this.root.classList.remove('dn-entering');
    this.root.classList.add('dn-leaving');
    setTimeout(() => { if (this.root.classList.contains('dn-leaving')) this.root.hidden = true; }, 160);
    this.view.dispatch(this.view.state.tr.setMeta(findKey, { query: '' }));
    this.view.focus();
  }

  key(e) {
    if (e.key === 'Escape') { e.preventDefault(); this.close(); }
    else if (e.key === 'Enter' && e.target === this.input) { e.preventDefault(); this.hop(e.shiftKey ? -1 : 1); }
    else if (e.key === 'Enter' && e.target === this.swap) { e.preventDefault(); this.replaceOne(); }
    else if (e.key === 'F3') { e.preventDefault(); this.hop(e.shiftKey ? -1 : 1); }
  }

  search() {
    const caseSensitive = this.caseBtn.getAttribute('aria-pressed') === 'true';
    this.view.dispatch(this.view.state.tr.setMeta(findKey, { query: this.input.value, caseSensitive }));
    this.reveal();
  }

  hop(step) {
    const state = findKey.getState(this.view.state);
    if (state.found.length === 0) { this.tally(); return; }
    const current = (state.current + step + state.found.length) % state.found.length;
    this.view.dispatch(this.view.state.tr.setMeta(findKey, { current }));
    this.reveal();
  }

  // la parola trovata si vede: selezionata, e la pagina scorre per mostrarla a un terzo dell'altezza
  reveal() {
    const state = findKey.getState(this.view.state);
    this.tally();
    if (state.current < 0) return;
    const hit = state.found[state.current];
    const coords = this.view.coordsAtPos(hit.from);
    const scroller = document.scrollingElement;
    const wanted = scroller.scrollTop + coords.top - window.innerHeight / 3;
    if (coords.top < 60 || coords.bottom > window.innerHeight - 40) scroller.scrollTo({ top: Math.max(0, wanted), behavior: 'smooth' });
    this.view.dispatch(this.view.state.tr.setSelection(TextSelection.create(this.view.state.doc, hit.from, hit.to)));
  }

  tally() {
    const state = findKey.getState(this.view.state);
    this.count.textContent = !this.input.value ? '' : state.found.length === 0 ? '0' : `${state.current + 1}/${state.found.length}`;
    this.root.classList.toggle('dn-find-none', !!this.input.value && state.found.length === 0);
  }

  replaceOne() {
    const state = findKey.getState(this.view.state);
    if (state.current < 0) return;
    const hit = state.found[state.current];
    const tr = this.view.state.tr;
    if (this.swap.value) tr.insertText(this.swap.value, hit.from, hit.to);
    else tr.delete(hit.from, hit.to);
    this.view.dispatch(tr);
    this.view.dispatch(this.view.state.tr.setMeta(findKey, { refresh: true, current: state.current }));
    this.reveal();
  }

  replaceAll() {
    const state = findKey.getState(this.view.state);
    if (state.found.length === 0) return 0;
    const tr = this.view.state.tr;
    for (const hit of [...state.found].reverse()) {
      if (this.swap.value) tr.insertText(this.swap.value, hit.from, hit.to);
      else tr.delete(hit.from, hit.to);
    }
    this.view.dispatch(tr);
    this.view.dispatch(this.view.state.tr.setMeta(findKey, { refresh: true }));
    this.tally();
    return state.found.length;
  }
}
