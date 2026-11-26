// Le curve di DuckNote (Ui/Motion.cs sul Mac, Fx.cs su Windows), scritte a mano con le stesse
// formule: niente transizioni o animazioni CSS gia' pronte. Ogni curva va da 0 a 1; le elastiche
// e "back" passano un poco oltre l'1 prima di posarsi.

const out = (easeIn) => (t) => 1 - easeIn(1 - t);
const inOut = (easeIn) => (t) => (t < 0.5 ? easeIn(t * 2) * 0.5 : (1 - easeIn((1 - t) * 2)) * 0.5 + 0.5);
const cube = (t) => t * t * t;

export const Curves = {
  linear: (t) => t,
  cubicIn: cube,
  cubicOut: out(cube),
  cubicInOut: inOut(cube),
  quintOut: out((t) => t * t * t * t * t),
  expoOut: (exponent = 4) => out((t) => (Math.exp(exponent * t) - 1) / (Math.exp(exponent) - 1)),
  elasticOut: (oscillations = 1, springiness = 6) => out((t) => {
    const expo = springiness <= 0 ? t : (Math.exp(springiness * t) - 1) / (Math.exp(springiness) - 1);
    return expo * Math.sin((2 * Math.PI * oscillations + Math.PI * 0.5) * t);
  }),
  backOut: (amplitude = 0.35) => out((t) => t * t * t - t * amplitude * Math.sin(Math.PI * t)),
};

// chi ha chiesto meno movimento nel sistema lo vede arrivare subito
const still = () => typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;

const running = new WeakMap();

function channels(owner) {
  let map = running.get(owner);
  if (!map) running.set(owner, (map = new Map()));
  return map;
}

/**
 * Porta un valore da `from` a `to` in `ms` millisecondi lungo `curve`, chiamando `step` a ogni
 * fotogramma. Un movimento nuovo sullo stesso canale ferma quello vecchio dove si vede.
 */
export function tween(owner, channel, from, to, ms, step, done, curve = Curves.cubicOut) {
  stop(owner, channel);
  if (ms <= 0 || still()) {
    step(to);
    done?.();
    return;
  }
  const run = { stopped: false };
  channels(owner).set(channel, run);
  const start = performance.now();
  const frame = (now) => {
    if (run.stopped) return;
    const t = Math.min(1, Math.max(0, (now - start) / ms));
    step(from + (to - from) * curve(t));
    if (t < 1) requestAnimationFrame(frame);
    else {
      channels(owner).delete(channel);
      done?.();
    }
  };
  requestAnimationFrame(frame);
}

export function stop(owner, channel) {
  const map = running.get(owner);
  const run = map?.get(channel);
  if (run) {
    run.stopped = true;
    map.delete(channel);
  }
}

export const isRunning = (owner, channel) => running.get(owner)?.has(channel) ?? false;

// ------------------------------------------------------------------ i gesti ricorrenti

/** Compare: dal basso (o da dove si chiede) e in dissolvenza, 260 ms in uscita cubica. */
export function show(element, { dx = 0, dy = 8, scale = 1, ms = 260 } = {}) {
  element.hidden = false;
  tween(element, 'show', 0, 1, ms, (t) => {
    element.style.opacity = String(t);
    const s = scale + (1 - scale) * t;
    element.style.transform = `translate(${dx * (1 - t)}px, ${dy * (1 - t)}px) scale(${s})`;
  }, () => { element.style.transform = ''; });
}

/** Se ne va: 150 ms in entrata cubica, poi si nasconde davvero. */
export function hide(element, { dx = 0, dy = 6, scale = 1, ms = 150, done } = {}) {
  if (element.hidden) {
    done?.();
    return;
  }
  const from = Number(element.style.opacity || 1);
  tween(element, 'show', from, 0, ms, (t) => {
    element.style.opacity = String(t);
    const s = scale + (1 - scale) * t;
    element.style.transform = `translate(${dx * (1 - t)}px, ${dy * (1 - t)}px) scale(${s})`;
  }, () => {
    element.hidden = true;
    element.style.transform = '';
    done?.();
  }, Curves.cubicIn);
}

/**
 * Il tocco dei pulsanti, come nelle app: premuto si stringe al 95% in 110 ms, deciso; lasciato
 * torna alla misura con un rimbalzo a molla in 520 ms. Non per i pulsanti piu' stretti di 18 px.
 */
export function pressable(root) {
  const scaleOf = (el) => Number(el.dataset.pressScale || 1);
  const set = (el, value) => {
    el.dataset.pressScale = String(value);
    el.style.transform = Math.abs(value - 1) < 0.0005 ? '' : `scale(${value})`;
  };
  let pressed = null;
  const release = () => {
    if (!pressed) return;
    const el = pressed;
    pressed = null;
    tween(el, 'press', scaleOf(el), 1, 520, (v) => set(el, v), null, Curves.elasticOut(1, 4));
  };
  root.addEventListener('pointerdown', (event) => {
    if (event.button !== 0) return;
    const el = event.target.closest?.('.dm-press');
    if (!el || el.disabled || el.getBoundingClientRect().width < 18) return;
    pressed = el;
    tween(el, 'press', scaleOf(el), 0.95, 110, (v) => set(el, v), null, Curves.expoOut(4));
  }, true);
  for (const type of ['pointerup', 'pointercancel', 'dragstart']) root.addEventListener(type, release, true);
  root.addEventListener('pointerout', (event) => {
    if (pressed && event.target === pressed && !pressed.contains(event.relatedTarget)) release();
  }, true);
}

/** Lo scossone di un valore che non va: 320 ms, 0, -9, 8, -5, 3, 0. */
export function shake(element) {
  const keys = [0, -9, 8, -5, 3, 0];
  tween(element, 'shake', 0, 1, 320, (t) => {
    const at = t * (keys.length - 1);
    const i = Math.min(keys.length - 2, Math.floor(at));
    const x = keys[i] + (keys[i + 1] - keys[i]) * (at - i);
    element.style.transform = x ? `translateX(${x}px)` : '';
  }, null, Curves.linear);
}
