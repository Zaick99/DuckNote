// I servizi VPN e le reti sovrapposte che un collegamento VPN puo' nominare: il nome, il logo
// (piccolo, sul collegamento, nella scheda e nella legenda) e il colore principale del logo, che
// e' il colore del collegamento. I colori sono variabili CSS (--vpn-<id>, in map.css, con la
// variante scura): lo schermo segue il tema, l'esportazione li sostituisce coi valori veri.
//
// Loghi: Tailscale, NordVPN, ZeroTier, Cloudflare, WireGuard e OpenVPN dal pacchetto simple-icons
// (CC0 1.0); NetBird dal suo repository (github.com/netbirdio/netbird, BSD-3-Clause); il
// lucchetto di IPsec e' disegnato qui. I marchi restano dei loro proprietari: servono solo a
// riconoscere a colpo d'occhio che cosa porta il collegamento.

/** I servizi, nell'ordine della scheda del collegamento e della legenda. */
export const VPNS = [
  { id: "tailscale", label: "Tailscale", kind: "Rete mesh", view: "0 0 24 24", paths: [{ d: "M24 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0zm-9 9a3 3 0 1 1-6 0 3 3 0 0 1 6 0zm0-9a3 3 0 1 1-6 0 3 3 0 0 1 6 0zm6-6a3 3 0 1 1 0-6 3 3 0 0 1 0 6zm0-.5a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5zM3 24a3 3 0 1 1 0-6 3 3 0 0 1 0 6zm0-.5a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5zm18 .5a3 3 0 1 1 0-6 3 3 0 0 1 0 6zm0-.5a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5zM6 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0zm9-9a3 3 0 1 1-6 0 3 3 0 0 1 6 0zm-3 2.5a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5zM6 3a3 3 0 1 1-6 0 3 3 0 0 1 6 0zM3 5.5a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5z" }] },
  { id: "netbird", label: "NetBird", kind: "Rete mesh", view: "0 0 31 23", paths: [{ d: "M21.4631 0.523438C17.8173 0.857913 16.0028 2.95675 15.3171 4.01871L4.66406 22.4734H17.5163L30.1929 0.523438H21.4631Z", fill: "#F68330" }, { d: "M17.5265 22.4737L0 3.88525C0 3.88525 19.8177 -1.44128 21.7493 15.1738L17.5265 22.4737Z", fill: "#F68330" }, { d: "M14.9236 4.70563L9.54688 14.0208L17.5158 22.4747L21.7385 15.158C21.0696 9.44682 18.2851 6.32784 14.9236 4.69727", fill: "#F05252" }] },
  { id: "meshnet", label: "NordVPN Meshnet", short: "Meshnet", kind: "Rete mesh", view: "0 0 24 24", paths: [{ d: "M2.2838 21.5414A11.9866 11.9866 0 010 14.4832C0 7.8418 5.3727 2.4586 12 2.4586c6.6279 0 12 5.3832 12 12.0246a11.9853 11.9853 0 01-2.2838 7.0582l-5.7636-9.3783-.5565.9419.5645 2.6186L12 8.9338l-2.45 4.1447.5707 2.6451-2.0764-3.5555-5.7605 9.3733z" }] },
  { id: "zerotier", label: "ZeroTier", kind: "Rete mesh", view: "0 0 24 24", paths: [{ d: "M4.01 0A3.999 3.999 0 0 0 .014 4v16c0 2.209 1.79 4 3.996 4h15.98a3.998 3.998 0 0 0 3.996-4V4c0-2.209-1.79-4-3.996-4zm-.672 2.834h17.326a.568.568 0 1 1 0 1.137h-8.129c.021.059.033.123.033.19v1.804A6.06 6.06 0 0 1 18.057 12c0 3.157-2.41 5.75-5.489 6.037v2.56a.568.568 0 1 1-1.136 0v-2.56A6.061 6.061 0 0 1 5.943 12a6.06 6.06 0 0 1 5.489-6.035V4.16c0-.066.012-.13.033-.19H3.338a.568.568 0 1 1 0-1.136zm8.094 4.307A4.89 4.89 0 0 0 7.113 12a4.89 4.89 0 0 0 4.319 4.86zm1.136 0v9.718A4.892 4.892 0 0 0 16.888 12a4.892 4.892 0 0 0-4.32-4.86z" }] },
  { id: "cloudflare", label: "Cloudflare", kind: "Tunnel e Zero Trust", view: "0 0 24 24", paths: [{ d: "M16.5088 16.8447c.1475-.5068.0908-.9707-.1553-1.3154-.2246-.3164-.6045-.499-1.0615-.5205l-8.6592-.1123a.1559.1559 0 0 1-.1333-.0713c-.0283-.042-.0351-.0986-.021-.1553.0278-.084.1123-.1484.2036-.1562l8.7359-.1123c1.0351-.0489 2.1601-.8868 2.5537-1.9136l.499-1.3013c.0215-.0561.0293-.1128.0147-.168-.5625-2.5463-2.835-4.4453-5.5499-4.4453-2.5039 0-4.6284 1.6177-5.3876 3.8614-.4927-.3658-1.1187-.5625-1.794-.499-1.2026.119-2.1665 1.083-2.2861 2.2856-.0283.31-.0069.6128.0635.894C1.5683 13.171 0 14.7754 0 16.752c0 .1748.0142.3515.0352.5273.0141.083.0844.1475.1689.1475h15.9814c.0909 0 .1758-.0645.2032-.1553l.12-.4268zm2.7568-5.5634c-.0771 0-.1611 0-.2383.0112-.0566 0-.1054.0415-.127.0976l-.3378 1.1744c-.1475.5068-.0918.9707.1543 1.3164.2256.3164.6055.498 1.0625.5195l1.8437.1133c.0557 0 .1055.0263.1329.0703.0283.043.0351.1074.0214.1562-.0283.084-.1132.1485-.204.1553l-1.921.1123c-1.041.0488-2.1582.8867-2.5527 1.914l-.1406.3585c-.0283.0713.0215.1416.0986.1416h6.5977c.0771 0 .1474-.0489.169-.126.1122-.4082.1757-.837.1757-1.2803 0-2.6025-2.125-4.727-4.7344-4.727" }] },
  { id: "wireguard", label: "WireGuard", kind: "VPN", view: "0 0 24 24", paths: [{ d: "M23.98 11.645S24.533 0 11.735 0C.418 0 .064 11.17.064 11.17S-1.6 24 11.997 24C25.04 24 23.98 11.645 23.98 11.645zM8.155 7.576c2.4-1.47 5.469-.571 6.618 1.638.218.419.246 1.063.108 1.503-.477 1.516-1.601 2.366-3.145 2.728.455-.39.817-.832.933-1.442a2.112 2.112 0 0 0-.364-1.677 2.14 2.14 0 0 0-2.465-.75c-.95.36-1.47 1.228-1.377 2.294.087.99.839 1.632 2.245 1.876-.21.111-.372.193-.53.281a5.113 5.113 0 0 0-1.644 1.43c-.143.192-.24.208-.458.075-2.827-1.729-3.009-6.067.078-7.956zM6.04 18.258c-.455.116-.895.286-1.359.438.227-1.532 2.021-2.943 3.539-2.782a3.91 3.91 0 0 0-.74 2.072c-.504.093-.98.155-1.44.272zM15.703 3.3c.448.017.898.01 1.347.02a2.324 2.324 0 0 1 .334.047 3.249 3.249 0 0 1-.34.434c-.16.15-.341.296-.573.069-.055-.055-.187-.042-.283-.044-.447-.005-.894-.02-1.34-.003a8.323 8.323 0 0 0-1.154.118c-.072.013-.178.25-.146.338.078.207.191.435.359.567.619.49 1.277.928 1.9 1.413.604.472 1.167.99 1.51 1.7.446.928.46 1.9.267 2.877-.322 1.63-1.147 2.98-2.483 3.962-.538.395-1.205.62-1.821.903-.543.25-1.1.465-1.644.712-.98.446-1.53 1.51-1.369 2.615.149 1.015 1.04 1.862 2.059 2.037 1.223.21 2.486-.586 2.785-1.83.336-1.397-.423-2.646-1.845-3.024l-.256-.066c.38-.17.708-.291 1.012-.458q.793-.437 1.558-.925c.15-.096.231-.096.36.014.977.846 1.56 1.898 1.724 3.187.27 2.135-.74 4.096-2.646 5.101-2.948 1.555-6.557-.215-7.208-3.484-.558-2.8 1.418-5.34 3.797-5.83 1.023-.211 1.958-.637 2.685-1.425.47-.508.697-.944.775-1.141a3.165 3.165 0 0 0 .217-1.158 2.71 2.71 0 0 0-.237-.992c-.248-.566-1.2-1.466-1.435-1.656l-2.24-1.754c-.079-.065-.168-.06-.36-.047-.23.016-.815.048-1.067-.018.204-.155.76-.38 1-.56-.726-.49-1.554-.314-2.315-.46.176-.328 1.046-.831 1.541-.888a7.323 7.323 0 0 0-.135-.822c-.03-.111-.154-.22-.263-.283-.262-.154-.541-.281-.843-.434a1.755 1.755 0 0 1 .906-.28 3.385 3.385 0 0 1 .908.088c.54.123.97.042 1.399-.324-.338-.136-.676-.26-1.003-.407a9.843 9.843 0 0 1-.942-.493c.85.118 1.671.437 2.54.32l.022-.118-2.018-.47c1.203-.11 2.323-.128 3.384.388.299.146.61.266.897.432.14.08.233.24.348.365.09.098.164.23.276.29.424.225.89.234 1.366.223l.01-.16c.479.15 1.017.702 1.017 1.105-.776 0-1.55-.003-2.325.004-.083 0-.165.061-.247.094.078.046.155.128.235.131z M14.703 2.153a.118.118 0 0 0-.016.19.179.179 0 0 0 .246.065c.075-.038.148-.078.238-.125-.072-.062-.13-.114-.19-.163-.106-.087-.193-.032-.278.033z" }] },
  { id: "openvpn", label: "OpenVPN", kind: "VPN", view: "0 0 24 24", paths: [{ d: "M12 .357C5.385.357 0 5.69 0 12.254c0 4.36 2.358 8.153 5.896 10.204l.77-5.076a7.046 7.046 0 01-1.846-4.719c0-3.897 3.18-7.076 7.13-7.076 3.948 0 7.126 3.18 7.126 7.076 0 1.847-.717 3.488-1.846 4.77L18 22.51c3.59-2.05 6-5.899 6-10.258C24 5.69 18.615.357 12 .357zm-.05 8.157a3.786 3.786 0 00-3.796 3.795 3.738 3.738 0 002.461 3.54L9.13 23.643h5.64l-1.435-7.795c1.385-.564 2.41-1.898 2.41-3.54a3.786 3.786 0 00-3.795-3.794z" }] },
  { id: "ipsec", label: "IPsec", kind: "VPN", view: "0 0 24 24", paths: [{ d: "M7 10V7a5 5 0 0 1 10 0v3h1.5A1.5 1.5 0 0 1 20 11.5v9a1.5 1.5 0 0 1-1.5 1.5h-13A1.5 1.5 0 0 1 4 20.5v-9A1.5 1.5 0 0 1 5.5 10H7zm2 0h6V7a3 3 0 0 0-6 0v3zm3 4.25a1.75 1.75 0 0 0-1 3.19V19.5h2v-2.06a1.75 1.75 0 0 0-1-3.19z" }] },
];
export const VPN = Object.fromEntries(VPNS.map((vpn) => [vpn.id, vpn]));

/** Il colore del servizio (variabile CSS). */
export const vpnColor = (id) => `var(--vpn-${id})`;

/** Il servizio di un collegamento: solo per i collegamenti VPN, e solo se e' uno di quelli conosciuti. */
export const vpnOf = (link) => (link?.kind === 'vpn' && VPN[link.vpn] ? VPN[link.vpn] : null);

/**
 * Il logo come gruppo SVG, centrato in un quadrato di lato size che comincia in x, y: i tracciati
 * senza colore proprio prendono quello del servizio.
 */
export function logoMarkup(vpn, x, y, size) {
  const [, , vw, vh] = vpn.view.split(' ').map(Number);
  const k = size / Math.max(vw, vh);
  const ox = x + (size - vw * k) / 2;
  const oy = y + (size - vh * k) / 2;
  const paths = vpn.paths.map((path) => `<path d="${path.d}" fill="${path.fill ?? vpnColor(vpn.id)}" fill-rule="evenodd"/>`).join('');
  return `<g transform="translate(${ox.toFixed(2)} ${oy.toFixed(2)}) scale(${k.toFixed(4)})">${paths}</g>`;
}

/** Il logo come elemento <svg> a se' (la scheda del collegamento, la legenda). */
export function logoSvg(vpn, size = 16) {
  const el = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  el.setAttribute('class', 'dm-vpn-logo');
  el.setAttribute('width', String(size));
  el.setAttribute('height', String(size));
  el.setAttribute('viewBox', `0 0 ${size} ${size}`);
  el.setAttribute('aria-hidden', 'true');
  el.innerHTML = logoMarkup(vpn, 0, 0, size);
  return el;
}

/** Il distintivo sul collegamento: una tessera chiara col bordo del colore del servizio e il logo. */
export const BADGE = 22;

export function badgeMarkup(vpn, cx, cy, down = false) {
  const x = cx - BADGE / 2;
  const y = cy - BADGE / 2;
  return `<rect x="${x.toFixed(1)}" y="${y.toFixed(1)}" width="${BADGE}" height="${BADGE}" rx="6" fill="var(--vt-panel)" stroke="${down ? 'var(--vt-crit)' : vpnColor(vpn.id)}" stroke-width="1.5"/>`
    + logoMarkup(vpn, x + 4, y + 4, BADGE - 8);
}

/**
 * Dove va il logo sulla strada di un collegamento: poco prima del capo d'arrivo, sul tratto che
 * entra nell'apparato (quello e' solo suo: i tratti vicino alla partenza i collegamenti di uno
 * stesso apparato spesso li condividono), sopra alla velocita'. Sui collegamenti corti, a meta'.
 */
export function badgePoint(points, before = 56) {
  let total = 0;
  for (let i = 1; i < points.length; i++) total += Math.hypot(points[i].x - points[i - 1].x, points[i].y - points[i - 1].y);
  let left = Math.min(before, total / 2);
  for (let i = points.length - 1; i > 0; i--) {
    const a = points[i];
    const b = points[i - 1];
    const step = Math.hypot(b.x - a.x, b.y - a.y);
    if (step >= left && step > 0) return { x: a.x + ((b.x - a.x) * left) / step, y: a.y + ((b.y - a.y) * left) / step };
    left -= step;
  }
  return points[0];
}
