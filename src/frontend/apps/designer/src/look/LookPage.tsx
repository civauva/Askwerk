// SPDX-License-Identifier: AGPL-3.0-only
import "./look.css";
import { ApiError, type StoredTheme, type ThemesClient } from "@askwerk/app-client";
import { checkTheme, contrastRatio, createTranslator, themePresets, type Locale, type ThemeIssue, type ThemeTokens } from "@askwerk/schema";
import { ComboBox } from "@askwerk/ui";
import { useEffect, useId, useMemo, useRef, useState, type ChangeEvent, type CSSProperties, type ReactNode } from "react";
import { ZoomBox } from "../designer/ZoomBox";
import { lookMessages, type LookMessageKey } from "./messages";

type T = (key: LookMessageKey, values?: Record<string, string | number>) => string;
type Theme = StoredTheme<ThemeTokens>;
type Colours = ThemeTokens["colours"];

type LookPageProps = {
  client: ThemesClient;
  locale: Locale;
  /** Pause after the last change before saving; tests use 0. */
  saveDelayMs?: number;
};

/** Self-hosted font families every theme can use; uploaded fonts come on top (decisions.md, AWK-17). */
const fontFamilies = ["IBM Plex Sans", "IBM Plex Sans Condensed", "IBM Plex Serif", "IBM Plex Mono"];

const dark = "#1a1917";
const white = "#ffffff";

/**
 * Organisation → Look of surveys (AWK-17, W10 A), stupid simple: pick a preset, set name, logo and accent colour;
 * everything else waits under More. Changes save by themselves once the theme checks pass.
 */
export function LookPage({ client, locale, saveDelayMs = 600 }: LookPageProps) {
  const t: T = useMemo(() => createTranslator(lookMessages, locale), [locale]);
  const [themes, setThemes] = useState<Theme[] | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [choosing, setChoosing] = useState(false);

  useEffect(() => {
    client.list<ThemeTokens>().then((loaded) => {
      setThemes(loaded);
      setSelectedId(loaded[0]?.id ?? null);
    }, () => setThemes([]));
  }, [client]);

  async function startFrom(presetId: string) {
    const preset = themePresets.find((each) => each.id === presetId)!;
    const base = preset.name[locale] ?? preset.name.en ?? preset.id;
    // A second theme from the same preset gets a number, so the list never shows two alike.
    const taken = new Set(themes?.map((theme) => theme.name));
    let name = base;

    for (let number = 2; taken.has(name); number += 1) {
      name = `${base} ${number}`;
    }

    const id = await client.create(name, preset.tokens);
    const loaded = await client.list<ThemeTokens>();
    setThemes(loaded);
    setSelectedId(id);
    setChoosing(false);
  }

  const selected = themes?.find((theme) => theme.id === selectedId);

  return (
    <main className="look" lang={locale}>
      <h1>{t("title")}</h1>
      <p className="look-lead">{t("lead")}</p>
      {themes === null ? (
        <p role="status" aria-busy="true">
          {t("loading")}
        </p>
      ) : (
        <div className="look-work">
          {themes.length > 0 && (
            <nav className="look-list" aria-label={t("themes")}>
              {themes.map((theme) => (
                <button
                  key={theme.id}
                  type="button"
                  className="look-list__theme"
                  aria-current={theme.id === selectedId && !choosing ? "true" : undefined}
                  onClick={() => {
                    setSelectedId(theme.id);
                    setChoosing(false);
                  }}
                >
                  <i className="look-swatch" style={{ background: theme.tokens.colours.accent }} aria-hidden="true" />
                  <span>{theme.name}</span>
                  {theme.isDefault && (
                    <>
                      {" "}
                      <span className="look-badge">{t("default")}</span>
                    </>
                  )}
                </button>
              ))}
              <button type="button" className="look-list__new" onClick={() => setChoosing(true)}>
                {t("newTheme")}
              </button>
            </nav>
          )}
          {themes.length === 0 || choosing ? (
            <Presets t={t} locale={locale} empty={themes.length === 0} onPick={(id) => void startFrom(id)} onCancel={themes.length > 0 ? () => setChoosing(false) : undefined} />
          ) : (
            selected !== undefined && (
              <Editor
                key={selected.id}
                t={t}
                locale={locale}
                client={client}
                theme={selected}
                saveDelayMs={saveDelayMs}
                onSaved={(name, tokens) => setThemes((current) => current?.map((theme) => (theme.id === selected.id ? { ...theme, name, tokens } : theme)) ?? null)}
                onMadeDefault={() => setThemes((current) => current?.map((theme) => ({ ...theme, isDefault: theme.id === selected.id })) ?? null)}
                onDeleted={() => {
                  const rest = themes.filter((theme) => theme.id !== selected.id);
                  setThemes(rest);
                  setSelectedId(rest[0]?.id ?? null);
                }}
              />
            )
          )}
        </div>
      )}
    </main>
  );
}

function Presets({ t, locale, empty, onPick, onCancel }: { t: T; locale: Locale; empty: boolean; onPick: (id: string) => void; onCancel: (() => void) | undefined }) {
  return (
    <section className="look-presets" aria-labelledby="look-presets-title">
      {empty && <p className="look-empty">{t("noTheme")}</p>}
      <h2 id="look-presets-title">{t("startFrom")}</h2>
      <div className="look-presets__grid">
        {themePresets.map((preset) => {
          const name = preset.name[locale] ?? preset.name.en ?? preset.id;

          return (
            <button key={preset.id} type="button" className="look-presets__card" aria-label={t("startFromPreset", { name })} onClick={() => onPick(preset.id)}>
              <Preview t={t} tokens={preset.tokens} logoUrl={undefined} small />
              <span>{name}</span>
            </button>
          );
        })}
      </div>
      {onCancel !== undefined && (
        <button type="button" className="look-button" onClick={onCancel}>
          {t("cancel")}
        </button>
      )}
    </section>
  );
}

type EditorProps = {
  t: T;
  locale: Locale;
  client: ThemesClient;
  theme: Theme;
  saveDelayMs: number;
  onSaved: (name: string, tokens: ThemeTokens) => void;
  onMadeDefault: () => void;
  onDeleted: () => void;
};

function Editor({ t, locale, client, theme, saveDelayMs, onSaved, onMadeDefault, onDeleted }: EditorProps) {
  const [name, setName] = useState(theme.name);
  const [tokens, setTokens] = useState(theme.tokens);
  const [more, setMore] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [status, setStatus] = useState<"saved" | "saving" | "failed">("saved");
  const [uploadError, setUploadError] = useState<string | null>(null);
  const first = useRef(true);
  const errors = checkTheme(tokens).filter((issue) => issue.severity === "error");
  const blocked = errors.length > 0 || name.trim() === "";
  const latestSaved = useRef(onSaved);
  latestSaved.current = onSaved;

  useEffect(() => {
    if (first.current) {
      first.current = false;
      return undefined;
    }

    if (blocked) {
      return undefined;
    }

    setStatus("saving");
    const timer = setTimeout(() => {
      client.update(theme.id, name.trim(), tokens).then(
        () => {
          setStatus("saved");
          latestSaved.current(name.trim(), tokens);
        },
        () => setStatus("failed"),
      );
    }, saveDelayMs);

    return () => clearTimeout(timer);
  }, [client, theme.id, name, tokens, blocked, saveDelayMs]);

  const setColours = (change: Partial<Colours>) => setTokens((current) => ({ ...current, colours: { ...current.colours, ...change } }));
  const set = <K extends keyof ThemeTokens>(key: K, value: ThemeTokens[K]) => setTokens((current) => ({ ...current, [key]: value }));

  async function upload(file: File | undefined, kind: "image" | "font", use: (assetId: string, file: File) => void) {
    if (file === undefined) {
      return;
    }

    setUploadError(null);

    try {
      const stored = await client.upload(file, kind);
      use(stored.id, file);
    } catch (error) {
      const code = error instanceof ApiError ? (error.problem.errors as Record<string, string[]> | undefined)?.file?.[0] : undefined;
      setUploadError(t("uploadFailed", { reason: code === "file-too-large" ? t("fileTooLarge") : t("fileTypeNotAllowed") }));
    }
  }

  const logoUrl = tokens.logo?.asset !== undefined ? client.assetUrl(tokens.logo.asset) : undefined;

  return (
    <>
      <section className="look-editor" aria-label={t("theme")}>
        <div className="look-field">
          <label htmlFor="look-name">{t("name")}</label>
          <input id="look-name" className="look-input" value={name} onChange={(event) => setName(event.target.value)} />
        </div>
        <div className="look-field">
          <span className="look-label">{t("logo")}</span>
          <div className="look-row">
            {logoUrl !== undefined && <img className="look-logo" src={logoUrl} alt="" />}
            <FileButton label={t("uploadLogo")} fileLabel={t("logoFile")} accept="image/png,image/jpeg,image/webp" onFile={(file) => void upload(file, "image", (asset) => set("logo", { asset }))} />
            {tokens.logo !== undefined && (
              <button type="button" className="look-link" onClick={() => setTokens(({ logo: _, ...rest }) => rest as ThemeTokens)}>
                {t("removeLogo")}
              </button>
            )}
          </div>
        </div>
        <ColourField
          label={t("accent")}
          hexLabel={t("accentHex")}
          value={tokens.colours.accent}
          // Button text follows the accent, so a new colour never makes buttons unreadable.
          onChange={(accent) => setColours({ accent, onAccent: readableOn(accent) })}
        />
        <p className="look-note">{t("accentNote")}</p>
        {uploadError !== null && (
          <p className="look-error" role="status">
            {uploadError}
          </p>
        )}
        {errors.length > 0 && (
          <div className="look-error" role="alert">
            {errors.map((issue) => (
              <p key={`${issue.code}-${issue.path}-${issue.against ?? ""}`}>{describe(t, locale, issue)}</p>
            ))}
            <p>
              <b>{t("notSaved")}</b>
            </p>
          </div>
        )}
        <button type="button" className="look-more" aria-expanded={more} onClick={() => setMore((current) => !current)}>
          {more ? t("less") : t("more")} <span aria-hidden="true">{more ? "▴" : "▾"}</span>
        </button>
        {more && <More t={t} tokens={tokens} setColours={setColours} set={set} upload={upload} />}
        <div className="look-actions">
          <span className="look-status" role="status">
            {!blocked && (status === "saved" ? t("saved") : status === "saving" ? t("saving") : t("saveFailed"))}
          </span>
          {theme.isDefault ? (
            <p className="look-note">{t("isDefault")}</p>
          ) : (
            <>
              <button type="button" className="look-button" onClick={() => void client.makeDefault(theme.id).then(onMadeDefault)}>
                {t("makeDefault")}
              </button>
              <button type="button" className="look-link" onClick={() => setConfirmDelete(true)}>
                {t("delete")}
              </button>
            </>
          )}
        </div>
        {confirmDelete && (
          <div className="look-confirm">
            <p>{t("deleteQuestion", { name: theme.name })}</p>
            <button type="button" className="look-button look-button--primary" onClick={() => void client.remove(theme.id).then(onDeleted)}>
              {t("deleteTheme")}
            </button>
            <button type="button" className="look-button" onClick={() => setConfirmDelete(false)}>
              {t("cancel")}
            </button>
          </div>
        )}
      </section>
      <section className="look-preview-region" aria-label={t("preview")}>
        <ZoomBox texts={{ zoom: t("zoom"), zoomIn: t("zoomIn"), zoomOut: t("zoomOut"), fit: t("fit") }}>
          <Preview t={t} tokens={tokens} logoUrl={logoUrl} headerUrl={tokens.headerImage?.asset !== undefined ? client.assetUrl(tokens.headerImage.asset) : undefined} />
        </ZoomBox>
      </section>
    </>
  );
}

type MoreProps = {
  t: T;
  tokens: ThemeTokens;
  setColours: (change: Partial<Colours>) => void;
  set: <K extends keyof ThemeTokens>(key: K, value: ThemeTokens[K]) => void;
  upload: (file: File | undefined, kind: "image" | "font", use: (assetId: string, file: File) => void) => Promise<void>;
};

/** Everything beyond name, logo and accent colour (W10 A, F2, F3). */
function More({ t, tokens, setColours, set, upload }: MoreProps) {
  const colours: [keyof Colours, LookMessageKey][] = [
    ["background", "colourBackground"],
    ["surface", "colourSurface"],
    ["text", "colourText"],
    ["muted", "colourMuted"],
    ["line", "colourLine"],
    ["onAccent", "colourOnAccent"],
  ];
  const uploadedFonts = [tokens.fonts.heading, tokens.fonts.body].filter((font) => font.asset !== undefined);
  const families = [...fontFamilies, ...uploadedFonts.map((font) => font.family).filter((family) => !fontFamilies.includes(family))];
  const background = tokens.background;
  const overlay = background?.overlayOpacity ?? 0.8;

  return (
    <div className="look-more-panel">
      <fieldset className="look-group">
        <legend>{t("colours")}</legend>
        {colours.map(([key, label]) => (
          <ColourField key={key} label={t(label)} hexLabel={t("hex", { name: t(label) })} value={tokens.colours[key]} onChange={(value) => setColours({ [key]: value })} />
        ))}
      </fieldset>
      <fieldset className="look-group">
        <legend>{t("fonts")}</legend>
        {(["heading", "body"] as const).map((which) => (
          <FontField
            key={which}
            t={t}
            label={t(which === "heading" ? "fontHeading" : "fontBody")}
            families={families}
            value={tokens.fonts[which].family}
            onChange={(family) => {
              const uploaded = uploadedFonts.find((font) => font.family === family);
              set("fonts", { ...tokens.fonts, [which]: uploaded ?? { family } });
            }}
          />
        ))}
        <FileButton
          label={t("uploadFont")}
          fileLabel={t("fontFile")}
          accept=".woff2,.woff,.ttf,.otf"
          onFile={(file) =>
            void upload(file, "font", (asset, chosen) => set("fonts", { ...tokens.fonts, heading: { family: chosen.name.replace(/\.[^.]+$/, ""), asset } }))
          }
        />
        <p className="look-note">{t("fontLicence")}</p>
      </fieldset>
      <Choice t={t} legend="layout" value={tokens.layout} options={[["card", "layoutCard"], ["header-image", "layoutHeader"], ["split-image", "layoutSplit"], ["minimal", "layoutMinimal"]]} onChange={(value) => set("layout", value)} />
      {(tokens.layout === "header-image" || tokens.layout === "split-image") && (
        <div className="look-field">
          <span className="look-label">{t("headerImage")}</span>
          <FileButton label={t("uploadImage")} fileLabel={t("headerFile")} accept="image/png,image/jpeg,image/webp" onFile={(file) => void upload(file, "image", (asset) => set("headerImage", { asset }))} />
        </div>
      )}
      <Choice t={t} legend="corners" value={tokens.corners} options={[["sharp", "cornersSharp"], ["rounded", "cornersRounded"], ["pill", "cornersPill"]]} onChange={(value) => set("corners", value)} />
      <Choice t={t} legend="buttons" value={tokens.buttons} options={[["filled", "buttonsFilled"], ["outline", "buttonsOutline"]]} onChange={(value) => set("buttons", value)} />
      <Choice t={t} legend="controls" value={tokens.controls} options={[["boxes", "controlsBoxes"], ["pills", "controlsPills"], ["underline", "controlsUnderline"]]} onChange={(value) => set("controls", value)} />
      <Choice t={t} legend="progress" value={tokens.progress} options={[["ruler", "progressRuler"], ["bar", "progressBar"], ["steps", "progressSteps"], ["none", "progressNone"]]} onChange={(value) => set("progress", value)} />
      <Choice t={t} legend="scale" value={tokens.scale} options={[["s", "scaleS"], ["m", "scaleM"], ["l", "scaleL"]]} onChange={(value) => set("scale", value)} />
      <Choice t={t} legend="spacing" value={tokens.spacing} options={[["compact", "spacingCompact"], ["normal", "spacingNormal"], ["airy", "spacingAiry"]]} onChange={(value) => set("spacing", value)} />
      <Choice
        t={t}
        legend="background"
        value={background?.kind === "image-overlay" ? "image-overlay" : "colour"}
        options={[["colour", "backgroundColour"], ["image-overlay", "backgroundImage"]]}
        onChange={(kind) => set("background", kind === "colour" ? { kind: "colour" } : { kind: "image-overlay", fit: "cover", overlay: tokens.colours.background, overlayOpacity: overlay, ...(background?.image !== undefined && { image: background.image }) })}
      />
      {background?.kind === "image-overlay" && (
        <>
          <FileButton
            label={t("uploadImage")}
            fileLabel={t("backgroundFile")}
            accept="image/png,image/jpeg,image/webp"
            onFile={(file) => void upload(file, "image", (asset) => set("background", { ...background, image: { asset } }))}
          />
          <Choice
            t={t}
            legend="overlay"
            value={String(overlay)}
            options={[["0.6", "overlayLight"], ["0.8", "overlayMedium"], ["0.9", "overlayStrong"]]}
            onChange={(value) => set("background", { ...background, overlay: tokens.colours.background, overlayOpacity: Number(value) })}
          />
        </>
      )}
    </div>
  );
}

function Choice<V extends string>({ t, legend, value, options, onChange }: { t: T; legend: LookMessageKey; value: V; options: readonly (readonly [V, LookMessageKey])[]; onChange: (value: V) => void }) {
  const name = useId();

  return (
    <fieldset className="look-group look-choice">
      <legend>{t(legend)}</legend>
      <div className="look-choice__options">
        {options.map(([option, label]) => (
          <label key={option} className="look-choice__option">
            <input type="radio" name={name} value={option} checked={value === option} onChange={() => onChange(option)} />
            <span>{t(label)}</span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}

function ColourField({ label, hexLabel, value, onChange }: { label: string; hexLabel: string; value: string; onChange: (value: string) => void }) {
  const id = useId();
  const [text, setText] = useState(value);

  useEffect(() => setText(value), [value]);

  const change = (next: string) => {
    setText(next);

    if (/^#[0-9a-fA-F]{6}$/.test(next)) {
      onChange(next.toLowerCase());
    } else {
      onChange(next);
    }
  };

  return (
    <div className="look-field look-colour">
      <label htmlFor={id}>{label}</label>
      <div className="look-row">
        <input id={id} type="color" aria-label={label} value={/^#[0-9a-fA-F]{6}$/.test(text) ? text : "#000000"} onChange={(event) => change(event.target.value)} />
        <input className="look-input look-input--hex" aria-label={hexLabel} value={text} spellCheck={false} onChange={(event) => change(event.target.value)} />
      </div>
    </div>
  );
}

function FontField({ t, label, families, value, onChange }: { t: T; label: string; families: readonly string[]; value: string; onChange: (family: string) => void }) {
  const id = useId();

  return (
    <div className="look-field">
      <span className="look-label" id={id}>
        {label}
      </span>
      <ComboBox
        labelledBy={id}
        options={families.map((family) => ({ value: family, label: family }))}
        value={value}
        onChange={onChange}
        texts={{ choose: t("chooseFont"), search: t("search"), results: (shown, total) => t("results", { shown, total }), noResults: t("noResults") }}
      />
    </div>
  );
}

function FileButton({ label, fileLabel, accept, onFile }: { label: string; fileLabel: string; accept: string; onFile: (file: File | undefined) => void }) {
  const input = useRef<HTMLInputElement>(null);

  return (
    <>
      <button type="button" className="look-button" onClick={() => input.current?.click()}>
        {label}
      </button>
      <input
        ref={input}
        type="file"
        className="look-visually-hidden"
        aria-label={fileLabel}
        accept={accept}
        tabIndex={-1}
        onChange={(event: ChangeEvent<HTMLInputElement>) => {
          onFile(event.target.files?.[0]);
          event.target.value = "";
        }}
      />
    </>
  );
}

/** A small survey in the theme: logo, title, a rating question and the start button, drawn from the tokens. */
function Preview({ t, tokens, logoUrl, headerUrl, small = false }: { t: T; tokens: ThemeTokens; logoUrl: string | undefined; headerUrl?: string | undefined; small?: boolean }) {
  const style = {
    "--look-accent": tokens.colours.accent,
    "--look-on-accent": tokens.colours.onAccent,
    "--look-background": tokens.colours.background,
    "--look-surface": tokens.colours.surface,
    "--look-text": tokens.colours.text,
    "--look-muted": tokens.colours.muted,
    "--look-line": tokens.colours.line,
    "--look-heading-font": `"${tokens.fonts.heading.family}", system-ui, sans-serif`,
    "--look-body-font": `"${tokens.fonts.body.family}", system-ui, sans-serif`,
  } as CSSProperties;
  const showsHeader = tokens.layout === "header-image" || tokens.layout === "split-image";

  return (
    <div
      className={`look-preview${small ? " look-preview--small" : ""}`}
      style={style}
      data-corners={tokens.corners}
      data-buttons={tokens.buttons}
      data-controls={tokens.controls}
      data-layout={tokens.layout}
      data-scale={tokens.scale}
      data-spacing={tokens.spacing}
      aria-hidden={small ? true : undefined}
    >
      {showsHeader && <div className="look-preview__header" style={headerUrl !== undefined ? { backgroundImage: `url("${headerUrl}")` } : undefined} />}
      <div className="look-preview__card">
        <div className="look-preview__org">{logoUrl !== undefined ? <img src={logoUrl} alt={t("logo")} /> : <i>{t("previewOrganisation")}</i>}</div>
        <Heading small={small}>{t("previewTitle")}</Heading>
        {!small && <p>{t("previewText")}</p>}
        <p className="look-preview__question">{t("previewQuestion")}</p>
        <div className="look-preview__scale">
          {[1, 2, 3, 4, 5].map((step) => (
            <span key={step} className={step === 4 ? "look-preview__step look-preview__step--on" : "look-preview__step"}>
              {step}
            </span>
          ))}
        </div>
        <span className="look-preview__button">{t("previewStart")}</span>
      </div>
    </div>
  );
}

function Heading({ small, children }: { small: boolean; children: ReactNode }) {
  return small ? <b className="look-preview__title">{children}</b> : <h2 className="look-preview__title">{children}</h2>;
}

/** White or dark text, whichever reads better on the colour. */
function readableOn(colour: string): string {
  if (!/^#[0-9a-fA-F]{6}$/.test(colour)) {
    return white;
  }

  const rgb = (hex: string) => [1, 3, 5].map((start) => parseInt(hex.slice(start, start + 2), 16) / 255) as [number, number, number];

  return contrastRatio(rgb(white), rgb(colour)) >= 4.5 ? white : dark;
}

const what: Record<string, LookMessageKey> = {
  "colours/text|colours/surface": "whatText",
  "colours/text|colours/background": "whatTextBackground",
  "colours/muted|colours/surface": "whatMuted",
  "colours/onAccent|colours/accent": "whatOnAccent",
  "colours/accent|colours/surface": "whatAccent",
  "background/overlay|colours/text": "whatOverlay",
};

const fieldNames: Record<string, LookMessageKey> = {
  "colours/accent": "accent",
  "colours/onAccent": "colourOnAccent",
  "colours/background": "colourBackground",
  "colours/surface": "colourSurface",
  "colours/text": "colourText",
  "colours/muted": "colourMuted",
  "colours/line": "colourLine",
  "background/overlay": "overlay",
};

/** A finding of the theme checks in plain words. */
function describe(t: T, locale: Locale, issue: ThemeIssue): string {
  const number = new Intl.NumberFormat(locale, { maximumFractionDigits: 2 });

  if (issue.code === "contrast-too-low") {
    const key = what[`${issue.path}|${issue.against ?? ""}`] ?? "whatText";

    return t("ratio", { what: t(key), ratio: number.format(issue.ratio ?? 0), minimum: number.format(key === "whatAccent" ? 3 : 4.5) });
  }

  const field = fieldNames[issue.path];

  return issue.code === "colour-invalid" && field !== undefined ? t("notAColour", { what: t(field) }) : t("otherIssue", { what: issue.path });
}
