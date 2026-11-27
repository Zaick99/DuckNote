// Una pagina di prova con la mappa impacchettata e i colori del tema, per Playwright.
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const assets = join(here, '..', 'dist');
const tokens = JSON.parse(readFileSync(join(here, '..', '..', '..', 'DuckNote.App', 'Theme', 'tokens.extended.json'), 'utf8'));
const art = readFileSync(join(here, '..', '..', '..', 'DuckNote.App', 'Assets', 'art', 'network.png')).toString('base64');

// i token sono #AARRGGBB (WPF); il web li vuole #RRGGBBAA, come li manda la finestra
const css = (theme) => Object.fromEntries(Object.entries(tokens[theme]).map(([key, value]) => {
  const hex = value.replace('#', '');
  return [key, hex.length === 8 ? `#${hex.slice(2)}${hex.slice(0, 2)}` : value];
}));

/** La pagina del calendario, coi colori del tema. */
export function calendarHtml(theme = 'light') {
  return `<!doctype html><html lang="it"><head><meta charset="utf-8"><style>${readFileSync(join(assets, 'calendar.css'), 'utf8')}</style></head>
<body><script>${readFileSync(join(assets, 'calendar.js'), 'utf8')}</script>
<script>dc.theme(${JSON.stringify(css(theme))}, ${theme === 'dark'});</script></body></html>`;
}

export function pageHtml(theme = 'light') {
  return `<!doctype html><html lang="it"><head><meta charset="utf-8"><style>${readFileSync(join(assets, 'map.css'), 'utf8')}</style>
<style>:root{--empty-art:url(data:image/png;base64,${art})}</style></head>
<body><script>${readFileSync(join(assets, 'map.js'), 'utf8')}</script>
<script>dm.theme(${JSON.stringify(css(theme))}, ${theme === 'dark'});</script></body></html>`;
}
