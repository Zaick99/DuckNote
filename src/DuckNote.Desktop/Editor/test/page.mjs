// Una pagina di prova con l'editor impacchettato e i colori del tema chiaro, per Playwright.
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const assets = join(here, '..', '..', 'Assets', 'editor');
const tokens = JSON.parse(readFileSync(join(here, '..', '..', '..', 'DuckNote.App', 'Theme', 'tokens.extended.json'), 'utf8'));

export function pageHtml(theme = 'light') {
  const vars = { ...tokens[theme], NotePaper: theme === 'dark' ? '#1E1E20' : '#FFFEFA', NoteMargin: '#F2C8A0' };
  return `<!doctype html><html><head><meta charset="utf-8"><style>${readFileSync(join(assets, 'editor.css'), 'utf8')}</style></head>
<body><script>${readFileSync(join(assets, 'editor.js'), 'utf8')}</script>
<script>dn.theme(${JSON.stringify(vars)}, ${theme === 'dark'}); dn.settle(700);</script></body></html>`;
}
