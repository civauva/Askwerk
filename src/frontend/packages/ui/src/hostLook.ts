// SPDX-License-Identifier: AGPL-3.0-only
/**
 * The host look (W37): four values a host app gives its drop-in components (accent, background, font, corners), passed
 * to the frame in its address's #look=… part. Only safe values pass: colours, a plain font list, 0–24 px corners.
 */
export type HostLook = { accent?: string; background?: string; font?: string; radius?: number };

const colour = /^(#[0-9a-f]{3,8}|rgba?\([\d\s.,%]+\)|hsla?\([\d\s.,%deg]+\))$/i;
const font = /^[\w\s"',.-]{1,200}$/;

/** The host look from #look=…, or null. */
export function readHostLook(hash: string): HostLook | null {
  const raw = /(?:^#|&)look=([^&]*)/.exec(hash)?.[1];

  if (raw === undefined) {
    return null;
  }

  try {
    const value = JSON.parse(decodeURIComponent(raw)) as Record<string, unknown>;
    const look: HostLook = {};
    typeof value.accent === "string" && colour.test(value.accent.trim()) && (look.accent = value.accent.trim());
    typeof value.background === "string" && colour.test(value.background.trim()) && (look.background = value.background.trim());
    typeof value.font === "string" && font.test(value.font.trim()) && (look.font = value.font.trim());
    typeof value.radius === "number" && Number.isFinite(value.radius) && (look.radius = Math.min(24, Math.max(0, value.radius)));

    return Object.keys(look).length === 0 ? null : look;
  } catch {
    return null;
  }
}

/** The CSS variables that make the app or the runner follow the host look. */
export function hostLookVariables(look: HostLook, target: "app" | "run"): Record<string, string> {
  const variables: Record<string, string> = {};

  if (look.accent !== undefined) {
    Object.assign(variables, { "--aw-signal": look.accent, "--aw-signal-ink": look.accent, "--aw-focus": look.accent, "--aw-on-signal": "#fff", "--aw-signal-soft": `color-mix(in srgb, ${look.accent} 12%, #fff)` });
  }

  if (look.background !== undefined) {
    variables["--aw-paper"] = look.background;
  }

  if (look.font !== undefined) {
    Object.assign(variables, { "--aw-font": look.font, "--aw-font-display": look.font });
  }

  if (look.radius !== undefined) {
    const card = `${Math.min(24, look.radius * 2)}px`;
    Object.assign(variables, target === "run" ? { "--run-radius": `${look.radius}px`, "--run-radius-card": card } : { "--aw-radius-control": `${look.radius}px`, "--aw-radius-card": card });
  }

  return variables;
}
