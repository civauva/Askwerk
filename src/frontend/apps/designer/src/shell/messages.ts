// SPDX-License-Identifier: AGPL-3.0-only
import type { Catalogs } from "@askwerk/schema";

const en = {
  navigation: "Navigation",
  menu: "Menu",
  closeMenu: "Close menu",
  surveys: "Surveys",
  templates: "Templates",
  administration: "Administration",
  organisation: "Organisation",
  members: "Members",
  signIn: "Sign-in",
  look: "Look of surveys",
  messages: "Messages",
  domains: "Domains",
  developers: "Developers & API",
  thisAskwerk: "This Askwerk",
  organisations: "Organisations",
  administrators: "Administrators",
  installation: "Installation",
  allSurveys: "All surveys",
  yourOrganisations: "Your organisations",
  suspended: "suspended",
  organisationSettings: "Organisation settings",
  newOrganisation: "New organisation",
} as const;

export type ShellMessageKey = keyof typeof en;

export const shellMessages: Catalogs<ShellMessageKey> = {
  en,
  de: {
    navigation: "Navigation",
    menu: "Menü",
    closeMenu: "Menü schließen",
    surveys: "Umfragen",
    templates: "Vorlagen",
    administration: "Verwaltung",
    organisation: "Organisation",
    members: "Mitglieder",
    signIn: "Anmeldung",
    look: "Aussehen der Umfragen",
    messages: "Nachrichten",
    domains: "Domains",
    developers: "Entwickler & API",
    thisAskwerk: "Dieses Askwerk",
    organisations: "Organisationen",
    administrators: "Administratoren",
    installation: "Installation",
    allSurveys: "Alle Umfragen",
    yourOrganisations: "Ihre Organisationen",
    suspended: "gesperrt",
    organisationSettings: "Einstellungen der Organisation",
    newOrganisation: "Neue Organisation",
  },
};
