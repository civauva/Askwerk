// SPDX-License-Identifier: AGPL-3.0-only
import { Icon } from "./RowTools";
import { Menu } from "./StandardControls";

/** A language's own name, e.g. "Deutsch" for de, so everyone finds theirs. */
export function languageName(code: string): string {
  try {
    const name = new Intl.DisplayNames([code], { type: "language" }).of(code);

    return name === undefined || name === code ? code.toUpperCase() : name.charAt(0).toLocaleUpperCase(code) + name.slice(1);
  } catch {
    return code.toUpperCase();
  }
}

function LanguageItem({ code, chosen, onSelect }: { code: string; chosen: boolean; onSelect: () => void }) {
  return (
    <button type="button" role="menuitemradio" aria-checked={chosen} tabIndex={-1} className="aw-menu__item aw-language__item" lang={code} onClick={onSelect}>
      <span className="aw-language__check">{chosen && <Icon name="check" size={15} />}</span>
      <span>{languageName(code)}</span>
      <span className="aw-menu__hint" aria-hidden="true">
        {code.toUpperCase()}
      </span>
    </button>
  );
}

/**
 * The language switch (W44 C, AWK-70): a globe and the chosen code; its menu lists each language by its own name. One
 * control for any number of languages, in the app and in surveys; it takes the colour of the text around it.
 */
export function LanguageMenu({ label, languages, value, onChange, className }: { label: string; languages: readonly string[]; value: string; onChange: (code: string) => void; className?: string }) {
  return (
    <Menu
      label={label}
      className="aw-language__menu"
      trigger={(props) => (
        <button type="button" {...props} className={`aw-language${className ? ` ${className}` : ""}`} aria-label={`${label}: ${languageName(value)}`}>
          <Icon name="globe" size={15} />
          <span>{value.toUpperCase()}</span>
          <Icon name="chevron-down" size={14} />
        </button>
      )}
    >
      {languages.map((code) => (
        <LanguageItem key={code} code={code} chosen={code === value} onSelect={() => onChange(code)} />
      ))}
    </Menu>
  );
}
