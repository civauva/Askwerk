* Encoding: UTF-8.
* Askwerk export of 'Customer satisfaction 2026', 2026-10-14T18:00:00Z.
* Put this file next to customer-satisfaction-2026.csv and run it.
GET DATA
  /TYPE=TXT
  /FILE='customer-satisfaction-2026.csv'
  /ENCODING='UTF8'
  /ARRANGEMENT=DELIMITED
  /DELCASE=LINE
  /FIRSTCASE=2
  /DELIMITERS=','
  /QUALIFIER='"'
  /VARIABLES=
    response_id A36
    status A8
    version F8.0
    language A2
    started_at A20
    completed_at A20
    duration_seconds F8.0
    SOURCE F8.0
    SOURCE_OTHER A20
    CH_a F8.0
    CH_b F8.0
    SAT F8.0
    TXT A29.

VARIABLE LABELS
  response_id 'Response'
  /status 'Status'
  /version 'Version'
  /language 'Language'
  /started_at 'Started (UTC)'
  /completed_at 'Finished (UTC)'
  /duration_seconds 'Time to finish (seconds)'
  /SOURCE 'Where from?'
  /SOURCE_OTHER 'Where from? – Other:'
  /CH_a 'Which, "if any"? – A'
  /CH_b 'Which, "if any"? – B'
  /SAT 'Satisfied?'
  /TXT 'Anything else?'.

VALUE LABELS
  SOURCE 1 'Search' 2 'Ads' 9 'Other:'
  /CH_a 1 'A' 0 'Not chosen'
  /CH_b 1 'B' 0 'Not chosen'
  /SAT -99 'Don''t know'.

MISSING VALUES SAT (-99).

EXECUTE.
