// Impacchetta le pagine web di DuckNote (la mappa, il calendario) in un JS e un CSS ciascuna, che
// le app incorporano nella pagina del WebView (WKWebView sul Mac, WebView2 su Windows). Si lancia
// con "npm run build" dopo ogni modifica in src/: i file in dist/ vanno nel repository, cosi' le
// app si compilano anche senza Node.
import { existsSync } from 'node:fs';
import { build } from 'esbuild';

const out = 'dist';
// WebKit di macOS 14 (Safari 17), WebView2, WebKitGTK
const target = ['safari16.4', 'chrome110'];

for (const page of ['map', 'calendar'].filter((name) => existsSync(`src/${name}/main.js`))) {
  await build({
    entryPoints: [`src/${page}/main.js`],
    bundle: true,
    minify: true,
    format: 'iife',
    target,
    legalComments: 'none',
    outfile: `${out}/${page}.js`,
  });

  await build({
    entryPoints: [`src/${page}/${page}.css`],
    bundle: true,
    minify: true,
    target,
    outfile: `${out}/${page}.css`,
  });
}

console.log('pagine pronte in', out);
