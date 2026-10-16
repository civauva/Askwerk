-- SPDX-License-Identifier: Apache-2.0
-- Tables for the Askwerk SQL Server sync example, in long format: one row per response and variable.
-- Reports join askwerk_answer to askwerk_value_label; nothing changes shape when a survey gets a new question.

CREATE TABLE askwerk_response (
  response_id       uniqueidentifier NOT NULL PRIMARY KEY,
  survey_id         uniqueidentifier NOT NULL,
  version           int              NOT NULL,
  status            nvarchar(20)     NOT NULL, -- partial or complete
  language          nvarchar(16)     NOT NULL,
  participant_email nvarchar(320)    NULL,     -- invited participants only
  external_id       nvarchar(200)    NULL,     -- the host system's user id, when it signs people in
  started_at        datetimeoffset   NOT NULL,
  completed_at      datetimeoffset   NULL,
  change_sequence   bigint           NOT NULL
);
CREATE INDEX ix_askwerk_response_survey ON askwerk_response (survey_id, change_sequence);

CREATE TABLE askwerk_answer (
  response_id  uniqueidentifier NOT NULL REFERENCES askwerk_response (response_id) ON DELETE CASCADE,
  variable     nvarchar(128)    NOT NULL, -- SAT, SOURCE_2, SAT_SERVICE …
  value        nvarchar(max)    NOT NULL, -- the code, number or text
  value_number decimal(28, 8)   NULL,     -- the same as a number, for numeric variables
  label        nvarchar(max)    NULL,     -- the value's label in the job's language
  PRIMARY KEY (response_id, variable)
);

CREATE TABLE askwerk_variable (
  survey_id  uniqueidentifier NOT NULL,
  version    int              NOT NULL,
  variable   nvarchar(128)    NOT NULL,
  label      nvarchar(max)    NOT NULL, -- the question (and option or row) it stands for
  is_numeric bit              NOT NULL,
  language   nvarchar(16)     NOT NULL,
  PRIMARY KEY (survey_id, version, variable)
);

CREATE TABLE askwerk_value_label (
  survey_id uniqueidentifier NOT NULL,
  variable  nvarchar(128)    NOT NULL,
  code      nvarchar(128)    NOT NULL,
  language  nvarchar(16)     NOT NULL,
  label     nvarchar(max)    NOT NULL,
  PRIMARY KEY (survey_id, variable, code, language)
);

CREATE TABLE askwerk_sync_state (
  survey_id     uniqueidentifier NOT NULL PRIMARY KEY,
  last_sequence bigint           NOT NULL, -- the nextSince of the last stored page
  last_run_at   datetimeoffset   NOT NULL,
  last_error    nvarchar(max)    NULL
);
