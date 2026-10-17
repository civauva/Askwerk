// SPDX-License-Identifier: Apache-2.0
// Askwerk drop-in components (W37): <script src="https://your-askwerk/s/components.js" type="module"></script>, then
//   <askwerk-survey survey="CODE" identity="…">       a survey for respondents
//   <askwerk-surveys token="…">                        the survey list
//   <askwerk-designer token="…">                       the designer of the token's survey
//   <askwerk-results token="…">                        Results of the token's survey
//   <askwerk-result token="…" question="SAT">          one key result
// Each is a thin frame of Askwerk that grows with its content and passes Askwerk's events on as DOM events on the
// element (askwerk:completed, askwerk:published, askwerk:open…). look="host" (the default) follows the page: the
// --askwerk-accent, --askwerk-background, --askwerk-font and --askwerk-radius custom properties, or the attributes
// accent, background, font and radius, else the page's font (docs/api/embed.md).
const origin = new URL(import.meta.url).origin;

/** The host look of an element: attributes first, then custom properties, then the page's font. */
function lookOf(element) {
  if ((element.getAttribute("look") || "host") !== "host") {
    return null;
  }

  const style = getComputedStyle(element);
  const value = (attribute, property) => (element.getAttribute(attribute) || style.getPropertyValue(property) || "").trim();
  const look = {};
  const accent = value("accent", "--askwerk-accent");
  const background = value("background", "--askwerk-background");
  const font = value("font", "--askwerk-font") || style.fontFamily;
  const radius = parseFloat(value("radius", "--askwerk-radius"));

  accent && (look.accent = accent);
  background && (look.background = background);
  font && (look.font = font);
  Number.isFinite(radius) && (look.radius = radius);

  return Object.keys(look).length === 0 ? null : look;
}

const query = (pairs) =>
  pairs
    .filter(([, value]) => value !== null && value !== "")
    .map(([key, value]) => (value === true ? key : `${key}=${encodeURIComponent(value)}`))
    .join("&");

class AskwerkFrame extends HTMLElement {
  static get observedAttributes() {
    return ["survey", "token", "identity", "lang", "question", "look", "accent", "background", "font", "radius"];
  }

  constructor() {
    super();
    this.listen = (event) => {
      const data = event.data;

      if (event.origin !== origin || !this.frame || event.source !== this.frame.contentWindow || !data || typeof data.type !== "string" || data.type.indexOf("askwerk:") !== 0) {
        return;
      }

      if (data.type === "askwerk:resize" && typeof data.height === "number" && data.height > 0) {
        this.frame.style.height = `${Math.ceil(data.height)}px`;
      }

      this.dispatchEvent(new CustomEvent(data.type, { bubbles: true, composed: true, detail: data }));
    };
  }

  connectedCallback() {
    window.addEventListener("message", this.listen);
    this.render();
  }

  disconnectedCallback() {
    window.removeEventListener("message", this.listen);
  }

  attributeChangedCallback() {
    this.isConnected && this.render();
  }

  render() {
    const address = this.address();

    if (address === null) {
      return;
    }

    const root = this.shadowRoot || this.attachShadow({ mode: "open" });
    const look = lookOf(this);
    const style = document.createElement("style");
    style.textContent = ":host{display:block}iframe{display:block;width:100%;border:0;background:transparent}";
    this.frame = document.createElement("iframe");
    this.frame.src = `${origin}${address}${look === null ? "" : `#look=${encodeURIComponent(JSON.stringify(look))}`}`;
    this.frame.title = this.getAttribute("title") || this.constructor.title;
    this.frame.loading = "lazy";
    this.frame.style.height = `${this.constructor.height}px`;
    root.replaceChildren(style, this.frame);
  }
}

customElements.get("askwerk-survey") ||
  customElements.define(
    "askwerk-survey",
    class extends AskwerkFrame {
      static title = "Survey";
      static height = 640;

      address() {
        const survey = this.getAttribute("survey");

        return survey ? `/s/${encodeURIComponent(survey)}?${query([["embed", true], ["lang", this.getAttribute("lang")], ["identity", this.getAttribute("identity")]])}` : null;
      }
    },
  );

/** The components that sign in with the host's hand-over token, each with its view. */
for (const [name, path, title, height, extra] of [
  ["askwerk-surveys", "list", "Surveys", 560, []],
  ["askwerk-designer", "designer", "Survey designer", 900, []],
  ["askwerk-results", "results", "Survey results", 900, []],
  ["askwerk-result", "result", "Key result", 170, ["question"]],
]) {
  customElements.get(name) ||
    customElements.define(
      name,
      class extends AskwerkFrame {
        static title = title;
        static height = height;

        address() {
          const token = this.getAttribute("token");

          return token ? `/embed/${path}?${query([["token", token], ["lang", this.getAttribute("lang")], ...extra.map((attribute) => [attribute, this.getAttribute(attribute)])])}` : null;
        }
      },
    );
}
