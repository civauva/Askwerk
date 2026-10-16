// SPDX-License-Identifier: AGPL-3.0-only
import type { SurveyLook as StoredLook } from "@askwerk/app-client";
import { ApiError } from "@askwerk/app-client";
import { checkTheme, contrastRatio, createTranslator, themePresets, type Locale, type SurveyDefinition, type ThemeIssue, type ThemeTag, type ThemeTokens } from "@askwerk/schema";
import { Dialog, DialogActions, Menu, MenuItem, Switch } from "@askwerk/ui";
import { useEffect, useId, useMemo, useRef, useState, type ReactNode, type RefObject } from "react";
import type { ThemeChoice } from "../api";
import { changesBetween, darkColoursFor, getAt, presetOf, resetPaths, setAt, type LookPath } from "./lookEditing";
import { LookThumb } from "./LookThumb";
import { lookMessages, type LookMessageKey } from "./messages";
import "./look.css";

export type LookText = (key: LookMessageKey, values?: Record<string, string | number>) => string;

/** What Look needs from the App API (W26). */
export interface LookApi {
  look(surveyId: string): Promise<StoredLook<ThemeTokens>>;
  setLook(surveyId: string, tokens: ThemeTokens, lookBase: string | null): Promise<void>;
  setTheme(surveyId: string, themeId: string | null): Promise<void>;
  listThemes(): Promise<ThemeChoice[]>;
  /** Saves the look for the whole organisation; 403 for roles other than owner and admin. */
  saveTheme(name: string, tokens: ThemeTokens): Promise<string>;
  uploadImage(file: File): Promise<string>;
  assetUrl(assetId: string, width?: number): string;
}

type Option = readonly [string, LookMessageKey];
type Field =
  | { readonly kind: "choice"; readonly label: LookMessageKey; readonly path: LookPath; readonly options: readonly Option[]; readonly fallback: string }
  | { readonly kind: "switch"; readonly label: LookMessageKey; readonly path: LookPath; readonly fallback: boolean }
  | { readonly kind: "colour"; readonly label: LookMessageKey; readonly path: LookPath; readonly fallback?: LookPath }
  | { readonly kind: "logo" | "header" | "questions-layout" | "dark-colours" };
type Part = { readonly id: string; readonly label: LookMessageKey; readonly fields: readonly Field[]; readonly goto?: string };
type Area = { readonly id: string; readonly label: LookMessageKey; readonly fields?: readonly Field[]; readonly parts?: readonly Part[]; readonly goto?: string };

const families: readonly Option[] = [
  ["IBM Plex Sans", "fontSans"],
  ["IBM Plex Sans Condensed", "fontCondensed"],
  ["IBM Plex Serif", "fontSerif"],
  ["IBM Plex Mono", "fontMono"],
];
const sizes: readonly Option[] = [
  ["s", "sizeS"],
  ["m", "sizeM"],
  ["l", "sizeL"],
];
const choice = (label: LookMessageKey, path: LookPath, options: readonly Option[], fallback: string): Field => ({ kind: "choice", label, path, options, fallback });
const toggle = (label: LookMessageKey, path: LookPath, fallback: boolean): Field => ({ kind: "switch", label, path, fallback });
const colour = (label: LookMessageKey, name: string): Field => ({ kind: "colour", label, path: ["colours", name] });

/** The seven areas (W26 B), closed by default; Components holds its parts. */
const areas: readonly Area[] = [
  {
    id: "colours",
    label: "areaColours",
    fields: [
      colour("cAccent", "accent"),
      colour("cOnAccent", "onAccent"),
      colour("cBackground", "background"),
      colour("cSurface", "surface"),
      colour("cText", "text"),
      colour("cMuted", "muted"),
      colour("cLine", "line"),
      { kind: "colour", label: "cError", path: ["components", "messages", "error"] },
      { kind: "dark-colours" },
    ],
  },
  {
    id: "type",
    label: "areaType",
    goto: "single,multiple",
    fields: [choice("headingFont", ["fonts", "heading", "family"], families, ""), choice("bodyFont", ["fonts", "body", "family"], families, ""), choice("textSize", ["scale"], sizes, "m")],
  },
  {
    id: "shape",
    label: "areaShape",
    goto: "single,multiple",
    fields: [
      choice("corners", ["corners"], [["sharp", "cornersSharp"], ["rounded", "cornersRounded"], ["pill", "cornersPill"]], "sharp"),
      choice("density", ["spacing"], [["compact", "spacingCompact"], ["normal", "spacingNormal"], ["airy", "spacingAiry"]], "normal"),
    ],
  },
  {
    id: "header",
    label: "areaHeader",
    goto: "welcome",
    fields: [
      choice("layout", ["layout"], [["card", "layoutCard"], ["header-image", "layoutHeaderImage"], ["split-image", "layoutSplit"], ["minimal", "layoutMinimal"]], "card"),
      choice("pattern", ["decoration", "pattern"], [["none", "patternNone"], ["confetti", "patternConfetti"], ["dots", "patternDots"], ["grid", "patternGrid"], ["waves", "patternWaves"], ["hills", "patternHills"], ["stripes", "patternStripes"], ["mountains", "patternMountains"]], "none"),
      toggle("headerBar", ["pages", "header", "show"], true),
      toggle("orgName", ["pages", "header", "organisation"], true),
      choice("logoSize", ["pages", "header", "logoSize"], sizes, "m"),
      { kind: "logo" },
      { kind: "header" },
    ],
  },
  {
    id: "components",
    label: "areaComponents",
    parts: [
      {
        id: "question",
        label: "partQuestion",
        goto: "single,multiple,rating",
        fields: [choice("size", ["components", "question", "size"], sizes, "m"), choice("font", ["components", "question", "font"], [["heading", "fontHeading"], ["body", "fontBody"]], "body"), toggle("numbered", ["components", "question", "numbered"], false)],
      },
      {
        id: "choices",
        label: "partChoices",
        goto: "single,multiple",
        fields: [
          choice("style", ["controls"], [["boxes", "styleBoxes"], ["pills", "stylePills"], ["underline", "styleUnderline"], ["tiles", "styleTiles"]], "boxes"),
          choice("columns", ["components", "choices", "columns"], [["one", "columnsOne"], ["two", "columnsTwo"], ["auto", "columnsAuto"]], "one"),
          choice("selected", ["components", "choices", "selected"], [["accent", "selAccent"], ["text", "selText"], ["fill", "selFill"]], "text"),
          choice("marker", ["components", "choices", "marker"], [["round", "markerRound"], ["check", "markerCheck"], ["none", "markerNone"]], "round"),
          choice("other", ["components", "choices", "other"], [["inside", "otherInside"], ["below", "otherBelow"]], "inside"),
        ],
      },
      {
        id: "buttons",
        label: "partButtons",
        goto: "single,multiple,rating",
        fields: [
          choice("style", ["buttons"], [["filled", "bFilled"], ["outline", "bOutline"], ["soft", "bSoft"]], "filled"),
          choice("width", ["components", "buttons", "width"], [["auto", "wAuto"], ["wide", "wWide"], ["full", "wFull"]], "auto"),
          choice("align", ["components", "buttons", "align"], [["split", "aSplit"], ["end", "aEnd"], ["centre", "aCentre"]], "split"),
        ],
      },
      { id: "fields", label: "partFields", goto: "short-text,long-text,number", fields: [choice("style", ["components", "textFields", "style"], [["boxes", "fBoxes"], ["underline", "fUnderline"], ["filled", "fFilled"]], "boxes")] },
      {
        id: "rating",
        label: "partRating",
        goto: "rating,nps,slider",
        fields: [
          choice("ratingSize", ["components", "rating", "size"], sizes, "m"),
          choice("ratingSelected", ["components", "rating", "selected"], [["accent", "selAccent"], ["text", "selText"], ["fill", "selFill"]], "fill"),
          choice("sliderTrack", ["components", "slider", "track"], [["thin", "trackThin"], ["thick", "trackThick"]], "thin"),
          choice("sliderThumb", ["components", "slider", "thumb"], [["round", "thumbRound"], ["square", "thumbSquare"]], "round"),
        ],
      },
      { id: "matrix", label: "partMatrix", goto: "matrix", fields: [toggle("stripes", ["components", "matrix", "stripes"], false), choice("columnHeads", ["components", "matrix", "header"], [["plain", "headPlain"], ["tinted", "headTinted"]], "plain")] },
      {
        id: "progress",
        label: "partProgress",
        goto: "single,multiple,rating",
        fields: [
          choice("style", ["progress"], [["ruler", "pRuler"], ["bar", "pBar"], ["steps", "pSteps"], ["none", "pNone"]], "ruler"),
          toggle("progressLabel", ["components", "progress", "label"], true),
          choice("position", ["components", "progress", "position"], [["top", "posTop"], ["bottom", "posBottom"]], "top"),
        ],
      },
      { id: "text-block", label: "partTextBlock", goto: "text-block", fields: [choice("style", ["components", "textBlock", "style"], [["plain", "tbPlain"], ["tinted", "tbTinted"]], "plain")] },
      { id: "messages", label: "partMessages", fields: [choice("style", ["components", "messages", "style"], [["box", "mBox"], ["bar", "mBar"]], "box")] },
    ],
  },
  {
    id: "pages",
    label: "areaPages",
    fields: [
      choice("welcomeLayout", ["pages", "welcome", "layout"], [["left", "plLeft"], ["centre", "plCentre"], ["band", "plBand"]], "left"),
      { kind: "questions-layout" },
      choice("column", ["pages", "questions", "width"], [["narrow", "widthNarrow"], ["normal", "widthNormal"], ["wide", "widthWide"]], "normal"),
      toggle("cards", ["pages", "questions", "cards"], false),
      choice("panel", ["pages", "questions", "panel"], [["solid", "panelSolid"], ["frosted", "panelFrosted"]], "solid"),
      choice("titlePlace", ["pages", "title"], [["panel", "titlePanel"], ["over-picture", "titleOver"]], "panel"),
      choice("endLayout", ["pages", "end", "layout"], [["left", "plLeft"], ["centre", "plCentre"], ["band", "plBand"]], "left"),
    ],
  },
  {
    id: "mail-pdf",
    label: "areaMailPdf",
    fields: [
      choice("emailHeader", ["email", "header"], [["logo", "eLogo"], ["band", "eBand"], ["plain", "ePlain"]], "logo"),
      choice("pdfHeader", ["pdf", "header"], [["full", "pdfFull"], ["plain", "pdfPlain"]], "full"),
      toggle("pdfNumbered", ["pdf", "numbered"], true),
    ],
  },
];

const tags: readonly (readonly [ThemeTag, LookMessageKey])[] = [
  ["calm", "tagCalm"],
  ["colourful", "tagColourful"],
  ["pictures", "tagPictures"],
  ["monochrome", "tagMonochrome"],
  ["print", "tagPrint"],
  ["dark", "tagDark"],
  ["kiosk", "tagKiosk"],
];

const pathsOf = (fields: readonly Field[]): LookPath[] => fields.flatMap((field) => ("path" in field ? [field.path] : field.kind === "logo" ? [["logo"]] : field.kind === "dark-colours" ? [["darkColours"], ["mode"]] : []));
const areaPaths = (area: Area): LookPath[] => [...pathsOf(area.fields ?? []), ...(area.parts ?? []).flatMap((part) => pathsOf(part.fields))];

export type SurveyLookProps = {
  api: LookApi;
  surveyId: string;
  locale: Locale;
  definition: SurveyDefinition;
  language: string;
  onLayout: (layout: "classic" | "one-at-a-time") => void;
  onHeaderImage: (asset: string | null) => void;
  onHeaderAlt: (language: string, value: string) => void;
  /** The one Preview panel, docked beside the settings; Look sends the look being edited to its frame (W26). */
  renderPreview: (frame: RefObject<HTMLIFrameElement | null>, onFrameLoad: () => void, saves: number) => ReactNode;
};

/**
 * Create → Look (W26): the survey's look definition. A gallery of looks to pick from, then "Make it yours" with every
 * setting in seven folded areas, beside the one Preview panel. Changes save at once, as the survey's own look.
 */
export function SurveyLook({ api, surveyId, locale, definition, language, onLayout, onHeaderImage, onHeaderAlt, renderPreview }: SurveyLookProps) {
  const t: LookText = useMemo(() => createTranslator(lookMessages, locale), [locale]);
  const [stored, setStored] = useState<StoredLook<ThemeTokens> | null>(null);
  const [tokens, setTokens] = useState<ThemeTokens | null>(null);
  const [base, setBase] = useState<{ tokens: ThemeTokens; name: string; lookBase: string | null } | null>(null);
  const [dirty, setDirty] = useState(0);
  const [mode, setMode] = useState<"gallery" | "editor">("gallery");
  const [status, setStatus] = useState<"idle" | "saving" | "saved" | "invalid" | "failed">("idle");
  const [saves, setSaves] = useState(0);
  const [themes, setThemes] = useState<readonly ThemeChoice[]>([]);
  const [notice, setNotice] = useState<string | null>(null);
  const frame = useRef<HTMLIFrameElement | null>(null);

  const load = () =>
    api.look(surveyId).then((look) => {
      setStored(look);
      setTokens(look.tokens);
      const preset = presetOf(look.lookBase);
      const theme = look.lookBase?.startsWith("theme:") ? look.lookBase.slice("theme:".length) : look.themeId;
      setBase({
        tokens: preset?.tokens ?? look.tokens,
        name: preset !== undefined ? (preset.name[locale] ?? preset.name.en ?? preset.id) : (themes.find((each) => each.id === theme)?.name ?? (look.own ? t("ownLook") : "")),
        lookBase: look.lookBase ?? (look.themeId === null ? null : `theme:${look.themeId}`),
      });
    }, () => undefined);

  useEffect(() => {
    void load();
    api.listThemes().then(setThemes, () => undefined);
    // Loads once per survey; load reads the latest state when called.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [api, surveyId]);

  // The look being edited goes to the preview at once; the server keeps it a moment later.
  const post = (message: object) => frame.current?.contentWindow?.postMessage(message, window.location.origin);

  useEffect(() => {
    if (tokens !== null) {
      post({ type: "askwerk:look", tokens });
    }
  }, [tokens]);

  // The preview says when it has loaded its survey; it then gets the look being edited (W26).
  useEffect(() => {
    const received = (event: MessageEvent) => {
      if (event.origin === window.location.origin && event.source === frame.current?.contentWindow && (event.data as { type?: string } | null)?.type === "askwerk:preview-ready" && tokens !== null) {
        post({ type: "askwerk:look", tokens });
      }
    };
    window.addEventListener("message", received);

    return () => window.removeEventListener("message", received);
  });

  useEffect(() => {
    if (dirty === 0 || tokens === null) {
      return undefined;
    }

    if (checkTheme(tokens).some((issue) => issue.severity === "error")) {
      setStatus("invalid");
      return undefined;
    }

    setStatus("saving");
    const timer = setTimeout(() => {
      api.setLook(surveyId, tokens, base?.lookBase ?? null).then(
        () => {
          setStatus("saved");
          setSaves((count) => count + 1);
          setStored((current) => (current === null ? current : { ...current, own: true, tokens }));
        },
        () => setStatus("failed"),
      );
    }, 500);

    return () => clearTimeout(timer);
    // Saves on edits only; base and api stay the same while editing.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dirty]);

  if (tokens === null || stored === null || base === null) {
    return <main className="sl" aria-label={t("look")} aria-busy="true" />;
  }

  const change = (next: ThemeTokens) => {
    setTokens(next);
    setDirty((count) => count + 1);
  };

  const pick = (presetId: string) => {
    const preset = themePresets.find((each) => each.id === presetId)!;
    setBase({ tokens: preset.tokens, name: preset.name[locale] ?? preset.id, lookBase: `preset:${preset.id}` });
    setTokens(preset.tokens);
    setStored((current) => (current === null ? current : { ...current, own: true, lookBase: `preset:${preset.id}`, tokens: preset.tokens }));
    setDirty((count) => count + 1);
    setMode("editor");
  };

  const pickTheme = (themeId: string | null) => {
    void api.setTheme(surveyId, themeId).then(() => {
      setSaves((count) => count + 1);
      void load();
      setMode("editor");
    });
  };

  const sample = firstChoiceQuestion(definition, language) ?? { question: t("look"), choices: [] };

  if (mode === "gallery") {
    return (
      <Gallery
        t={t}
        locale={locale}
        stored={stored}
        themes={themes}
        sample={sample}
        onPick={pick}
        onTheme={pickTheme}
        onCustomise={() => setMode("editor")}
      />
    );
  }

  return (
    <main className="sl sl--editor" aria-label={t("look")}>
      <Areas
        t={t}
        tokens={tokens}
        base={base}
        definition={definition}
        language={language}
        api={api}
        onChange={change}
        onLayout={onLayout}
        onHeaderImage={onHeaderImage}
        onHeaderAlt={onHeaderAlt}
        onGoto={(target) => post({ type: "askwerk:goto", target })}
        onAllLooks={() => setMode("gallery")}
        onSaveForOrg={(name) =>
          api.saveTheme(name, tokens).then(
            () => {
              setNotice(t("savedForOrg", { name }));
              api.listThemes().then(setThemes, () => undefined);
            },
            (error: unknown) => setNotice(error instanceof ApiError && error.status === 403 ? t("notAllowed") : t("failed")),
          )
        }
      />
      <Quick t={t} tokens={tokens} status={status} notice={notice} onChange={change} />
      {renderPreview(frame, () => post({ type: "askwerk:look", tokens }), saves)}
    </main>
  );
}

/** The survey's first question with options, in the editing language, to show the looks with. */
function firstChoiceQuestion(definition: SurveyDefinition, language: string): { question: string; choices: string[] } | undefined {
  const text = (value: Record<string, string> | undefined) => value?.[language] || value?.[definition.defaultLanguage] || "";
  const question = definition.pages.flatMap((page) => page.questions).find((each) => (each.type === "single" || each.type === "multiple") && (each.options?.length ?? 0) > 0);

  return question === undefined ? undefined : { question: text(question.text), choices: (question.options ?? []).map((option) => text(option.label)) };
}

function Gallery({
  t,
  locale,
  stored,
  themes,
  sample,
  onPick,
  onTheme,
  onCustomise,
}: {
  t: LookText;
  locale: Locale;
  stored: StoredLook<ThemeTokens>;
  themes: readonly ThemeChoice[];
  sample: { question: string; choices: string[] };
  onPick: (presetId: string) => void;
  onTheme: (themeId: string | null) => void;
  onCustomise: () => void;
}) {
  const [filter, setFilter] = useState<ThemeTag | "all">("all");
  const shown = themePresets.filter((preset) => filter === "all" || preset.tags?.includes(filter));
  const fallbackChoices = sample.choices.length > 0 ? sample.choices : ["A", "B", "C"];
  const defaultTheme = themes.find((theme) => theme.isDefault);
  const usedTheme = !stored.own ? (themes.find((theme) => theme.id === stored.themeId) ?? defaultTheme) : undefined;

  return (
    <main className="sl sl--gallery" aria-label={t("look")}>
      <div className="sl-gallery__head">
        <div>
          <h2 className="sl-title">{t("look")}</h2>
          <p className="sl-lead">{t("lookLead")}</p>
        </div>
        <span className="sl-grow" />
        <Menu
          label={t("orgLooks")}
          trigger={(props) => (
            <button type="button" className="designer-button designer-button--quiet" {...props}>
              {usedTheme !== undefined ? t("inUseOrg", { name: usedTheme.name }) : t("orgLooks")} <span aria-hidden="true">▾</span>
            </button>
          )}
        >
          <MenuItem onSelect={() => onTheme(null)}>{t("orgDefault", { name: defaultTheme?.name ?? "Werkstatt" })}</MenuItem>
          {themes.map((theme) => (
            <MenuItem key={theme.id} onSelect={() => onTheme(theme.id)}>
              {theme.name}
            </MenuItem>
          ))}
        </Menu>
        <button type="button" className="designer-button" onClick={onCustomise}>
          {t("customise")}
        </button>
      </div>
      <div className="sl-filters" role="group" aria-label={t("filters")}>
        <button type="button" className="sl-chip" aria-pressed={filter === "all"} onClick={() => setFilter("all")}>
          {t("filterAll", { count: themePresets.length })}
        </button>
        {tags.map(([tag, label]) => (
          <button key={tag} type="button" className="sl-chip" aria-pressed={filter === tag} onClick={() => setFilter(tag)}>
            {t(label)}
          </button>
        ))}
        <span className="sl-grow" />
        <span className="sl-note">{t("shownWith")}</span>
      </div>
      <ul className="sl-grid">
        {shown.map((preset) => {
          const name = preset.name[locale] ?? preset.name.en ?? preset.id;
          const inUse = stored.own && stored.lookBase === `preset:${preset.id}`;

          return (
            <li key={preset.id} className={inUse ? "sl-card sl-card--in-use" : "sl-card"}>
              <button type="button" className="sl-card__button" aria-label={t("useLook", { name })} aria-current={inUse ? "true" : undefined} onClick={() => onPick(preset.id)}>
                {inUse && <span className="sl-card__badge">{t("inUse")}</span>}
                <span className="sl-card__thumb">
                  <LookThumb tokens={preset.tokens} question={sample.question} choices={fallbackChoices} />
                </span>
                <span className="sl-card__body">
                  <span>
                    <b className="sl-card__name">{name}</b>
                    <span className="sl-card__desc">{preset.description?.[locale] ?? preset.description?.en}</span>
                  </span>
                  <span className="sl-media" title={t("rendersIn")}>
                    <span>web</span>
                    <span>mail</span>
                    <span>pdf</span>
                  </span>
                </span>
              </button>
            </li>
          );
        })}
      </ul>
    </main>
  );
}

type AreasProps = {
  t: LookText;
  tokens: ThemeTokens;
  base: { tokens: ThemeTokens; name: string; lookBase: string | null };
  definition: SurveyDefinition;
  language: string;
  api: LookApi;
  onChange: (tokens: ThemeTokens) => void;
  onLayout: (layout: "classic" | "one-at-a-time") => void;
  onHeaderImage: (asset: string | null) => void;
  onHeaderAlt: (language: string, value: string) => void;
  onGoto: (target: string) => void;
  onAllLooks: () => void;
  onSaveForOrg: (name: string) => Promise<void>;
};

function Areas({ t, tokens, base, definition, language, api, onChange, onLayout, onHeaderImage, onHeaderAlt, onGoto, onAllLooks, onSaveForOrg }: AreasProps) {
  const [open, setOpen] = useState<string | null>(null);
  const [openPart, setOpenPart] = useState<string | null>(null);
  const [naming, setNaming] = useState(false);
  const changes = changesBetween(base.tokens, tokens);
  const id = useId();

  const label = (field: Field) => {
    if (field.kind === "choice") {
      const value = String(getAt(tokens, field.path) ?? field.fallback);
      const option = field.options.find(([optionValue]) => optionValue === value);

      return option === undefined ? value.replace(/^IBM Plex /, "") : t(option[1]);
    }

    return field.kind === "switch" ? `${t(field.label)} ${(getAt(tokens, field.path) as boolean | undefined) ?? field.fallback ? t("on") : t("off")}` : "";
  };
  const summary = (area: Area) =>
    area.id === "colours"
      ? `${t("cAccent")} ${tokens.colours.accent}`
      : area.parts !== undefined
        ? t("partsCount", { count: area.parts.length })
        : (area.fields ?? []).filter((field) => field.kind === "choice").slice(0, 2).map(label).join(" · ");

  const fieldsOf = (fields: readonly Field[]) => (
    <div className="sl-fields">
      {fields.map((field, index) => (
        <FieldControl key={index} t={t} field={field} tokens={tokens} definition={definition} language={language} api={api} onChange={onChange} onLayout={onLayout} onHeaderImage={onHeaderImage} onHeaderAlt={onHeaderAlt} />
      ))}
    </div>
  );

  return (
    <aside className="sl-side" aria-label={t("areas")}>
      <div className="sl-side__head">
        <span className="sl-swatch" aria-hidden="true" style={{ background: `linear-gradient(135deg, ${tokens.colours.accent} 0 40%, ${tokens.colours.background} 40% 70%, ${tokens.colours.text} 70%)` }} />
        <div className="sl-side__name">
          <b>{base.name || t("ownLook")}</b>
          {changes > 0 && <span className="sl-note"> · {changes === 1 ? t("changedOne") : t("changed", { count: changes })}</span>}
          <span className="sl-note sl-side__scope">
            {t("forThisSurvey")} ·{" "}
            <button type="button" className="sl-link" onClick={() => setNaming(true)}>
              {t("saveForOrg")}
            </button>
          </span>
        </div>
        <button type="button" className="designer-button designer-button--quiet" onClick={onAllLooks}>
          {t("allLooks")}
        </button>
      </div>
      <div className="sl-areas">
        {areas.map((area) => {
          const isOpen = open === area.id;

          return (
            <div key={area.id} className={isOpen ? "sl-area sl-area--open" : "sl-area"}>
              <button
                type="button"
                className="sl-area__row"
                aria-expanded={isOpen}
                aria-controls={`${id}-${area.id}`}
                onClick={() => {
                  setOpen(isOpen ? null : area.id);

                  if (!isOpen && area.goto !== undefined) {
                    onGoto(area.goto);
                  }
                }}
              >
                <span className="sl-chev" aria-hidden="true">
                  {isOpen ? "▾" : "▸"}
                </span>
                <b>{t(area.label)}</b>
                <span className="sl-area__sum">{summary(area)}</span>
              </button>
              {isOpen && (
                <div id={`${id}-${area.id}`} className="sl-area__body">
                  {area.fields !== undefined && fieldsOf(area.fields)}
                  {area.parts?.map((part) => {
                    const partOpen = openPart === part.id;

                    return (
                      <div key={part.id} className="sl-part">
                        <button
                          type="button"
                          className="sl-area__row sl-part__row"
                          aria-expanded={partOpen}
                          onClick={() => {
                            setOpenPart(partOpen ? null : part.id);

                            if (!partOpen && part.goto !== undefined) {
                              onGoto(part.goto);
                            }
                          }}
                        >
                          <span className="sl-chev" aria-hidden="true">
                            {partOpen ? "▾" : "▸"}
                          </span>
                          {t(part.label)}
                          <span className="sl-area__sum">{part.fields.filter((field) => field.kind === "choice").slice(0, 1).map(label).join("")}</span>
                        </button>
                        {partOpen && (
                          <>
                            {fieldsOf(part.fields)}
                            <button type="button" className="sl-link" onClick={() => onChange(resetPaths(tokens, base.tokens, pathsOf(part.fields)))}>
                              {t("backTo", { name: base.name || t("ownLook") })}
                            </button>
                          </>
                        )}
                      </div>
                    );
                  })}
                  {area.parts === undefined && (
                    <button type="button" className="sl-link" onClick={() => onChange(resetPaths(tokens, base.tokens, areaPaths(area)))}>
                      {t("backTo", { name: base.name || t("ownLook") })}
                    </button>
                  )}
                </div>
              )}
            </div>
          );
        })}
      </div>
      {naming && <NameDialog t={t} initial={base.name} onCancel={() => setNaming(false)} onSave={(name) => onSaveForOrg(name).then(() => setNaming(false))} />}
    </aside>
  );
}

function NameDialog({ t, initial, onCancel, onSave }: { t: LookText; initial: string; onCancel: () => void; onSave: (name: string) => Promise<void> }) {
  const [name, setName] = useState(initial);
  const id = useId();

  return (
    <Dialog title={t("saveForOrgTitle")} onClose={onCancel}>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          void onSave(name.trim());
        }}
      >
        <label className="designer-label" htmlFor={`${id}-name`}>
          {t("lookName")}
        </label>
        <input id={`${id}-name`} className="designer-input sl-name" maxLength={80} value={name} onChange={(event) => setName(event.target.value)} />
        <DialogActions>
          <button type="button" className="designer-button designer-button--quiet" onClick={onCancel}>
            {t("cancel")}
          </button>
          <button type="submit" className="designer-button designer-button--primary" disabled={name.trim() === ""}>
            {t("save")}
          </button>
        </DialogActions>
      </form>
    </Dialog>
  );
}

type FieldProps = Omit<AreasProps, "base" | "onGoto" | "onAllLooks" | "onSaveForOrg"> & { field: Field };

function FieldControl({ t, field, tokens, definition, language, api, onChange, onLayout, onHeaderImage, onHeaderAlt }: FieldProps) {
  const id = useId();
  const file = useRef<HTMLInputElement>(null);

  switch (field.kind) {
    case "choice": {
      const value = String(getAt(tokens, field.path) ?? field.fallback);

      return (
        <Segment label={t(field.label)} value={value} options={field.options.map(([optionValue, optionLabel]) => [optionValue, t(optionLabel)] as const)} onChange={(next) => onChange(setAt(tokens, field.path, next))} />
      );
    }
    case "switch":
      return <Switch label={t(field.label)} checked={(getAt(tokens, field.path) as boolean | undefined) ?? field.fallback} onChange={(on) => onChange(setAt(tokens, field.path, on))} />;
    case "colour":
      return <ColourField t={t} label={t(field.label)} value={(getAt(tokens, field.path) as string | undefined) ?? ""} onChange={(next) => onChange(setAt(tokens, field.path, next === "" ? undefined : next))} />;
    case "dark-colours":
      return tokens.darkColours === undefined ? null : (
        <div className="sl-dark">
          <span className="designer-label">{t("darkColours")}</span>
          {(["accent", "onAccent", "background", "surface", "text", "muted", "line"] as const).map((name) => (
            <ColourField
              key={name}
              t={t}
              label={t(`c${name[0]!.toUpperCase()}${name.slice(1)}` as LookMessageKey)}
              value={tokens.darkColours![name]}
              onChange={(next) => onChange(setAt(tokens, ["darkColours", name], next))}
            />
          ))}
        </div>
      );
    case "questions-layout":
      return (
        <Segment
          label={t("questionsLayout")}
          value={definition.layout ?? "classic"}
          options={[
            ["classic", t("qlClassic")],
            ["one-at-a-time", t("qlOne")],
          ]}
          onChange={(next) => onLayout(next as "classic" | "one-at-a-time")}
        />
      );
    case "logo":
      return (
        <div className="sl-upload">
          <span className="designer-label">{t("logo")}</span>
          {tokens.logo !== undefined && <img className="sl-upload__logo" src={api.assetUrl(tokens.logo.asset, 240)} alt="" />}
          <span className="designer-row">
            <button type="button" className="designer-button designer-button--quiet" onClick={() => file.current?.click()}>
              {t("uploadLogo")}
            </button>
            {tokens.logo !== undefined && (
              <button type="button" className="designer-button designer-button--quiet" onClick={() => onChange(setAt(tokens, ["logo"], undefined))}>
                {t("removeLogo")}
              </button>
            )}
          </span>
          <input
            ref={file}
            type="file"
            className="designer-visually-hidden"
            aria-label={t("logoFile")}
            accept="image/png,image/jpeg,image/webp,image/svg+xml"
            tabIndex={-1}
            onChange={(event) => {
              const chosen = event.target.files?.[0];
              event.target.value = "";

              if (chosen !== undefined) {
                void api.uploadImage(chosen).then((asset) => onChange(setAt(tokens, ["logo"], { asset })));
              }
            }}
          />
        </div>
      );
    case "header": {
      const header = definition.headerImage;

      return (
        <div className="sl-upload">
          <span className="designer-label">{t("surveyHeader")}</span>
          {header !== undefined && <img className="designer-header-image" src={api.assetUrl(header.asset, 480)} alt={header.alt?.[language] ?? header.alt?.[definition.defaultLanguage] ?? ""} />}
          <span className="designer-row">
            <button type="button" className="designer-button designer-button--quiet" onClick={() => file.current?.click()}>
              {t("uploadImage")}
            </button>
            {header !== undefined && (
              <button type="button" className="designer-button designer-button--quiet" onClick={() => onHeaderImage(null)}>
                {t("removeHeader")}
              </button>
            )}
          </span>
          <input
            ref={file}
            type="file"
            className="designer-visually-hidden"
            aria-label={t("headerFile")}
            accept="image/png,image/jpeg,image/webp"
            tabIndex={-1}
            onChange={(event) => {
              const chosen = event.target.files?.[0];
              event.target.value = "";

              if (chosen !== undefined) {
                void api.uploadImage(chosen).then(onHeaderImage);
              }
            }}
          />
          {header !== undefined && (
            <>
              <label className="designer-label" htmlFor={`${id}-alt`}>
                {t("headerAlt", { language: language.toUpperCase() })}
              </label>
              <input id={`${id}-alt`} className="designer-input" value={header.alt?.[language] ?? ""} onChange={(event) => onHeaderAlt(language, event.target.value)} />
            </>
          )}
          <p className="designer-note">{t("headerNote")}</p>
        </div>
      );
    }
  }
}

function Segment({ label, value, options, onChange }: { label: string; value: string; options: readonly (readonly [string, string])[]; onChange: (value: string) => void }) {
  return (
    <div className="sl-field">
      <span className="sl-field__label">{label}</span>
      <span className="designer-segment sl-segment" role="group" aria-label={label}>
        {options.map(([optionValue, optionLabel]) => (
          <button key={optionValue} type="button" aria-pressed={optionValue === value} onClick={() => onChange(optionValue)}>
            {optionLabel}
          </button>
        ))}
      </span>
    </div>
  );
}

function ColourField({ t, label, value, onChange }: { t: LookText; label: string; value: string; onChange: (value: string) => void }) {
  const id = useId();
  const [text, setText] = useState(value);

  useEffect(() => setText(value), [value]);

  return (
    <div className="sl-field">
      <label className="sl-field__label" htmlFor={`${id}-hex`}>
        {label}
      </label>
      <span className="sl-colour">
        <input type="color" aria-label={`${label} · ${t("accent")}`} value={/^#[0-9a-f]{6}$/i.test(value) ? value : "#000000"} onChange={(event) => onChange(event.target.value)} />
        <input
          id={`${id}-hex`}
          className="designer-input sl-colour__hex"
          value={text}
          spellCheck={false}
          onChange={(event) => {
            setText(event.target.value);

            if (/^#[0-9a-f]{6}$/i.test(event.target.value) || event.target.value === "") {
              onChange(event.target.value);
            }
          }}
        />
      </span>
    </div>
  );
}

/** Quick changes and the checks (W26 B): accent, fonts, corners, density, mode; readable or not, saved or not. */
function Quick({ t, tokens, status, notice, onChange }: { t: LookText; tokens: ThemeTokens; status: "idle" | "saving" | "saved" | "invalid" | "failed"; notice: string | null; onChange: (tokens: ThemeTokens) => void }) {
  const issues = checkTheme(tokens);
  const errors = issues.filter((issue) => issue.severity === "error");
  const hexRgb = (hex: string) => [1, 3, 5].map((start) => parseInt(hex.slice(start, start + 2), 16) / 255) as unknown as [number, number, number];
  const ratio = (a: string, b: string) => (Math.floor(contrastRatio(hexRgb(a), hexRgb(b)) * 10) / 10).toFixed(1);
  const what = (path: string | undefined) => (path ?? "").split("/").at(-1) ?? "";

  return (
    <section className="sl-main" aria-label={t("makeYours")}>
      <h2 className="sl-title">{t("makeYours")}</h2>
      <p className="sl-lead">{t("makeYoursLead")}</p>
      <h3 className="sl-label">{t("quick")}</h3>
      <div className="sl-fields">
        <ColourField t={t} label={t("accent")} value={tokens.colours.accent} onChange={(accent) => accent !== "" && onChange(setAt(tokens, ["colours", "accent"], accent))} />
        <Segment
          label={t("fonts")}
          value={tokens.fonts.heading.family}
          options={families.map(([family, key]) => [family, t(key)] as const)}
          onChange={(family) => onChange(setAt(tokens, ["fonts", "heading"], { family }))}
        />
        <Segment
          label={t("corners")}
          value={tokens.corners}
          options={[
            ["sharp", t("cornersSharp")],
            ["rounded", t("cornersRounded")],
            ["pill", t("cornersPill")],
          ]}
          onChange={(corners) => onChange(setAt(tokens, ["corners"], corners))}
        />
        <Segment
          label={t("density")}
          value={tokens.spacing}
          options={[
            ["compact", t("spacingCompact")],
            ["normal", t("spacingNormal")],
            ["airy", t("spacingAiry")],
          ]}
          onChange={(spacing) => onChange(setAt(tokens, ["spacing"], spacing))}
        />
        <Segment
          label={t("mode")}
          value={tokens.mode ?? "light"}
          options={[
            ["light", t("modeLight")],
            ["dark", t("modeDark")],
            ["auto", t("modeAuto")],
          ]}
          onChange={(mode) => {
            const withMode = setAt(tokens, ["mode"], mode === "light" ? undefined : mode);
            onChange(mode !== "light" && withMode.darkColours === undefined ? setAt(withMode, ["darkColours"], darkColoursFor(tokens.colours)) : withMode);
          }}
        />
      </div>
      <div className="sl-checks" role="status">
        <h3 className="sl-label">{t("checks")}</h3>
        {errors.length === 0 ? (
          <p>
            <span className="sl-ok">✓</span> {t("checkReadable", { text: ratio(tokens.colours.text, tokens.colours.surface), buttons: ratio(tokens.colours.onAccent, tokens.colours.accent) })}
          </p>
        ) : (
          errors.map((issue: ThemeIssue, index) => (
            <p key={index} className="sl-bad">
              !{" "}
              {issue.code === "contrast-too-low"
                ? t("checkContrast", { what: what(issue.path), against: what(issue.against), ratio: issue.ratio ?? 0 })
                : issue.code === "dark-colours-missing"
                  ? t("checkDarkMissing")
                  : issue.code === "colour-invalid"
                    ? t("checkInvalid", { what: what(issue.path) })
                    : t("checkOther", { code: issue.code, what: issue.path })}
            </p>
          ))
        )}
        {!["none", "mountains"].includes(tokens.decoration?.pattern ?? "none") && <p className="sl-note">{t("pdfPattern")}</p>}
        <p className="sl-note">{status === "saving" ? t("saving") : status === "saved" ? t("saved") : status === "invalid" ? t("notSaved") : status === "failed" ? t("failed") : ""}</p>
        {notice !== null && <p className="sl-note">{notice}</p>}
      </div>
    </section>
  );
}
