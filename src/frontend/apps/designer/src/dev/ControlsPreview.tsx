// SPDX-License-Identifier: AGPL-3.0-only
// Development only (/dev/controls): the shared controls of AWK-19 in their states, for review and screenshots.
// ?lang=de|en picks the language. Never part of a production build: main.tsx loads it behind import.meta.env.DEV.
import { ComboBox, DataGrid, dataGridTexts, DatePicker, TimeField, type DataGridColumn, type DataGridSort } from "@askwerk/ui";
import { useState } from "react";

const branches = ["Wien Mitte", "Wien Favoriten", "Wiener Neustadt", "Graz", "Linz", "Salzburg", "Innsbruck", "Klagenfurt", "Bregenz", "Eisenstadt", "St. Pölten", "Villach"].map(
  (label, index) => ({ value: `B${index + 1}`, label }),
);

const texts = {
  de: {
    title: "Gemeinsame Bedienelemente",
    branch: "Filiale",
    date: "Datum",
    time: "Uhrzeit",
    combo: { choose: "Auswählen…", search: "Suchen", results: (shown: number, total: number) => `${shown} von ${total}`, noResults: "Nichts gefunden." },
    datePicker: { openCalendar: "Datum wählen", previousMonth: "Voriger Monat", nextMonth: "Nächster Monat", invalid: "Geben Sie ein Datum wie 14.10.2026 ein." },
    timeField: { chooseTime: "Uhrzeit wählen", invalid: "Geben Sie eine Uhrzeit wie 09:30 ein." },
  },
  en: {
    title: "Shared controls",
    branch: "Branch",
    date: "Date",
    time: "Time",
    combo: { choose: "Choose…", search: "Search", results: (shown: number, total: number) => `${shown} of ${total}`, noResults: "Nothing matches." },
    datePicker: { openCalendar: "Choose a date", previousMonth: "Previous month", nextMonth: "Next month", invalid: "Enter a date like 14/10/2026." },
    timeField: { chooseTime: "Choose a time", invalid: "Enter a time like 09:30." },
  },
};

type Row = { id: string; title: string; status: string; responses: number };

const rows: Row[] = Array.from({ length: 1000 }, (_, index) => ({
  id: `r${index + 1}`,
  title: `${["Customer satisfaction", "Employee pulse", "Event feedback", "NPS short"][index % 4]} ${index + 1}`,
  status: ["Published", "Draft", "Closed"][index % 3]!,
  responses: (index * 37) % 4000,
}));

/** The data grid (AWK-19, W13) with paging and choosing, loading more, and its states. */
function GridPreview({ locale }: { locale: "de" | "en" }) {
  const texts = dataGridTexts(locale);
  const [page, setPage] = useState(2);
  const [pageSize, setPageSize] = useState(50);
  const [sort, setSort] = useState<DataGridSort>({ key: "responses", descending: true });
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
  const [all, setAll] = useState(false);
  const [loaded, setLoaded] = useState(150);
  const [active, setActive] = useState<string | null>(null);
  const columns: DataGridColumn<Row>[] = [
    { key: "title", header: locale === "de" ? "Titel" : "Title", cell: (row) => row.title, sortable: true, phone: "title" },
    { key: "status", header: "Status", cell: (row) => row.status },
    { key: "responses", header: locale === "de" ? "Antworten" : "Responses", cell: (row) => row.responses.toLocaleString(locale), align: "end", sortable: true },
  ];
  const sorted = [...rows].sort((a, b) => (sort.key === "title" ? a.title.localeCompare(b.title) : a.responses - b.responses) * (sort.descending ? -1 : 1));

  return (
    <>
      <DataGrid
        label="Paging"
        columns={columns}
        rows={sorted.slice((page - 1) * pageSize, page * pageSize)}
        rowKey={(row) => row.id}
        sort={sort}
        onSort={setSort}
        onOpen={(row) => setActive(row.id)}
        active={active}
        paging={{ page, pageSize, total: rows.length, onPage: setPage, onPageSize: setPageSize }}
        selection={{
          selected,
          all,
          rowLabel: (row) => row.title,
          onChange: (next, nextAll) => {
            setSelected(next);
            setAll(nextAll);
          },
          actions: <button type="button">{locale === "de" ? "Erinnerung senden" : "Send reminder"}</button>,
        }}
        texts={texts}
      />
      <DataGrid
        label="More"
        columns={columns}
        rows={rows.slice(0, loaded)}
        rowKey={(row) => row.id}
        stickyFirst
        more={{ total: rows.length, loading: false, onMore: () => setLoaded((count) => Math.min(rows.length, count + 50)) }}
        texts={texts}
      />
      <DataGrid label="Loading" columns={columns} rows={null} rowKey={(row) => row.id} texts={texts} />
      <DataGrid label="Empty" columns={columns} rows={[]} rowKey={(row) => row.id} empty={<p>{locale === "de" ? "Noch keine Umfragen." : "No surveys yet."}</p>} texts={texts} />
      <DataGrid label="Error" columns={columns} rows={null} rowKey={(row) => row.id} error onRetry={() => undefined} texts={texts} />
    </>
  );
}

export function ControlsPreview() {
  const requested = new URLSearchParams(location.search).get("lang");
  const locale = requested === "de" ? "de" : "en";
  const t = texts[locale];
  const [branch, setBranch] = useState<string | null>(null);
  const [date, setDate] = useState<string | null>("2026-10-14");
  const [time, setTime] = useState<string | null>("09:30");

  return (
    <main lang={locale} style={{ maxWidth: 1100, margin: "0 auto", padding: "32px 20px", display: "grid", gap: 24, font: "400 15px/1.5 var(--aw-font)", color: "var(--aw-ink)" }}>
      <h1 style={{ margin: 0, font: "700 28px/1.1 var(--aw-font-display)" }}>{t.title}</h1>
      <div style={{ display: "grid", gap: 6 }}>
        <span id="branch-label" style={{ fontWeight: 600 }}>
          {t.branch}
        </span>
        <ComboBox labelledBy="branch-label" options={branches} value={branch} onChange={setBranch} texts={t.combo} />
      </div>
      <div style={{ display: "grid", gap: 6 }}>
        <label htmlFor="date" style={{ fontWeight: 600 }}>
          {t.date}
        </label>
        <DatePicker id="date" locale={locale} value={date} onChange={setDate} texts={t.datePicker} />
      </div>
      <div style={{ display: "grid", gap: 6 }}>
        <label htmlFor="time" style={{ fontWeight: 600 }}>
          {t.time}
        </label>
        <TimeField id="time" value={time} onChange={setTime} texts={t.timeField} />
      </div>
      <GridPreview locale={locale} />
    </main>
  );
}
