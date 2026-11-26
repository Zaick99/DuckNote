// I riquadri nel testo: immagini, grafici e disegni, con il testo che gli gira attorno.
// Nel documento c'e' solo il segnaposto (id, tipo, misura); i byte li tiene la nota e
// arrivano dal C# con dn.insetData(). I bordi si trascinano per cambiare la misura; il grafico
// si modifica dalla finestra di DuckNote, il disegno qui, a mano libera.
import { NodeSelection } from 'prosemirror-state';

const data = new Map();
const views = new Set();

export function setInsetData(id, value) {
  data.set(id, value);
  for (const view of views) if (view.node.attrs.id === id) view.render();
}

export function forgetInsets() {
  data.clear();
}

const titles = { picture: 'Immagine', chart: 'Grafico', drawing: 'Disegno' };
const least = { width: 120, height: 90 };
const most = { width: 1600, height: 1200 };

// ------------------------------------------------------------------ grafici (come ChartSpec.cs)

export function readChart(text) {
  let shape = 'bars';
  let title = '';
  const points = [];
  for (const raw of (text ?? '').replace(/\r\n?/g, '\n').split('\n')) {
    const line = raw.trim();
    if (!line) continue;
    const colon = line.indexOf(':');
    const field = colon > 0 ? line.slice(0, colon).trim().toLowerCase() : '';
    if (field === 'tipo') {
      const kind = line.slice(colon + 1).trim().toLowerCase();
      shape = kind === 'linea' || kind === 'linee' ? 'line' : kind === 'torta' ? 'pie' : 'bars';
      continue;
    }
    if (field === 'titolo') { title = line.slice(colon + 1).trim(); continue; }
    const gap = line.lastIndexOf(' ');
    const value = gap > 0 ? Number(line.slice(gap + 1).replace(',', '.')) : NaN;
    if (gap > 0 && Number.isFinite(value)) points.push({ label: line.slice(0, gap).trim(), value });
  }
  return { shape, title, points };
}

const series = ['Blue', 'Green', 'Orange', 'Purple', 'Teal', 'Pink', 'Yellow'];
const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

export function chartSvg(text, width, height) {
  const spec = readChart(text);
  const w = Math.max(80, width);
  const h = Math.max(60, height);
  const top = spec.title ? 30 : 12;
  const bottom = 26;
  const left = 14;
  const right = 14;
  const parts = [];
  if (spec.title) parts.push(`<text x="${w / 2}" y="19" class="dn-chart-title" text-anchor="middle">${esc(spec.title)}</text>`);
  const points = spec.points;
  if (points.length === 0) {
    parts.push(`<text x="${w / 2}" y="${h / 2}" class="dn-chart-empty" text-anchor="middle">Nessun valore: modifica il grafico</text>`);
  } else if (spec.shape === 'pie') {
    const total = points.reduce((sum, p) => sum + Math.max(0, p.value), 0) || 1;
    const r = Math.max(10, Math.min((w - 140) / 2, (h - top - 10) / 2));
    const cx = left + r + 4;
    const cy = top + (h - top) / 2 - 4;
    let angle = -Math.PI / 2;
    points.forEach((p, i) => {
      const slice = (Math.max(0, p.value) / total) * Math.PI * 2;
      const x1 = cx + r * Math.cos(angle);
      const y1 = cy + r * Math.sin(angle);
      const x2 = cx + r * Math.cos(angle + slice);
      const y2 = cy + r * Math.sin(angle + slice);
      const large = slice > Math.PI ? 1 : 0;
      const shape = slice >= Math.PI * 2 - 0.001
        ? `<circle cx="${cx}" cy="${cy}" r="${r}" fill="var(--c-${series[i % series.length]})"/>`
        : `<path d="M${cx},${cy} L${x1},${y1} A${r},${r} 0 ${large} 1 ${x2},${y2} Z" fill="var(--c-${series[i % series.length]})"/>`;
      parts.push(shape);
      angle += slice;
      const ly = top + 10 + i * 18;
      parts.push(`<rect x="${cx + r + 18}" y="${ly - 9}" width="10" height="10" rx="2" fill="var(--c-${series[i % series.length]})"/>` +
        `<text x="${cx + r + 34}" y="${ly}" class="dn-chart-label">${esc(p.label)} ${esc(p.value)}</text>`);
    });
  } else {
    const tallest = Math.max(...points.map((p) => p.value), 0) || 1;
    const plotW = w - left - right;
    const plotH = h - top - bottom;
    const step = plotW / points.length;
    parts.push(`<line x1="${left}" y1="${top + plotH}" x2="${w - right}" y2="${top + plotH}" class="dn-chart-axis"/>`);
    if (spec.shape === 'line') {
      const xy = points.map((p, i) => [left + step * (i + 0.5), top + plotH - (Math.max(0, p.value) / tallest) * (plotH - 14)]);
      parts.push(`<polyline points="${xy.map(([x, y]) => `${x},${y}`).join(' ')}" fill="none" stroke="var(--c-Blue)" stroke-width="2.2" stroke-linejoin="round"/>`);
      xy.forEach(([x, y], i) => parts.push(`<circle cx="${x}" cy="${y}" r="3.5" fill="var(--c-Blue)"/><text x="${x}" y="${y - 8}" class="dn-chart-value" text-anchor="middle">${esc(points[i].value)}</text>`));
    } else {
      points.forEach((p, i) => {
        const bh = (Math.max(0, p.value) / tallest) * (plotH - 14);
        const bw = Math.max(6, step * 0.62);
        const x = left + step * i + (step - bw) / 2;
        parts.push(`<rect x="${x}" y="${top + plotH - bh}" width="${bw}" height="${bh}" rx="3" fill="var(--c-${series[i % series.length]})"/>` +
          `<text x="${x + bw / 2}" y="${top + plotH - bh - 4}" class="dn-chart-value" text-anchor="middle">${esc(p.value)}</text>`);
      });
    }
    points.forEach((p, i) => parts.push(`<text x="${left + step * (i + 0.5)}" y="${h - 8}" class="dn-chart-label" text-anchor="middle">${esc(p.label)}</text>`));
  }
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${w} ${h}" width="${w}" height="${h}">${parts.join('')}</svg>`;
}

// ------------------------------------------------------------------ disegni

// il disegno di DuckNote multipiattaforma: tratti in JSON. Quello della versione WPF (ISF) qui
// non si sa leggere: resta nella nota, intatto, e si vede nella versione Windows.
export function readDrawing(text) {
  try {
    const parsed = JSON.parse(text);
    if (parsed && Array.isArray(parsed.strokes)) return parsed;
  } catch { /* non e' un disegno di questa versione */ }
  return null;
}

function strokePath(points) {
  if (points.length < 2) return '';
  let d = `M${points[0]},${points[1]}`;
  for (let i = 2; i < points.length; i += 2) d += ` L${points[i]},${points[i + 1]}`;
  return d;
}

// ------------------------------------------------------------------ la cornice

export class InsetView {
  constructor(node, view, getPos, send) {
    this.node = node;
    this.view = view;
    this.getPos = getPos;
    this.send = send;
    this.pen = 'Label';
    this.dom = document.createElement('span');
    this.dom.className = 'dn-inset';
    this.dom.contentEditable = 'false';
    this.dom.innerHTML = `
      <span class="dn-inset-bar">
        <span class="dn-inset-title"></span>
        <span class="dn-inset-tools"></span>
      </span>
      <span class="dn-inset-body"></span>
      <span class="dn-inset-grip dn-grip-e" data-grip="e" title="Trascina per allargare"></span>
      <span class="dn-inset-grip dn-grip-s" data-grip="s" title="Trascina per allungare"></span>
      <span class="dn-inset-grip dn-grip-se" data-grip="se" title="Trascina per cambiare la misura"></span>`;
    this.body = this.dom.querySelector('.dn-inset-body');
    this.tools = this.dom.querySelector('.dn-inset-tools');
    this.dom.addEventListener('pointerdown', (e) => this.pointerDown(e));
    this.dom.addEventListener('click', (e) => this.click(e));
    this.dom.addEventListener('dblclick', (e) => { if (this.node.attrs.kind === 'chart') { e.preventDefault(); this.send({ t: 'insetEdit', id: this.node.attrs.id }); } });
    views.add(this);
    this.layout();
    this.render();
  }

  layout() {
    this.dom.style.width = `${this.node.attrs.width}px`;
    this.dom.style.height = `${this.node.attrs.height}px`;
    this.dom.dataset.kind = this.node.attrs.kind;
    this.dom.querySelector('.dn-inset-title').textContent = titles[this.node.attrs.kind] ?? 'Riquadro';
  }

  render() {
    const kind = this.node.attrs.kind;
    const value = data.get(this.node.attrs.id);
    const buttons = [];
    if (kind === 'chart') buttons.push('<button data-act="edit" title="Modifica il grafico">Modifica</button>');
    if (kind === 'drawing' && (!value || readDrawing(value.text ?? '') || !value.text)) {
      buttons.push(`<button data-act="draw" class="${this.drawing ? 'dn-on' : ''}" title="Disegna a mano libera">${this.drawing ? 'Fatto' : 'Disegna'}</button>`);
      if (this.drawing) {
        for (const ink of ['Label', 'Red', 'Blue', 'Green', 'Orange']) {
          buttons.push(`<button data-act="ink" data-ink="${ink}" class="dn-ink ${this.pen === ink ? 'dn-on' : ''}" style="--ink: var(--c-${ink})" title="Colore"></button>`);
        }
        buttons.push('<button data-act="wipe" title="Cancella il disegno">Pulisci</button>');
      }
    }
    buttons.push('<button data-act="drop" class="dn-inset-drop" title="Togli dalla nota (Ctrl+Z lo rimette)">✕</button>');
    this.tools.innerHTML = buttons.join('');

    if (!value) {
      this.body.innerHTML = `<span class="dn-inset-note">${kind === 'drawing' ? 'Disegno vuoto: premi Disegna' : 'Caricamento…'}</span>`;
      return;
    }
    if (kind === 'picture') {
      this.body.innerHTML = `<img alt="Immagine" draggable="false" src="${value.src}">`;
    } else if (kind === 'chart') {
      this.body.innerHTML = chartSvg(value.text, this.node.attrs.width, this.node.attrs.height - 26);
    } else {
      const drawing = value.text ? readDrawing(value.text) : { strokes: [] };
      if (!drawing) {
        this.body.innerHTML = '<span class="dn-inset-note">Disegno fatto con la versione per Windows: si vede e si modifica li\'. Qui resta com\'e\', intatto.</span>';
        return;
      }
      this.strokes = drawing.strokes;
      this.drawSvg();
    }
  }

  drawSvg() {
    const w = this.node.attrs.width;
    const h = this.node.attrs.height - 26;
    const paths = (this.strokes ?? []).map((s) =>
      `<path d="${strokePath(s.p)}" fill="none" stroke="var(--c-${s.c || 'Label'})" stroke-width="${s.w || 2}" stroke-linecap="round" stroke-linejoin="round"/>`);
    this.body.innerHTML = `<svg class="dn-drawing ${this.drawing ? 'dn-drawing-live' : ''}" viewBox="0 0 ${w} ${h}" width="${w}" height="${h}">${paths.join('')}</svg>`;
  }

  saveDrawing() {
    const text = JSON.stringify({ v: 1, strokes: this.strokes ?? [] });
    data.set(this.node.attrs.id, { text });
    this.send({ t: 'insetBytes', id: this.node.attrs.id, text });
  }

  click(e) {
    const button = e.target.closest('button');
    if (!button) {
      // un clic sul riquadro lo seleziona: Canc lo toglie, le frecce lo scavalcano
      if (!this.drawing) this.view.dispatch(this.view.state.tr.setSelection(NodeSelection.create(this.view.state.doc, this.getPos())));
      return;
    }
    e.preventDefault();
    const act = button.dataset.act;
    if (act === 'edit') this.send({ t: 'insetEdit', id: this.node.attrs.id });
    else if (act === 'drop') {
      const pos = this.getPos();
      this.view.dispatch(this.view.state.tr.delete(pos, pos + this.node.nodeSize));
      this.view.focus();
    } else if (act === 'draw') {
      this.drawing = !this.drawing;
      this.dom.classList.toggle('dn-inset-live', this.drawing);
      this.render();
    } else if (act === 'ink') {
      this.pen = button.dataset.ink;
      this.render();
    } else if (act === 'wipe') {
      this.strokes = [];
      this.drawSvg();
      this.saveDrawing();
    }
  }

  pointerDown(e) {
    const grip = e.target.closest('[data-grip]')?.dataset.grip;
    if (grip) { this.resize(e, grip); return; }
    if (this.drawing && e.target.closest('.dn-drawing')) this.stroke(e);
  }

  resize(e, grip) {
    e.preventDefault();
    const startX = e.clientX;
    const startY = e.clientY;
    const { width, height } = this.node.attrs;
    const column = this.view.dom.clientWidth - 60;
    let w = width;
    let h = height;
    const move = (ev) => {
      if (grip.includes('e')) w = Math.round(Math.min(Math.max(least.width, width + ev.clientX - startX), Math.min(most.width, column)));
      if (grip.includes('s')) h = Math.round(Math.min(Math.max(least.height, height + ev.clientY - startY), most.height));
      this.dom.style.width = `${w}px`;
      this.dom.style.height = `${h}px`;
    };
    const up = () => {
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
      if (w !== width || h !== height) {
        this.view.dispatch(this.view.state.tr.setNodeMarkup(this.getPos(), null, { ...this.node.attrs, width: w, height: h }));
      }
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
  }

  stroke(e) {
    e.preventDefault();
    const svg = this.body.querySelector('svg');
    const box = svg.getBoundingClientRect();
    const scale = this.node.attrs.width / box.width;
    const point = (ev) => [Math.round((ev.clientX - box.left) * scale * 10) / 10, Math.round((ev.clientY - box.top) * scale * 10) / 10];
    const current = { c: this.pen, w: 2.2, p: point(e) };
    this.strokes = [...(this.strokes ?? []), current];
    const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    path.setAttribute('fill', 'none');
    path.setAttribute('stroke', `var(--c-${current.c})`);
    path.setAttribute('stroke-width', current.w);
    path.setAttribute('stroke-linecap', 'round');
    path.setAttribute('stroke-linejoin', 'round');
    svg.appendChild(path);
    const move = (ev) => {
      current.p.push(...point(ev));
      path.setAttribute('d', strokePath(current.p));
    };
    const up = () => {
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
      if (current.p.length < 4) current.p.push(current.p[0] + 0.1, current.p[1] + 0.1);
      this.saveDrawing();
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
  }

  update(node) {
    if (node.type !== this.node.type || node.attrs.id !== this.node.attrs.id) return false;
    const resized = node.attrs.width !== this.node.attrs.width || node.attrs.height !== this.node.attrs.height;
    this.node = node;
    this.layout();
    if (resized) this.render();
    return true;
  }

  selectNode() { this.dom.classList.add('dn-inset-selected'); }

  deselectNode() { this.dom.classList.remove('dn-inset-selected'); }

  stopEvent(e) {
    // dentro la cornice comandano i suoi pulsanti, le maniglie e il disegno, non l'editor
    return !!e.target.closest?.('button, [data-grip]') || (this.drawing && e.type.startsWith('pointer')) || (this.drawing && e.type.startsWith('mouse'));
  }

  ignoreMutation() { return true; }

  destroy() { views.delete(this); }
}
