// SPDX-License-Identifier: AGPL-3.0-only
// The guides in docs/ as React elements (AWK-34): headings, paragraphs, lists, tables, code blocks, inline code,
// bold and links. Small on purpose; the guides use nothing else. Text is never inserted as HTML.
import type { ReactNode } from "react";

function inline(text: string, key: string): ReactNode[] {
  const parts: ReactNode[] = [];
  const pattern = /(`[^`]+`)|(\*\*[^*]+\*\*)|(\[[^\]]+\]\([^)]+\))/g;
  let last = 0;
  let index = 0;

  for (const match of text.matchAll(pattern)) {
    if (match.index > last) {
      parts.push(text.slice(last, match.index));
    }

    const token = match[0];
    const id = `${key}-${index++}`;

    if (token.startsWith("`")) {
      parts.push(<code key={id}>{token.slice(1, -1)}</code>);
    } else if (token.startsWith("**")) {
      parts.push(<strong key={id}>{token.slice(2, -2)}</strong>);
    } else {
      const [, label = "", href = ""] = /^\[([^\]]+)\]\(([^)]+)\)$/.exec(token) ?? [];
      parts.push(
        /^https?:\/\//.test(href) ? (
          <a key={id} href={href} target="_blank" rel="noreferrer">
            {label}
          </a>
        ) : (
          <span key={id}>{label}</span>
        ),
      );
    }

    last = match.index + token.length;
  }

  if (last < text.length) {
    parts.push(text.slice(last));
  }

  return parts;
}

/** The Markdown as elements; the first heading becomes level 2, the page's own h1 names the guide. */
export function Markdown({ source }: { source: string }) {
  const lines = source.replace(/\r\n/g, "\n").split("\n");
  const blocks: ReactNode[] = [];
  let i = 0;

  while (i < lines.length) {
    const line = lines[i]!;
    const key = `b${i}`;

    if (line.startsWith("<!--") || line.trim() === "") {
      i++;
    } else if (line.startsWith("```")) {
      const code: string[] = [];
      i++;

      while (i < lines.length && !lines[i]!.startsWith("```")) {
        code.push(lines[i]!);
        i++;
      }

      i++;
      blocks.push(
        <pre key={key} tabIndex={0} className="dev-code">
          <code>{code.join("\n")}</code>
        </pre>,
      );
    } else if (/^#{1,4} /.test(line)) {
      const level = Math.min(4, line.indexOf(" ") + 1);
      const text = inline(line.slice(line.indexOf(" ") + 1), key);
      blocks.push(level <= 2 ? <h2 key={key}>{text}</h2> : level === 3 ? <h3 key={key}>{text}</h3> : <h4 key={key}>{text}</h4>);
      i++;
    } else if (line.startsWith("|")) {
      const rows: string[][] = [];

      while (i < lines.length && lines[i]!.startsWith("|")) {
        if (!/^\|[\s|:-]+\|$/.test(lines[i]!)) {
          rows.push(lines[i]!.slice(1, -1).split("|").map((cell) => cell.trim()));
        }

        i++;
      }

      const [head, ...body] = rows;
      blocks.push(
        <table key={key} className="dev-table">
          <thead>
            <tr>{head?.map((cell, index) => <th key={index}>{inline(cell, `${key}h${index}`)}</th>)}</tr>
          </thead>
          <tbody>
            {body.map((row, rowIndex) => (
              <tr key={rowIndex}>{row.map((cell, index) => <td key={index}>{inline(cell, `${key}r${rowIndex}c${index}`)}</td>)}</tr>
            ))}
          </tbody>
        </table>,
      );
    } else if (/^\s*([-*]|\d+\.) /.test(line)) {
      const ordered = /^\s*\d+\. /.test(line);
      const items: string[] = [];

      while (i < lines.length && /^\s*([-*]|\d+\.) /.test(lines[i]!)) {
        let item = lines[i]!.replace(/^\s*([-*]|\d+\.) /, "");
        i++;

        while (i < lines.length && /^\s{2,}\S/.test(lines[i]!) && !/^\s*([-*]|\d+\.) /.test(lines[i]!)) {
          item += ` ${lines[i]!.trim()}`;
          i++;
        }

        items.push(item);
      }

      const children = items.map((item, index) => <li key={index}>{inline(item, `${key}i${index}`)}</li>);
      blocks.push(ordered ? <ol key={key}>{children}</ol> : <ul key={key}>{children}</ul>);
    } else {
      const paragraph: string[] = [];

      while (i < lines.length && lines[i]!.trim() !== "" && !/^(#{1,4} |```|\||\s*([-*]|\d+\.) )/.test(lines[i]!)) {
        paragraph.push(lines[i]!.trim());
        i++;
      }

      blocks.push(<p key={key}>{inline(paragraph.join(" "), key)}</p>);
    }
  }

  return <div className="dev-doc">{blocks}</div>;
}
