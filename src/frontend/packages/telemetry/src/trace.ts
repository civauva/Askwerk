// SPDX-License-Identifier: AGPL-3.0-only

const toHex = (bytes: Uint8Array) => Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0")).join("");

/**
 * Creates a W3C trace context header for one API call, so the click in the browser and the server work
 * it caused share one trace id.
 */
export function newTraceparent(): { traceparent: string; traceId: string } {
  const traceId = toHex(crypto.getRandomValues(new Uint8Array(16)));
  const spanId = toHex(crypto.getRandomValues(new Uint8Array(8)));

  return { traceparent: `00-${traceId}-${spanId}-01`, traceId };
}
