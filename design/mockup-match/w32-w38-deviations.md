# W32–W34, W38: built vs mockup (design/AWK-45-feedback, design/AWK-51-key-questions)

Paths under apps/designer/src.

## Surveys list
1. Status as chips ("Published · v3" outlined, "Draft · changes" amber) — SurveyListPage, surveys.css
2. "Draft · changes" (not "… since v1")
3. Subtitle "of Acme GmbH" only
4. Toolbar one row; "Cards show [Contents|Key result] [List|Cards]" at the right end, also when wrapping
5. "New survey" without "+"

## Cards A/E
6. Strip "● Acme GmbH  Monochrome" — look name (data: look name in SurveyFront)
7. Footer one row: status chip · Published date · /s/code · languages (no Results link)
8. No internal-name kicker above the title
9. E legend "210 responses · 86 % complete … last 2 hours ago"
10. Auto-picked chip "FIRST RATING" at the right end of the question line

## Templates
12. "New template" in the header
13. Askwerk cards: one small "Use" right-aligned after "Askwerk · N questions"; note "Copy to our templates"
14. Ours cards: "Ours · N questions · [Edit]"
15. Sample cards with contents like template cards (data: sample contents)
17. Card strip clickable like surveys

## Template bar
18. Visible hint "Edits save by themselves · no publishing, no results"
19. Chip solid border, mono 500
20. Breadcrumb "Templates › name"
21. Back link "‹ All templates" to Templates

## Key flag
23. Key question switch directly under Required; hint indented under the label

## Look
24. Subtitle "changed N things · for this survey"
25. Style labels "Accent colour", "Type", "Shape"
26. Type tiles 3 (Sans, Condensed, Serif)
27. Background tiles Colour, Pattern, Picture
29. Readability box inside the fields, green tint: "✓ Readable everywhere · text 14.3 : 1 · buttons 5.0 : 1"
30. Settings column 340px
31. Stage bar one 48px row: Web/Email/PDF, Phone/Tablet/Desktop, "Page 2 of 4 · Welcome · Thank you", "↗ Open in new tab"
32. Stage checkered, page centred as framed card with shadow
33. Fit fits the page; Ctrl + scroll zoom
34. Stale CSS cleanup look.css
35. Administration → Looks: same layout as the survey Look; stage fills, zoom floating bottom-right
36. Phone/Tablet/Desktop + Ctrl-scroll there; nav label "Looks"

## Share
37. Link hint second sentence "Respondents choose their language in the survey."
38. "Respondent's choice"
39. Link box white, 40px, strong border
40. Anchor buttons (Open ↗, Download PDF) not blue/underlined
41. QR 92px with one "Download QR" button
42. Email card "Invite by email / Personal links; each person answers once. 640 invited, 248 answered. / [Invite people…]" (data: counts)
43. Website card "On a website / Embed the survey in a page of your own site. / [Get embed code]" (code in a dialog)
44. Paper card "Download PDF ▾" menu button
45. Three equal columns
46. No extra "Share" h2

## Drag
48. Drop marker 3px accent line with a round 9px dot
49. Lifted item: shadow, −1.5° tilt, follows the pointer
50. ⋮⋮ grip before each page
51. Drop between pages / onto empty pages
52. Drag and marker on the canvas
