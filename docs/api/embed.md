<!-- SPDX-License-Identifier: Apache-2.0 -->
# Embedding a survey

Publish → Embed gives the code for a survey; Organisation → Domains lists the websites that may show it. Survey pages
answer with `Content-Security-Policy: frame-ancestors 'self' <listed websites>`, so a survey shows in a frame only on
those websites. With no website listed, embedding is off.

## Script

```html
<script src="https://surveys.acme.at/s/embed.js" data-survey="kz26b7mq" data-lang="de" async></script>
```

The script puts the survey in a frame right after itself and keeps the frame as tall as the survey.

- `data-survey`: the survey's short code (the end of its link).
- `data-lang`: the language to start in; the survey's default when it does not offer it.
- `data-title`: the frame's title for screen readers (default "Survey").
- `data-identity`: optional; a hand-over token your server signed for the respondent with the claim `survey`
  (see [identity](identity.md)). The response then keeps their `sub` and email, shown as `participant.externalId` and
  `participant.email` in the responses API. Used once.

## iframe

```html
<iframe src="https://surveys.acme.at/s/kz26b7mq?embed&lang=de" title="Customer satisfaction" style="width:100%;height:720px;border:0" loading="lazy"></iframe>
```

Add `&identity=…` for an identified respondent, as with `data-identity`. Share → Embed → "Only on these sites"
narrows a survey to some of the organisation's websites.

A plain iframe does not grow with the survey; give it a height, or listen to `askwerk:resize`.

## Events

The survey posts these messages to the embedding page (`window.postMessage`). The script passes them on as DOM events
on its frame (`frame.addEventListener("askwerk:completed", …)`, `event.detail` is the message). They tell what
happens, never what was answered.

| type | when | fields |
|---|---|---|
| `askwerk:started` | the respondent starts | `survey`, `language` |
| `askwerk:page` | a page was saved | `survey`, `page` (the page's id) |
| `askwerk:completed` | the response is complete | `survey` |
| `askwerk:resize` | the survey's height changed | `survey`, `height` (CSS pixels) |

`survey` is the code or id the frame was opened with. Check `event.origin` against the survey's address when you
listen to the messages yourself.

## The designer and Results inside your product

Organisation → Domains → "Embedding the designer": off by default; switched on, the designer and Results may show in a
frame only on the websites listed there. Your product signs people in with the hand-over of Organisation → Sign-in: its
server signs a token for the user (see [identity](identity.md)) with the extra claim `survey` (the survey's id) and,
optionally, `locale`.

```html
<iframe src="https://surveys.acme.at/embed/designer?token=eyJ…&lang=de" title="Survey designer" style="width:100%;height:900px;border:0"></iframe>
<iframe src="https://surveys.acme.at/embed/results?token=eyJ…" title="Survey results" style="width:100%;height:900px;border:0"></iframe>
```

Askwerk checks the token once and starts an embedded session for that survey only: a bearer token the frame keeps in
memory (never a third-party cookie), renewed while the frame is open, at most 8 hours. Analysts and viewers always get
Results. There is no Askwerk bar, survey list or account menu inside the frame; your product owns the navigation.

The frame posts these messages to your page (`window.postMessage`):

| Message | When |
|---|---|
| `askwerk:ready` `{surveyId, view}` | the session started |
| `askwerk:saved` `{surveyId}` | the draft was saved |
| `askwerk:published` `{surveyId, version}` | a version was published |
| `askwerk:navigate` `{to, surveyId}` | the person asked for the survey list (`to: "list"`) or another survey |
| `askwerk:resize` `{height}` | the content's height changed |
| `askwerk:session-ending` `{endsAt}` | two minutes before the session ends |
| `askwerk:error` `{code}` | `token-invalid`, `embedding-off`, `no-access`, `survey-not-found` or `failed` |

Check `event.origin` against your Askwerk address before you act on a message.
