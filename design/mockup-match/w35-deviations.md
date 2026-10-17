# W35 Participants: built vs mockup (design/AWK-48-participants/w35-*)

Tags: L layout, S style, T text, M missing, D data needed. Paths under apps/designer/src.

## Participants page (v=a)
1. T lead: "Answers by link only count in Results, not here."
2. T search placeholder: "Search name, email, field…"
3. T bounced: chip "Bounced N", funnel "N bounced" (not "address refused")
4. S funnel side: only the number bold red, words graphite
5. T add menu: "Import a file" / "CSV or a pasted table: map the columns"
6. S bulk bar: white checkbox left, bold sans count, small outlined buttons pushed right, no "Clear"
7. M bulk bar context: "3 chosen · of 392 not answered"
8. S selected row tint: signal 6 % over surface
9. T last column header empty (not "LINK")
10. S sortable header arrows not in mockup (keep as addition? -> remove arrows look; plain headers)
11. S/T link cell: small outlined button "Link"
12. M bounced row action "Fix email" (opens person)
13. M unsubscribed row: no action
14. M status dot colours: answered #2f6f4f, opened #9a5b00, started ink, sent line-strong, bounced #b3261e, unsubscribed muted
15. S answered trail: 3 ink + 4th green
16. S unsubscribed: no trail
17. D started detail "Started · page 2 of 4"
18. D invited column always the invite date
19. D last activity with kind: "answered 3 Oct", "opened 9 Oct", "mailbox full · 2 Oct", "yesterday"
20. T reminders "—" for bounced/unsubscribed
21. S reminders left-aligned muted 12px
22. S invited/last activity muted 12px
23. L pager inside the table card: "1–50 of 640 · Sales: 86" with ‹ ›
24. M field-filter count "· Sales: 86"
25. T/D field header display name ("Department"), not raw key
26. S reminders switch ink
27. L reminders sentence: "Automatic reminders · [after 7 days ▾] to people who have not answered, at most [2 ▾] · next: …", compact bold pickers, one line
28. T "next: 23 Oct, 09:00 to 392 people"
29. S "Edit the reminder text" small outlined button right
30. S title 28px
31. S version tag soft sans pill "Published · v3" (survey header)

## Import (v=b)
32. L mapping, check and "Then" on one Columns step; footer "Import N people"
33. T/M steps "✓ File · 2 Columns · 3 Check · 4 Invite"
34. L header: title + steps one row, file meta right, border below
35. S width 900px
36. M arrow column "→" between In the file and Becomes
37. S pickers bold, single line ~34px
38. S "Leave out" muted
39. T new fields "Field: Department (new)"
40. L four check boxes always
41. T "emails not valid · show" with show link listing them
42. T send later hint "They wait; send to all or the chosen ones when you are ready."
43. S ink radios
44. L/S "Also save them in Contacts" toggle + inline "group: …" tag
45. T footer hint "Fields become placeholders: {department}; the External ID links answers to your system"
46. L shaded footer bar: ‹ Back left, hint middle, primary right
47. T meta "customers-q4.csv · 214 rows"

## Person panel (v=c)
48. S status dot before the status line
49. S header border-bottom
50. L order: Email, Name, Language, fields, External ID
51. T/D field labels (display names)
52. T "Copy" small button
53. S link shown without protocol
54. S history dates "2 Oct 09:14"
55. D "Invited · added by import “customers-q4.csv”"
56. D "Reminder 1 sent (automatic)"
57. T "Next: reminder 2 (automatic)"
58. S next entry muted
59. L actions one row in a bordered footer, Remove red, Save primary

## Contacts (v=d)
60. L top header bar (shell) — check against other admin pages
61. S "+ Add contact" primary orange
62. S title 28px
63. M "+ New group" in the Groups card
64. S field entries like nav items 14px
65. T "All" chip without count
66. T "Bounced N"
67. M Export for the chosen
68. M bulk context "48 chosen · group Key accounts"
69. S bulk bar as 6
70. T "Check · bounced"
71. D "No · unsubscribed 4 Oct"
72. S surveys / last invited muted 12px
73. L table fits at 1280 px, nine columns
74. T/D field header display name
75. M sidebar counts (Participants, Contacts)
