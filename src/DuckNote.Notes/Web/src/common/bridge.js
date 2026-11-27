// Il filo fra una pagina web di DuckNote e la sua finestra (WKWebView sul Mac, WebView2 su
// Windows): i messaggi verso la finestra, l'attivita' (che rimanda il blocco automatico), le
// scorciatoie della finestra e i colori del tema.

import { modKey } from './dom.js';

const outbox = [];

/** Un messaggio JSON alla finestra; nel browser delle prove resta in outbox(). */
export function send(message) {
  const text = JSON.stringify(message);
  try {
    if (typeof window.invokeCSharpAction === 'function') {
      window.invokeCSharpAction(text);
      return;
    }
    if (window.chrome?.webview?.postMessage) {
      window.chrome.webview.postMessage(text);
      return;
    }
  } catch { /* nel browser delle prove non c'e' la finestra */ }
  outbox.push(message);
}

/** I messaggi che sarebbero andati alla finestra (per le prove), svuotando la coda. */
export const takeOutbox = () => outbox.splice(0);

let lastActivity = 0;

/** Chi usa la pagina sta lavorando: la finestra rimanda il blocco automatico (al massimo ogni 4 s). */
export function activity() {
  const now = Date.now();
  if (now - lastActivity > 4000) {
    lastActivity = now;
    send({ t: 'activity' });
  }
}

/** Le scorciatoie della finestra: dal WebView i tasti non arrivano alla finestra, li si passa. */
export function windowKey(event) {
  const mod = modKey(event);
  const key = event.key;
  let name = null;
  if (key === 'F9' && !mod && !event.altKey) name = 'sidebar';
  else if (mod && (key === '\\' || event.code === 'Backslash' || event.code === 'IntlBackslash')) name = 'sidebar';
  else if (mod && !event.shiftKey && (key === 'l' || key === 'L')) name = 'lock';
  else if (mod && event.shiftKey && (key === 'f' || key === 'F')) name = 'searchSheets';
  else if (mod && key === ',') name = 'settings';
  if (!name) return false;
  event.preventDefault();
  send({ t: 'key', key: name });
  return true;
}

export const TONES = ['Blue', 'Teal', 'Green', 'Yellow', 'Purple', 'Pink', 'Red', 'Gray', 'Indigo', 'Orange'];

/** #RRGGBB o #RRGGBBAA con un'altra opacita'. */
export function withAlpha(value, alpha) {
  const hex = String(value ?? '').trim().replace('#', '');
  if (!/^[0-9a-f]{6}([0-9a-f]{2})?$/i.test(hex)) return value;
  return `#${hex.slice(0, 6)}${alpha}`;
}

/** I colori del tema (token -> #RRGGBBAA) e le tinte morbide che se ne ricavano. */
export function applyTheme(vars, dark) {
  const root = document.documentElement;
  for (const [name, value] of Object.entries(vars ?? {})) root.style.setProperty(`--c-${name}`, value);
  for (const tone of TONES) {
    const value = vars?.[tone];
    if (!value) continue;
    root.style.setProperty(`--t-${tone}-soft`, withAlpha(value, dark ? '2E' : '1F'));
    root.style.setProperty(`--t-${tone}-border`, withAlpha(value, '59'));
    root.style.setProperty(`--t-${tone}-zone`, withAlpha(value, dark ? '17' : '0F'));
  }
  root.dataset.theme = dark ? 'dark' : 'light';
}
