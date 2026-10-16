// SPDX-License-Identifier: AGPL-3.0-only
import type { Me } from "@askwerk/app-client";

const editorRoles = ["owner", "admin", "editor"];

/**
 * Whether the user edits surveys in the current organisation (AWK-27, W15 rev. 2): owners, admins and editors open
 * the editor; analysts and viewers open a survey's Results, with Preview and the PDF.
 */
export function canEditSurveys(me: Me): boolean {
  const organisation = me.organisations.find((candidate) => candidate.id === me.currentOrganisationId) ?? me.organisations[0];

  return organisation !== undefined && editorRoles.includes(organisation.role);
}
