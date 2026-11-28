// Mouse, trackpad e penna sulla mappa. Un gesto per volta: spostare (apparati e zone), tirare
// un cavo, allargare una zona, selezionare col rettangolo, scorrere la vista. Sul vuoto si
// trascina la vista; con Maiuscole si seleziona col rettangolo.

import { GRID, centerOf, rectOf, sizeOf, membersOf, inside, portCell, linkAtPort, freePort, portLabel } from './model.js';
import { intersects, normalizeRect } from './geometry.js';
import { isMac, modKey } from '../common/dom.js';
import { stop } from '../common/motion.js';

const SNAP_GUIDE = 6;
const CLICK = 3;
/** Quanto si tiene premuta una presa perche' il gesto diventi "collega da questa porta". */
const HOLD = 380;

const snap = (value) => Math.round(value / GRID) * GRID;

export class Pointer {
  /**
   * ui: { mode(): 'move'|'connect', focusSink(), closePopups(), kindPicker(x, y, choose),
   *       contextMenu(x, y, hit), inspect(field), changedView(), hint(text, tone) }
   */
  constructor(scene, store, ui) {
    this.scene = scene;
    this.store = store;
    this.ui = ui;
    this.drag = null;
    this.space = false;
    this.pinch = null;

    const el = scene.host;
    el.addEventListener('pointerdown', (e) => this.down(e));
    el.addEventListener('pointermove', (e) => this.move(e));
    el.addEventListener('pointerup', (e) => this.up(e));
    el.addEventListener('pointercancel', () => this.cancel());
    el.addEventListener('lostpointercapture', () => this.cancel(false));
    el.addEventListener('dblclick', (e) => this.double(e));
    el.addEventListener('wheel', (e) => this.wheel(e), { passive: false });
    el.addEventListener('contextmenu', (e) => this.context(e));
    // il pizzico del trackpad nel WebKit del Mac arriva come gesto, non come rotella
    el.addEventListener('gesturestart', (e) => {
      e.preventDefault();
      this.pinch = this.scene.view.k;
    });
    el.addEventListener('gesturechange', (e) => {
      e.preventDefault();
      if (this.pinch == null) return;
      stop(this.scene, 'view');
      this.scene.zoomAt(e.clientX, e.clientY, (this.pinch * e.scale) / this.scene.view.k);
      this.ui.changedView();
    });
    el.addEventListener('gestureend', (e) => {
      e.preventDefault();
      this.pinch = null;
    });
  }

  get busy() {
    return this.drag != null;
  }

  hit(event) {
    return this.hitOn(event.target instanceof Element ? event.target : null);
  }

  /**
   * Cosa c'e' sotto un punto dello schermo. Il doppio clic e il destro arrivano all'elemento che
   * ha catturato il puntatore (la tela), non a quello sotto: si guarda dove si e' cliccato.
   */
  hitAt(clientX, clientY) {
    return this.hitOn(document.elementFromPoint(clientX, clientY));
  }

  hitOn(target) {
    const item = target?.closest('[data-type]');
    if (!item || !this.scene.host.contains(item)) return { type: 'empty' };
    return {
      type: item.dataset.type,
      id: item.dataset.id,
      part: target.closest('[data-part]')?.dataset.part ?? null,
      corner: target.closest('[data-corner]')?.dataset.corner ?? null,
    };
  }

  /** Gli apparati e le zone che si muovono insieme: i selezionati, e chi sta nelle zone scelte. */
  moving() {
    const ids = new Set();
    for (const id of this.store.sel) if (['node', 'zone'].includes(this.store.typeOf(id))) ids.add(id);
    for (const zone of this.store.selected('zone')) for (const node of membersOf(this.store.map, zone)) ids.add(node.id);
    const items = new Map();
    for (const id of ids) {
      const item = this.store.find(id);
      items.set(id, { x: item.x, y: item.y });
    }
    return items;
  }

  down(event) {
    this.ui.closePopups();
    if (event.button === 2) return;
    this.ui.focusSink();
    stop(this.scene, 'view');
    const world = this.scene.toWorld(event.clientX, event.clientY);
    const hit = this.hit(event);
    const additive = event.shiftKey || modKey(event);

    if (event.button === 1 || this.space || (hit.type === 'empty' && !event.shiftKey)) {
      this.start(event, { type: 'pan', x: event.clientX, y: event.clientY, view: { ...this.scene.view }, moved: false, clear: hit.type === 'empty' && event.button === 0 && !this.space });
      return;
    }
    if (event.button !== 0) return;

    if (hit.type === 'empty') {
      this.start(event, { type: 'marquee', from: world, base: new Set(this.store.sel), moved: false });
      return;
    }

    // una presa del pannello: tenuta premuta, il gesto diventa "collega da questa porta" (o, se la
    // porta ha gia' il suo cavo, "sposta quel cavo"); trascinata subito, l'apparato si sposta
    const jackEl = hit.type === 'node' && !this.store.readonly && this.ui.mode() !== 'connect' ? event.target.closest?.('.dm-jack') : null;
    const jack = jackEl ? { port: { kind: jackEl.dataset.kind, n: Number(jackEl.dataset.port) } } : null;
    if (jack) jack.existing = linkAtPort(this.store.map, hit.id, jack.port.kind, jack.port.n);

    if (hit.type === 'node' && (hit.part === 'handle' || this.ui.mode() === 'connect')) {
      if (this.store.readonly) return;
      this.start(event, { type: 'connect', from: hit.id, at: world, moved: false });
      return;
    }

    if (hit.type === 'zone' && hit.part === 'grip' && !this.store.readonly) {
      if (!this.store.sel.has(hit.id)) this.store.select([hit.id]);
      const zone = this.store.find(hit.id);
      this.start(event, { type: 'resize', id: hit.id, corner: hit.corner, from: world, rect: { x: zone.x, y: zone.y, w: zone.w, h: zone.h }, moved: false });
      return;
    }

    // un clic su un oggetto: selezione (Maiuscole o Cmd aggiungono e tolgono)
    const wasSelected = this.store.sel.has(hit.id);
    if (additive) this.store.toggle(hit.id);
    else if (!wasSelected) this.store.select([hit.id]);

    // in sola lettura un apparato non si sposta: trascinarlo sposta la vista
    if (this.store.readonly && !additive) {
      this.start(event, { type: 'pan', x: event.clientX, y: event.clientY, view: { ...this.scene.view }, moved: false, clear: false });
      return;
    }
    if (hit.type === 'link' || this.store.readonly || !this.store.sel.has(hit.id)) {
      this.start(event, { type: 'none' });
      return;
    }
    this.start(event, { type: 'move', from: world, items: this.moving(), primary: hit.id, moved: false, collapse: wasSelected && !additive, jack, node: hit.id });
    if (jack && !additive) this.hold(event.pointerId);
  }

  /** La presa tenuta premuta senza muoversi: da spostare l'apparato si passa a tirare un cavo da lei. */
  hold(pointer) {
    clearTimeout(this.holdTimer);
    this.holdTimer = setTimeout(() => {
      const drag = this.drag;
      if (!drag || drag.type !== 'move' || drag.moved || drag.pointer !== pointer || !drag.jack) return;
      this.drag = { type: 'plug', from: drag.node, port: drag.jack.port, existing: drag.jack.existing, at: drag.from, moved: false, held: true, pointer };
      this.scene.host.classList.add('plugging');
      this.store.select([drag.node]);
      this.mark(null);
      this.jackEl(drag.node, drag.jack.port)?.classList.add('plug-from');
    }, HOLD);
  }

  start(event, drag) {
    this.drag = { ...drag, pointer: event.pointerId };
    try {
      this.scene.host.setPointerCapture(event.pointerId);
    } catch { /* un puntatore gia' andato */ }
  }

  move(event) {
    const drag = this.drag;
    if (!drag || drag.pointer !== event.pointerId) return;
    const world = this.scene.toWorld(event.clientX, event.clientY);
    const k = this.scene.view.k;

    switch (drag.type) {
      case 'pan': {
        const dx = event.clientX - drag.x;
        const dy = event.clientY - drag.y;
        if (!drag.moved && Math.hypot(dx, dy) < CLICK) return;
        drag.moved = true;
        this.scene.host.classList.add('panning');
        this.scene.setView(drag.view.x + dx, drag.view.y + dy, drag.view.k);
        break;
      }
      case 'move': {
        let dx = world.x - drag.from.x;
        let dy = world.y - drag.from.y;
        if (!drag.moved && Math.hypot(dx, dy) * k < CLICK) return;
        if (!drag.moved) {
          clearTimeout(this.holdTimer);
          drag.moved = true;
          this.store.begin();
          this.scene.host.classList.add('dragging');
          this.scene.quick = true;
        }
        const primary = drag.items.get(drag.primary) ?? drag.items.values().next().value;
        const guides = [];
        if (!event.altKey && primary) {
          dx = snap(primary.x + dx) - primary.x;
          dy = snap(primary.y + dy) - primary.y;
          const aligned = this.align(drag, primary, dx, dy, k);
          dx = aligned.dx;
          dy = aligned.dy;
          guides.push(...aligned.guides);
        }
        const positions = new Map();
        for (const [id, from] of drag.items) positions.set(id, { x: from.x + dx, y: from.y + dy });
        this.store.moveTo(positions);
        this.scene.guides(guides);
        break;
      }
      case 'connect': {
        if (!drag.moved && Math.hypot(world.x - drag.at.x, world.y - drag.at.y) * k < CLICK) return;
        drag.moved = true;
        const source = this.store.node(drag.from);
        const over = this.nodeAt(event.clientX, event.clientY);
        const target = over && over !== drag.from ? over : null;
        this.scene.setTarget(target);
        const end = target ? centerOf(this.store.node(target)) : world;
        this.scene.wire(edgeToward(source, end), end);
        break;
      }
      case 'plug': {
        if (!drag.moved && Math.hypot(world.x - drag.at.x, world.y - drag.at.y) * k < CLICK) return;
        drag.moved = true;
        const aim = this.aim(drag, event.clientX, event.clientY);
        this.scene.setTarget(aim.node);
        this.mark(aim.jack, aim.busy ? 'plug-busy' : 'plug-ok');
        const start = this.jackPoint(drag.from, drag.port) ?? centerOf(this.store.node(drag.from));
        const end = aim.port ? this.jackPoint(aim.node, aim.port) : aim.node ? centerOf(this.store.node(aim.node)) : world;
        this.scene.wire(start, end);
        break;
      }
      case 'marquee': {
        if (!drag.moved && Math.hypot(world.x - drag.from.x, world.y - drag.from.y) * k < CLICK) return;
        drag.moved = true;
        const rect = normalizeRect(drag.from.x, drag.from.y, world.x, world.y);
        this.scene.marquee(rect);
        const ids = new Set(drag.base);
        for (const node of this.store.map.nodes) if (intersects(rect, rectOf(node))) ids.add(node.id);
        for (const zone of this.store.map.zones) {
          if (rect.x <= zone.x && rect.y <= zone.y && rect.x + rect.w >= zone.x + zone.w && rect.y + rect.h >= zone.y + zone.h) ids.add(zone.id);
        }
        this.store.select([...ids]);
        break;
      }
      case 'resize': {
        const dx = world.x - drag.from.x;
        const dy = world.y - drag.from.y;
        if (!drag.moved && Math.hypot(dx, dy) * k < CLICK) return;
        if (!drag.moved) {
          drag.moved = true;
          this.store.begin();
          this.scene.quick = true;
        }
        const r = drag.rect;
        let x0 = r.x;
        let y0 = r.y;
        let x1 = r.x + r.w;
        let y1 = r.y + r.h;
        if (drag.corner.includes('w')) x0 = Math.min(x1 - 160, snap(r.x + dx));
        if (drag.corner.includes('e')) x1 = Math.max(x0 + 160, snap(r.x + r.w + dx));
        if (drag.corner.includes('n')) y0 = Math.min(y1 - 96, snap(r.y + dy));
        if (drag.corner.includes('s')) y1 = Math.max(y0 + 96, snap(r.y + r.h + dy));
        const zone = this.store.find(drag.id);
        Object.assign(zone, { x: x0, y: y0, w: x1 - x0, h: y1 - y0 });
        this.store.emit('map');
        break;
      }
      default:
        break;
    }
  }

  /**
   * Le guide: il centro (o un bordo) dell'apparato trascinato che si allinea a quello di un
   * altro apparato fermo, entro 6 pixel dello schermo, ci si aggancia e la linea si vede.
   */
  align(drag, primary, dx, dy, k) {
    const threshold = SNAP_GUIDE / k;
    const size = sizeOf(primary);
    const cx = primary.x + dx + size.w / 2;
    const cy = primary.y + dy + size.h / 2;
    let bestX = null;
    let bestY = null;
    for (const node of this.store.map.nodes) {
      if (drag.items.has(node.id)) continue;
      const other = sizeOf(node);
      const ox = node.x + other.w / 2;
      const oy = node.y + other.h / 2;
      if (Math.abs(ox - cx) < threshold && (!bestX || Math.abs(ox - cx) < Math.abs(bestX.ox - cx))) bestX = { ox, node };
      if (Math.abs(oy - cy) < threshold && (!bestY || Math.abs(oy - cy) < Math.abs(bestY.oy - cy))) bestY = { oy, node };
    }
    const guides = [];
    if (bestX) {
      dx += bestX.ox - cx;
      const ys = [bestX.node.y, bestX.node.y + sizeOf(bestX.node).h, primary.y + dy, primary.y + dy + size.h];
      guides.push({ x1: bestX.ox, x2: bestX.ox, y1: Math.min(...ys) - 12, y2: Math.max(...ys) + 12 });
    }
    if (bestY) {
      dy += bestY.oy - cy;
      const xs = [bestY.node.x, bestY.node.x + sizeOf(bestY.node).w, primary.x + dx, primary.x + dx + size.w];
      guides.push({ y1: bestY.oy, y2: bestY.oy, x1: Math.min(...xs) - 12, x2: Math.max(...xs) + 12 });
    }
    return { dx, dy, guides };
  }

  /** La presa di un apparato sulla tela (l'elemento), per illuminarla durante il gesto. */
  jackEl(nodeId, port) {
    return this.scene.host.querySelector(`[data-type="node"][data-id="${nodeId}"] .dm-jack[data-kind="${port.kind}"][data-port="${port.n}"]`);
  }

  /** Il centro di una presa in coordinate della mappa. */
  jackPoint(nodeId, port) {
    const node = this.store.node(nodeId);
    const cell = node ? portCell(node, port.n, port.kind) : null;
    return cell ? { x: node.x + cell.cx, y: node.y + cell.cy } : null;
  }

  /** Una sola presa illuminata per volta per ogni ruolo (partenza, arrivo libero, arrivo occupato). */
  mark(el, role) {
    for (const old of this.scene.host.querySelectorAll('.plug-ok, .plug-busy')) {
      if (old !== el || !old.classList.contains(role)) old.classList.remove('plug-ok', 'plug-busy');
    }
    if (el) el.classList.add(role);
  }

  /**
   * Dove andrebbe il cavo se si lasciasse qui: l'apparato sotto il puntatore (non quello di
   * partenza), la sua presa se si e' sopra una presa, e se quella presa e' gia' occupata da un
   * altro cavo.
   */
  aim(drag, clientX, clientY) {
    const under = document.elementFromPoint(clientX, clientY);
    const nodeEl = under?.closest?.('[data-type="node"]');
    const node = nodeEl && this.scene.host.contains(nodeEl) ? nodeEl.dataset.id : null;
    const other = drag.existing ? drag.existing.link[drag.existing.end === 'a' ? 'b' : 'a'] : null;
    // un cavo nuovo va verso un altro apparato; uno gia' collegato puo' cambiare porta anche sullo stesso
    if (!node || (node === drag.from && !drag.existing) || node === other) return { node: null, jack: null, port: null, busy: false };
    const jack = under.closest('.dm-jack');
    if (!jack || !nodeEl.contains(jack)) return { node, jack: null, port: null, busy: false };
    const port = { kind: jack.dataset.kind, n: Number(jack.dataset.port) };
    const taken = linkAtPort(this.store.map, node, port.kind, port.n);
    return { node, jack, port, busy: !!taken && taken.link.id !== drag.existing?.link.id };
  }

  /** Lasciato il cavo tirato da una presa: collega, ricollega o dice perche' no. */
  plug(drag, event) {
    const map = this.store.map;
    const source = this.store.node(drag.from);
    const portName = (port) => `${port.kind === 'cage' ? 'la SFP' : 'la porta'} ${port.n}`;
    const aim = this.aim(drag, event.clientX, event.clientY);

    if (!aim.node) {
      if (drag.existing) return; // un cavo gia' collegato lasciato nel vuoto resta dov'era
      // una porta libera tirata nel vuoto: un apparato nuovo, collegato a quella porta
      const at = this.scene.toWorld(event.clientX, event.clientY);
      const label = portLabel(map, source, drag.port.kind, drag.port.n);
      this.ui.kindPicker(event.clientX, event.clientY, (kind) => {
        const node = this.store.addNode(kind, at.x - sizeOf({ kind }).w / 2, at.y - sizeOf({ kind }).h / 2);
        if (!node) return null;
        this.scene.pulse.add(node.id);
        this.store.cut();
        const link = this.store.addLink(drag.from, node.id, { pa: label });
        this.store.select([node.id]);
        return link;
      }, 'Collega un apparato nuovo');
      return;
    }

    const target = this.store.node(aim.node);
    if (aim.busy) {
      this.ui.hint(`${portName(aim.port)[0].toUpperCase()}${portName(aim.port).slice(1)} di ${target.name || 'quell\'apparato'} e' gia' occupata: ogni porta tiene un cavo.`, 'crit');
      return;
    }

    // la porta d'arrivo: quella su cui si e' lasciato, o la prima libera dell'apparato
    const port = aim.port ?? freePort(map, target, drag.port.kind);
    const there = port ? portLabel(map, target, port.kind, port.n) : '';

    if (drag.existing) {
      // il cavo si stacca da questa porta e va nell'altra (anche dello stesso apparato)
      const { link, end } = drag.existing;
      if (aim.node === drag.from && (!port || (port.kind === drag.port.kind && port.n === drag.port.n))) return;
      this.store.update(link.id, end === 'a' ? { a: aim.node, pa: there } : { b: aim.node, pb: there });
      this.store.select([link.id]);
      this.ui.hint(`Cavo spostato su ${port ? `${portName(port)} di ` : ''}${target.name || 'un altro apparato'}.`, 'ok');
      return;
    }

    const here = portLabel(map, source, drag.port.kind, drag.port.n);
    const link = this.store.addLink(drag.from, aim.node, { pa: here, pb: there });
    if (link) {
      this.store.select([link.id]);
      this.ui.hint(`Collegati: ${portName(drag.port)} di ${source.name || 'un apparato'} e ${port ? `${portName(port)} di ` : ''}${target.name || 'un apparato'}.`, 'ok');
    }
  }

  nodeAt(clientX, clientY) {
    const el = document.elementFromPoint(clientX, clientY)?.closest?.('[data-type="node"]');
    return el && this.scene.host.contains(el) ? el.dataset.id : null;
  }

  up(event) {
    const drag = this.drag;
    if (!drag || drag.pointer !== event.pointerId) return;
    this.drag = null;
    this.scene.host.classList.remove('panning', 'dragging');
    this.settle();

    switch (drag.type) {
      case 'pan':
        if (drag.moved) this.ui.changedView();
        else if (drag.clear) this.store.deselect();
        break;
      case 'move':
        clearTimeout(this.holdTimer);
        this.scene.guides(null);
        if (drag.moved) this.store.cut();
        // un clic su una porta occupata sceglie il suo cavo
        else if (drag.jack?.existing) this.store.select([drag.jack.existing.link.id]);
        else if (drag.collapse) this.store.select([drag.primary]);
        break;
      case 'connect': {
        this.scene.wire(null);
        this.scene.setTarget(null);
        const target = this.nodeAt(event.clientX, event.clientY);
        if (target && target !== drag.from) {
          const link = this.store.addLink(drag.from, target);
          if (link) this.store.select([link.id]);
        } else if (drag.moved && !target) {
          // un cavo lasciato nel vuoto: un apparato nuovo, gia' collegato, proprio li'
          const at = this.scene.toWorld(event.clientX, event.clientY);
          this.ui.kindPicker(event.clientX, event.clientY, (kind) => {
            const node = this.store.addNode(kind, at.x - sizeOf({ kind }).w / 2, at.y - sizeOf({ kind }).h / 2);
            if (!node) return;
            this.scene.pulse.add(node.id);
            this.store.cut();
            const link = this.store.addLink(drag.from, node.id);
            this.store.select([node.id]);
            return link;
          }, 'Collega un apparato nuovo');
        } else if (!drag.moved && this.ui.mode() === 'connect') {
          this.store.select([drag.from]);
        }
        break;
      }
      case 'plug':
        this.scene.host.classList.remove('plugging');
        this.mark(null);
        for (const el of this.scene.host.querySelectorAll('.plug-from')) el.classList.remove('plug-from');
        this.scene.wire(null);
        this.scene.setTarget(null);
        if (drag.moved) this.plug(drag, event);
        break;
      case 'marquee':
        this.scene.marquee(null);
        break;
      case 'resize':
        if (drag.moved) this.store.cut();
        break;
      default:
        break;
    }
  }

  /** Finito il gesto: le etichette tornano a cercarsi il posto migliore. */
  settle() {
    if (!this.scene.quick) return;
    this.scene.quick = false;
    this.scene.invalidate();
  }

  cancel(clear = true) {
    clearTimeout(this.holdTimer);
    if (!this.drag) return;
    const drag = this.drag;
    this.drag = null;
    this.scene.host.classList.remove('panning', 'dragging', 'plugging');
    this.settle();
    this.mark(null);
    for (const el of this.scene.host.querySelectorAll('.plug-from')) el.classList.remove('plug-from');
    this.scene.wire(null);
    this.scene.setTarget(null);
    this.scene.marquee(null);
    this.scene.guides(null);
    if (clear && (drag.type === 'move' || drag.type === 'resize') && drag.moved) this.store.cut();
  }

  /** Il doppio clic: sul vuoto un apparato nuovo, su un oggetto il suo nome nella scheda. */
  double(event) {
    const hit = this.hitAt(event.clientX, event.clientY);
    if (hit.type === 'empty') {
      if (this.store.readonly) return;
      const at = this.scene.toWorld(event.clientX, event.clientY);
      this.ui.kindPicker(event.clientX, event.clientY, (kind) => {
        const node = this.store.addNode(kind, at.x - sizeOf({ kind }).w / 2, at.y - sizeOf({ kind }).h / 2);
        if (node) {
          this.scene.pulse.add(node.id);
          this.store.select([node.id]);
        }
      });
      return;
    }
    this.store.select([hit.id]);
    this.ui.inspect(hit.type === 'link' ? 'pa' : 'name');
  }

  context(event) {
    event.preventDefault();
    const hit = this.hitAt(event.clientX, event.clientY);
    if (hit.type !== 'empty' && !this.store.sel.has(hit.id)) this.store.select([hit.id]);
    this.ui.contextMenu(event.clientX, event.clientY, hit, this.scene.toWorld(event.clientX, event.clientY));
  }

  /**
   * La rotella: col trackpad (e sul Mac) scorre la vista, con Ctrl/Cmd o col pizzico fa lo
   * zoom; su Windows la rotella di un mouse fa lo zoom, come nei programmi di disegno.
   */
  wheel(event) {
    event.preventDefault();
    stop(this.scene, 'view');
    const mouseWheel = !isMac() && (event.deltaMode !== 0 || (event.deltaX === 0 && Math.abs(event.deltaY) >= 50));
    if (event.ctrlKey || event.metaKey || (mouseWheel && !event.shiftKey)) {
      const step = event.deltaMode === 1 ? event.deltaY * 16 : event.deltaY;
      const factor = Math.exp(-step * (event.ctrlKey && !mouseWheel ? 0.01 : 0.0018));
      this.scene.zoomAt(event.clientX, event.clientY, factor);
    } else {
      const scale = event.deltaMode === 1 ? 16 : 1;
      let dx = event.deltaX * scale;
      let dy = event.deltaY * scale;
      if (event.shiftKey && dx === 0) [dx, dy] = [dy, 0];
      const { x, y, k } = this.scene.view;
      this.scene.setView(x - dx, y - dy, k);
    }
    this.ui.changedView();
  }
}

/** Il punto del bordo di un apparato verso un punto dato (dove parte il cavo che si tira). */
export function edgeToward(node, point) {
  const c = centerOf(node);
  const rect = rectOf(node);
  const dx = point.x - c.x;
  const dy = point.y - c.y;
  if (Math.abs(dx) < 0.001 && Math.abs(dy) < 0.001) return c;
  const tx = dx !== 0 ? (rect.w / 2) / Math.abs(dx) : Infinity;
  const ty = dy !== 0 ? (rect.h / 2) / Math.abs(dy) : Infinity;
  const t = Math.min(tx, ty, 1);
  return inside(point, rect) ? c : { x: c.x + dx * t, y: c.y + dy * t };
}
