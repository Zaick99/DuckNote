// Impacchetta la mappa in due file (JS e CSS) che le app incorporano nella pagina del WebView
// (WKWebView sul Mac, WebView2 su Windows). Si lancia con "npm run build" dopo ogni modifica in
// src/: i due file vanno nel repository, cosi' le app si compilano anche senza Node.
import { build } from 'esbuild';

const out = '../Assets';
// WebKit di macOS 14 (Safari 17), WebView2, WebKitGTK
const target = ['safari16.4', 'chrome110'];

await build({
  entryPoints: ['src/main.js'],
  bundle: true,
  minify: true,
  format: 'iife',
  target,
  legalComments: 'none',
  outfile: `${out}/map.js`,
});

await build({
  entryPoints: ['src/map.css'],
  bundle: true,
  minify: true,
  target,
  outfile: `${out}/map.css`,
});

console.log('mappa pronta in', out);
