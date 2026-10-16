// SPDX-License-Identifier: AGPL-3.0-only
import { encodeQr } from "./qr";

/** A QR code as an SVG file, for download (AWK-7): black on white with the quiet zone. */
export function qrSvg(value: string): string {
  const modules = encodeQr(value);
  const extent = modules.length + 8;
  const path = modules.flatMap((row, y) => row.map((dark, x) => (dark ? `M${x + 4},${y + 4}h1v1h-1z` : ""))).join("");

  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${extent} ${extent}" width="${extent * 10}" height="${extent * 10}" shape-rendering="crispEdges"><rect width="${extent}" height="${extent}" fill="#ffffff"/><path d="${path}" fill="#000000"/></svg>`;
}

/** A QR code as SVG: black on white with the 4-module quiet zone, so it scans in dark mode too. */
export function QrCode({ value, label, size = 168 }: { value: string; label: string; size?: number }) {
  const modules = encodeQr(value);
  const quiet = 4;
  const extent = modules.length + quiet * 2;
  const path = modules
    .flatMap((row, y) => row.map((dark, x) => (dark ? `M${x + quiet},${y + quiet}h1v1h-1z` : "")))
    .join("");

  return (
    <svg role="img" aria-label={label} viewBox={`0 0 ${extent} ${extent}`} width={size} height={size} shapeRendering="crispEdges" className="aw-qr">
      <rect width={extent} height={extent} fill="#ffffff" />
      <path d={path} fill="#000000" />
    </svg>
  );
}
