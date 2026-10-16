// SPDX-License-Identifier: Apache-2.0
// Askwerk embed (AWK-7): <script src="https://your-domain/s/embed.js" data-survey="CODE" data-lang="de" async></script>
// puts the survey in a frame right after the script, keeps the frame as tall as the survey, and passes the survey's
// events on as DOM events on the frame: askwerk:started, askwerk:page, askwerk:completed, askwerk:resize
// (docs/api/embed.md). The survey's organisation must list the page's website under Domains.
(function () {
  "use strict";
  var script = document.currentScript;

  if (!script || !script.getAttribute("data-survey")) {
    return;
  }

  var origin = new URL(script.src).origin;
  var lang = script.getAttribute("data-lang");
  // data-identity: a host product's token naming the respondent (AWK-13); signed by the host's server, used once.
  var identity = script.getAttribute("data-identity");
  var frame = document.createElement("iframe");
  frame.src = origin + "/s/" + encodeURIComponent(script.getAttribute("data-survey")) + "?embed" + (lang ? "&lang=" + encodeURIComponent(lang) : "") + (identity ? "&identity=" + encodeURIComponent(identity) : "");
  frame.title = script.getAttribute("data-title") || "Survey";
  frame.loading = "lazy";
  frame.style.cssText = "display:block;width:100%;height:640px;border:0";
  script.parentNode.insertBefore(frame, script.nextSibling);

  window.addEventListener("message", function (event) {
    var data = event.data;

    if (event.origin !== origin || event.source !== frame.contentWindow || !data || typeof data.type !== "string" || data.type.indexOf("askwerk:") !== 0) {
      return;
    }

    if (data.type === "askwerk:resize" && typeof data.height === "number" && data.height > 0) {
      frame.style.height = Math.ceil(data.height) + "px";
    }

    frame.dispatchEvent(new CustomEvent(data.type, { bubbles: true, detail: data }));
  });
})();
