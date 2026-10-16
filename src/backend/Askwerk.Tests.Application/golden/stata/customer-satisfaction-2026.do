* Askwerk export of Customer satisfaction 2026, 2026-10-14T18:00:00Z.
* Put this file next to customer-satisfaction-2026.csv and run it.
import delimited using "customer-satisfaction-2026.csv", varnames(1) encoding("utf-8") bindquote(strict) case(preserve) clear
label variable response_id `"Response"'
label variable status `"Status"'
label variable version `"Version"'
label variable language `"Language"'
label variable started_at `"Started (UTC)"'
label variable completed_at `"Finished (UTC)"'
label variable duration_seconds `"Time to finish (seconds)"'
label variable SOURCE `"Where from?"'
label variable SOURCE_OTHER `"Where from? – Other:"'
label variable CH_a `"Which, "if any"? – A"'
label variable CH_b `"Which, "if any"? – B"'
label variable SAT `"Satisfied?"'
label variable TXT `"Anything else?"'
label define SOURCE 1 `"Search"' 2 `"Ads"' 9 `"Other:"', replace
label values SOURCE SOURCE
label define CH_a 1 `"A"' 0 `"Not chosen"', replace
label values CH_a CH_a
label define CH_b 1 `"B"' 0 `"Not chosen"', replace
label values CH_b CH_b
replace SAT = .a if SAT == -99
label define SAT .a `"Don't know"', replace
label values SAT SAT
