// SPDX-License-Identifier: Apache-2.0
// A survey's look as data (AWK-17, W10 rev. 2): the runner, the email renderer and the PDF renderer each map the same
// tokens; nothing here is web-only. Twin of Askwerk.Domain/Themes/ThemeTokens.cs.

/** A colour as "#rrggbb". */
export type ThemeColour = string;

/** A font: one of the self-hosted families, or a font file the organisation uploaded (asset id). */
export interface ThemeFont {
  readonly family: string;
  readonly asset?: string;
}

/** An uploaded image; the focal point (0–1 from the left and top) stays visible when the image is cropped. */
export interface ThemeImage {
  readonly asset: string;
  readonly focalX?: number;
  readonly focalY?: number;
}

/** Behind the content: a colour, an image, or an image under a colour overlay. */
export interface ThemeBackground {
  readonly kind: "colour" | "image" | "image-overlay";
  readonly image?: ThemeImage;
  readonly fit?: "cover" | "contain" | "tile";
  readonly overlay?: ThemeColour;
  /** 0–1. */
  readonly overlayOpacity?: number;
}

export interface ThemeColours {
  readonly accent: ThemeColour;
  /** Text and icons on the accent colour (buttons, chosen answers). */
  readonly onAccent: ThemeColour;
  readonly background: ThemeColour;
  /** Cards and answer controls. */
  readonly surface: ThemeColour;
  readonly text: ThemeColour;
  readonly muted: ThemeColour;
  readonly line: ThemeColour;
}

/** Five steps since W47 (AWK-73): extra small to extra large. */
export type ThemeSize = "xs" | "s" | "m" | "l" | "xl";
export type SelectedStyle = "accent" | "text" | "fill";
export type PageLayout = "left" | "centre" | "band";
export type ThemePattern = "none" | "confetti" | "dots" | "grid" | "waves" | "hills" | "stripes" | "mountains";

/** Finer settings of each part of a page (W26). Every field is optional; unset ones follow the main settings. */
export interface ThemeComponents {
  readonly question?: { readonly size?: ThemeSize; readonly font?: "heading" | "body"; readonly numbered?: boolean };
  /** The style is ThemeTokens.controls. */
  readonly choices?: { readonly columns?: "one" | "two" | "auto"; readonly selected?: SelectedStyle; readonly marker?: "round" | "check" | "none"; readonly other?: "inside" | "below" };
  /** The style is ThemeTokens.buttons. */
  readonly buttons?: { readonly width?: "auto" | "wide" | "full"; readonly align?: "split" | "end" | "centre" };
  readonly textFields?: { readonly style?: "boxes" | "underline" | "filled" };
  readonly rating?: { readonly size?: ThemeSize; readonly selected?: SelectedStyle };
  readonly slider?: { readonly track?: "thin" | "thick"; readonly thumb?: "round" | "square" };
  readonly matrix?: { readonly stripes?: boolean; readonly header?: "plain" | "tinted" };
  /** The style is ThemeTokens.progress. */
  readonly progress?: { readonly label?: boolean; readonly position?: "top" | "bottom" };
  readonly messages?: { readonly error?: ThemeColour; readonly style?: "box" | "bar" };
  /** Text blocks between the questions (W28): on a lighter tint with an accent edge, or plain. */
  readonly textBlock?: { readonly style?: "tinted" | "plain" };
}

/** Header, welcome, question and end pages (W26). */
export interface ThemePages {
  readonly header?: { readonly show?: boolean; readonly organisation?: boolean; readonly logoSize?: ThemeSize };
  readonly welcome?: { readonly layout?: PageLayout };
  /** panel (W28): the content on the surface, or frosted over the background. */
  readonly questions?: { readonly width?: "narrow" | "normal" | "wide"; readonly cards?: boolean; readonly panel?: "solid" | "frosted" };
  /** Thank-you, closed and other messages. */
  readonly end?: { readonly layout?: PageLayout };
  /** Where the survey's title shows on question pages (W28). */
  readonly title?: "panel" | "over-picture";
}

export interface ThemeTokens {
  readonly schemaVersion: 1;
  readonly colours: ThemeColours;
  readonly fonts: { readonly heading: ThemeFont; readonly body: ThemeFont };
  readonly scale: ThemeSize;
  readonly spacing: "compact" | "normal" | "airy";
  readonly corners: "sharp" | "rounded" | "pill";
  readonly buttons: "filled" | "outline" | "soft";
  readonly controls: "boxes" | "pills" | "underline" | "tiles";
  readonly progress: "ruler" | "bar" | "steps" | "none";
  readonly layout: "card" | "header-image" | "split-image" | "minimal";
  readonly logo?: ThemeImage;
  readonly headerImage?: ThemeImage;
  readonly background?: ThemeBackground;
  readonly components?: ThemeComponents;
  readonly pages?: ThemePages;
  /** A pattern drawn from the colours, no file needed (W26). */
  readonly decoration?: { readonly pattern: ThemePattern };
  /** Light, dark, or the device's choice (W26); dark uses darkColours. */
  readonly mode?: "light" | "dark" | "auto";
  readonly darkColours?: ThemeColours;
  /** How invitation emails follow the look (W26). */
  readonly email?: { readonly header?: "logo" | "band" | "plain" };
  /** How the PDF follows the look (W26): plain header for printing, numbered questions. */
  readonly pdf?: { readonly header?: "full" | "plain"; readonly numbered?: boolean };
}
