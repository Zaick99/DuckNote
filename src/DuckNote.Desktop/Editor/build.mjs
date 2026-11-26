// Impacchetta l'editor in due file (JS e CSS) che l'app incorpora nella pagina del WebView.
// Si lancia con "npm run build" dopo ogni modifica in src/: i due file vanno nel repository,
// cosi' l'app si compila anche senza Node.
import { build } from 'esbuild';

const out = '../Assets/editor';
// WebKit di macOS 13.3+ (Safari 16.4: lookbehind nelle espressioni regolari), WebView2, WebKitGTK
const target = ['safari16.4', 'chrome110'];

await build({
  entryPoints: ['src/main.js'],
  bundle: true,
  minify: true,
  format: 'iife',
  target,
  legalComments: 'none',
  outfile: `${out}/editor.js`,
});

await build({
  entryPoints: ['src/editor.css'],
  bundle: true,
  minify: true,
  target,
  outfile: `${out}/editor.css`,
});

console.log('editor pronto in', out);
