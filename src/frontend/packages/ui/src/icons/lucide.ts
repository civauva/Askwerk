// SPDX-License-Identifier: ISC
// Lucide icons (https://lucide.dev), version 1.17.0, copied as data: only the ones Askwerk uses, no package
// (agents/ui-design-system.md, Icons). Licence: LICENSE-lucide next to this file.

/** One drawing element: its tag and attributes, on a 24 × 24 grid with 2 px round strokes. */
export type IconNode = readonly (readonly [tag: "path" | "circle" | "line" | "rect", attributes: Readonly<Record<string, string>>])[];

export const lucide = {
  "square-pen": [ ["path", { "d": "M12 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" }], [ "path", { "d": "M18.375 2.625a1 1 0 0 1 3 3l-9.013 9.014a2 2 0 0 1-.853.505l-2.873.84a.5.5 0 0 1-.62-.62l.84-2.873a2 2 0 0 1 .506-.852z" } ]],
  "copy-plus": [ ["line", { "x1": "15", "x2": "15", "y1": "12", "y2": "18" }], ["line", { "x1": "12", "x2": "18", "y1": "15", "y2": "15" }], ["rect", { "width": "14", "height": "14", "x": "8", "y": "8", "rx": "2", "ry": "2" }], ["path", { "d": "M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2" }]],
  "circle-play": [ [ "path", { "d": "M9 9.003a1 1 0 0 1 1.517-.859l4.997 2.997a1 1 0 0 1 0 1.718l-4.997 2.997A1 1 0 0 1 9 14.996z" } ], ["circle", { "cx": "12", "cy": "12", "r": "10" }]],
  "circle-pause": [ ["circle", { "cx": "12", "cy": "12", "r": "10" }], ["line", { "x1": "10", "x2": "10", "y1": "15", "y2": "9" }], ["line", { "x1": "14", "x2": "14", "y1": "15", "y2": "9" }]],
  "rotate-ccw": [ ["path", { "d": "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8" }], ["path", { "d": "M3 3v5h5" }]],
  "trash-2": [ ["path", { "d": "M10 11v6" }], ["path", { "d": "M14 11v6" }], ["path", { "d": "M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6" }], ["path", { "d": "M3 6h18" }], ["path", { "d": "M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" }]],
  "user-x": [ ["path", { "d": "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" }], ["circle", { "cx": "9", "cy": "7", "r": "4" }], ["line", { "x1": "17", "x2": "22", "y1": "8", "y2": "13" }], ["line", { "x1": "22", "x2": "17", "y1": "8", "y2": "13" }]],
  "ban": [ ["circle", { "cx": "12", "cy": "12", "r": "10" }], ["path", { "d": "M4.929 4.929 19.07 19.071" }]],
  "send": [ [ "path", { "d": "M14.536 21.686a.5.5 0 0 0 .937-.024l6.5-19a.496.496 0 0 0-.635-.635l-19 6.5a.5.5 0 0 0-.024.937l7.93 3.18a2 2 0 0 1 1.112 1.11z" } ], ["path", { "d": "m21.854 2.147-10.94 10.939" }]],
  "history": [ ["path", { "d": "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8" }], ["path", { "d": "M3 3v5h5" }], ["path", { "d": "M12 7v5l4 2" }]],
  "plug": [ ["path", { "d": "M12 22v-5" }], ["path", { "d": "M15 8V2" }], [ "path", { "d": "M17 8a1 1 0 0 1 1 1v4a4 4 0 0 1-4 4h-4a4 4 0 0 1-4-4V9a1 1 0 0 1 1-1z" } ], ["path", { "d": "M9 8V2" }]],
  "globe": [ ["circle", { "cx": "12", "cy": "12", "r": "10" }], ["path", { "d": "M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20" }], ["path", { "d": "M2 12h20" }]],
  "check": [["path", { "d": "M20 6 9 17l-5-5" }]],
  "chevron-down": [["path", { "d": "m6 9 6 6 6-6" }]],
} as const satisfies Record<string, IconNode>;

export type IconName = keyof typeof lucide;
