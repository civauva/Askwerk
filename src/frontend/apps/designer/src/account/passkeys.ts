// SPDX-License-Identifier: AGPL-3.0-only

/**
 * The browser's passkey support (WebAuthn level 3), behind an interface so the screens can be tested without one.
 * Options come from the server as JSON and credentials go back as the JSON of PublicKeyCredential.toJSON().
 */
export interface PasskeyDevice {
  /** False where the browser cannot create or use passkeys; the screens then hide or disable passkey actions. */
  readonly supported: boolean;
  /** Asks for fingerprint, face or PIN and creates a passkey; rejects when cancelled. */
  create(options: object): Promise<string>;
  /** Asks for fingerprint, face or PIN and signs the challenge with a passkey; rejects when cancelled. */
  get(options: object): Promise<string>;
}

type JsonOptions = Parameters<typeof PublicKeyCredential.parseCreationOptionsFromJSON>[0];
type JsonRequestOptions = Parameters<typeof PublicKeyCredential.parseRequestOptionsFromJSON>[0];

function supported(): boolean {
  return (
    typeof PublicKeyCredential !== "undefined" &&
    typeof PublicKeyCredential.parseCreationOptionsFromJSON === "function" &&
    typeof PublicKeyCredential.parseRequestOptionsFromJSON === "function" &&
    typeof navigator.credentials?.create === "function"
  );
}

async function asJson(credential: Credential | null): Promise<string> {
  if (!(credential instanceof PublicKeyCredential)) {
    throw new DOMException("No passkey was returned.", "NotAllowedError");
  }

  return JSON.stringify(credential.toJSON());
}

/** The current browser's passkeys. */
export const browserPasskeys: PasskeyDevice = {
  get supported() {
    return supported();
  },
  create: async (options) =>
    asJson(await navigator.credentials.create({ publicKey: PublicKeyCredential.parseCreationOptionsFromJSON(options as JsonOptions) })),
  get: async (options) =>
    asJson(await navigator.credentials.get({ publicKey: PublicKeyCredential.parseRequestOptionsFromJSON(options as JsonRequestOptions) })),
};
