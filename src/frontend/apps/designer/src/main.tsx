// SPDX-License-Identifier: AGPL-3.0-only
import "@askwerk/ui/tokens.css";
import "./styles.css";
import { createApiKeysClient, createAuthClient, createConnectionsClient, createContactsClient, createDevelopersClient, createInstallationClient, createOrganisationClient, createDomainsClient, createMembersClient, createMessagesClient, createSignInSettingsClient, createOAuthClientsClient, createParticipantsClient, createResultsClient, createSurveysClient, createThemesClient, createWebhooksClient, type SurveysClient } from "@askwerk/app-client";
import { pickLocale } from "@askwerk/schema";
import { beaconTransport, createTelemetry, installGlobalErrorHandlers, observeWebVitals } from "@askwerk/telemetry";
import { StrictMode, useEffect, useState, type ReactNode } from "react";
import { createRoot } from "react-dom/client";
import { AccountApp, type HomeProps } from "./account/AccountApp";
import { AdministratorsPage, InstallationPage, OrganisationPage, OrganisationsPage } from "./admin/AdminPages";
import { TemplatesPage } from "./admin/TemplatesPage";
import { AppShell, placeOf } from "./shell/AppShell";
import { designerApi } from "./designer/api";
import { Designer } from "./designer/Designer";
import { canEditSurveys } from "./account/roles";
import { ApiKeysPage } from "./apiKeys/ApiKeysPage";
import { DevelopersPage } from "./developers/DevelopersPage";
import { DomainsPage } from "./domains/DomainsPage";
import { EmbeddedApp } from "./embed/EmbeddedApp";
import { LookPage } from "./look/LookPage";
import { MembersPage } from "./members/MembersPage";
import { ConnectPage } from "./connections/ConnectPage";
import { ConnectionsPage } from "./connections/ConnectionsPage";
import { ContactsPage } from "./contacts/ContactsPage";
import { SignInPage } from "./members/SignInPage";
import { MessagesPage } from "./messages/MessagesPage";
import { ResultsPage } from "./results/ResultsPage";
import { SurveyListPage } from "./surveys/SurveyListPage";
import { ParticipantsPage, SharePage, SurveySettingsPage, type SurveyTab } from "./workspace";

const telemetry = createTelemetry({ service: "designer", transport: beaconTransport(), route: () => location.pathname });
installGlobalErrorHandlers(window, telemetry);
observeWebVitals(PerformanceObserver, telemetry);

const auth = createAuthClient({ telemetry });
const surveys = createSurveysClient({ telemetry });
const themes = createThemesClient({ telemetry });
const designer = designerApi(surveys, themes);
const messages = createMessagesClient({ telemetry });
const results = createResultsClient({ telemetry });
const domains = createDomainsClient({ telemetry });
const participants = createParticipantsClient({ telemetry });
const contacts = createContactsClient({ telemetry });
const connections = createConnectionsClient({ telemetry });
const apiKeys = createApiKeysClient({ telemetry });
const developers = createDevelopersClient({ telemetry });
const organisation = createOrganisationClient({ telemetry });
const installation = createInstallationClient({ telemetry });
// Share → Embed → "Only on these sites" (AWK-13).
const embedSitesOf = (surveyId: string) => ({ load: () => domains.surveyEmbedding(surveyId), save: (origins: readonly string[]) => domains.setSurveyEmbedding(surveyId, origins) });
const members = createMembersClient({ telemetry });
const signInSettings = createSignInSettingsClient({ telemetry });
const webhooks = createWebhooksClient({ telemetry });
const oauthClients = createOAuthClientsClient({ telemetry });
// The invitation as people get it, drawn by the server (AWK-7).
const previewInvitation = (surveyId: string, language: string) => messages.preview("invitation", { surveyId, language }).then(({ subject, text }) => ({ subject, text }));
const previewEmail = (surveyId: string, language: string) => messages.preview("invitation", { surveyId, language }).then(({ html }) => html);

/** Saves a file the browser made, e.g. a QR code, as a download. */
function saveFile(name: string, file: Blob) {
  const url = URL.createObjectURL(file);
  const link = document.createElement("a");
  link.href = url;
  link.download = name;
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/** Where the survey list was (?page=2&sort=title), so going back from a survey returns there (W13 A). */
let listQuery = "";

/** Signed in: every page inside the app's sidebar (AWK-38, W29); a survey's screens step into it. */
function Home(props: HomeProps) {
  const { me, locale, client: authClient, path, onNavigate, onSignedOut, onSwitched, onOpenAccount } = props;
  // The Contacts count in the sidebar (W35), once the Contacts page has read it.
  const [contactsCount, setContactsCount] = useState<number | null>(null);
  const shell = (page: ReactNode, survey?: { onBack: () => void }) => (
    <AppShell
      client={authClient}
      locale={locale}
      me={me}
      active={placeOf(path)}
      onNavigate={onNavigate}
      onSignedOut={onSignedOut}
      onSwitched={onSwitched}
      onOpenAccount={onOpenAccount}
      onLeave={() => members.remove(me.id).then(onSwitched)}
      {...(survey !== undefined && { survey })}
      {...(contactsCount === null ? {} : { counts: { contacts: contactsCount } })}
    >
      {page}
    </AppShell>
  );

  return <Pages {...props} shell={shell} onContactsCount={setContactsCount} />;
}

function Pages({ me, locale, path, onNavigate, onSwitched, shell, onContactsCount }: HomeProps & { shell: (page: ReactNode, survey?: { onBack: () => void }) => ReactNode; onContactsCount: (count: number) => void }) {
  const [, surveyId, view] = /^\/surveys\/([^/]+)(?:\/(look|share|results|participants|survey-settings))?$/.exec(path) ?? [];

  if (surveyId !== undefined) {
    const id = decodeURIComponent(surveyId);
    const canEdit = canEditSurveys(me);
    const at = (other: string) => `/surveys/${encodeURIComponent(other)}`;
    // One bar and one row of tabs on every screen of a survey (AWK-29, W17, W23); Questions and Look live in the editor.
    const onTab = (tab: SurveyTab) => onNavigate(tab === "build" ? at(id) : tab === "settings" ? `${at(id)}/look` : `${at(id)}/${tab}`);
    const navigation = { surveys, surveyId: id, locale, canEdit, onTab, onBack: () => onNavigate(`/${listQuery}`), onOpenSurvey: (other: string) => onNavigate(at(other)), onTemplates: () => onNavigate("/templates") };
    const inSurvey = (page: ReactNode) => shell(page, { onBack: navigation.onBack });
    const preview = (language: string) => previewInvitation(id, language);

    // Analysts and viewers read a survey's Results and Participants; only editors get the editor and Share (AWK-27, W17).
    if (view === "participants") {
      return inSurvey(<ParticipantsPage key={id} {...navigation} participants={participants} contacts={contacts} preview={preview} />);
    }

    if (view === "survey-settings") {
      return inSurvey(<SurveySettingsPage key={id} {...navigation} />);
    }

    if (view === "share" && canEdit) {
      return inSurvey(<SharePage key={id} {...navigation} participants={participants} preview={preview} saveFile={saveFile} embedSites={embedSitesOf} />);
    }

    return inSurvey(
      (view === undefined || view === "look") && canEdit ? (
        <Designer key={id} api={designer} surveyId={id} initialLocale={locale} newId={() => crypto.randomUUID()} onBack={navigation.onBack} onTab={onTab} onOpenSurvey={navigation.onOpenSurvey} onTemplates={navigation.onTemplates} participants={participants} previewInvitation={previewInvitation} previewEmail={previewEmail} initialLook={view === "look"} />
      ) : (
        <ResultsPage key={id} client={results} {...navigation} surveyLink={`${location.origin}/s/${encodeURIComponent(id)}`} />
      ),
    );
  }


  if (path === "/templates") {
    return shell(
      <TemplatesPage
        client={surveys}
        locale={locale}
        canEdit={canEditSurveys(me)}
        userId={me.id}
        organisation={me.organisations.find((organisation) => organisation.id === me.currentOrganisationId)?.name ?? ""}
        onOpen={(id) => onNavigate(`/surveys/${encodeURIComponent(id)}`)}
      />,
    );
  }

  if (path === "/organisation") {
    return shell(<OrganisationPage client={organisation} members={members} locale={locale} onDeleted={onSwitched} />);
  }

  if (path.startsWith("/installation/organisations")) {
    return shell(<OrganisationsPage client={installation} locale={locale} startNew={path.endsWith("?new")} />);
  }

  if (path === "/installation/administrators") {
    return shell(<AdministratorsPage client={installation} locale={locale} />);
  }

  if (path === "/installation") {
    return shell(<InstallationPage client={installation} locale={locale} />);
  }

  if (path === "/developers") {
    const role = me.organisations.find((organisation) => organisation.id === me.currentOrganisationId)?.role;

    return (
      shell(
        <DevelopersPage client={developers} locale={locale} canManageKeys={role === "owner" || role === "admin"} onOpenApiKeys={() => onNavigate("/api-keys")} initialGuide={new URLSearchParams(location.search).get("guide")} />
      )
    );
  }

  if (path === "/messages") {
    return (
      shell(
        <MessagesWithSurveys surveys={surveys} locale={locale} />
      )
    );
  }

  if (path === "/domains") {
    return (
      shell(
        <DomainsPage client={domains} locale={locale} />
      )
    );
  }

  if (path === "/contacts") {
    return shell(<ContactsPage client={contacts} participants={participants} surveys={surveys} locale={locale} canEdit={canEditSurveys(me)} onCount={onContactsCount} saveFile={saveFile} />);
  }

  if (path === "/connections") {
    return shell(<ConnectionsPage client={connections} locale={locale} onNavigate={onNavigate} writeClipboard={(text) => navigator.clipboard.writeText(text)} />);
  }

  if (path === "/connections/new") {
    return shell(<ConnectPage client={connections} surveys={surveys} locale={locale} onNavigate={onNavigate} writeClipboard={(text) => navigator.clipboard.writeText(text)} />);
  }

  if (path === "/members") {
    return (
      shell(
        <MembersPage client={members} locale={locale} organisation={me.organisations.find((organisation) => organisation.id === me.currentOrganisationId)?.name ?? ""} />
      )
    );
  }

  if (path === "/sign-in-settings") {
    return (
      shell(
        <SignInPage client={signInSettings} locale={locale} />
      )
    );
  }

  if (path === "/api-keys") {
    return (
      shell(
        <ApiKeysPage client={apiKeys} webhooks={webhooks} oauthClients={oauthClients} locale={locale} makeConnection={(kind, id) => connections.adopt(kind, id).then(() => onNavigate("/connections"))} />
      )
    );
  }

  if (path === "/look") {
    return (
      shell(
        <LookPage client={themes} locale={locale} />
      )
    );
  }

  return shell(
      <SurveyListPage
        client={surveys}
        me={me}
        locale={locale}
        onOpen={(id) => onNavigate(`/surveys/${encodeURIComponent(id)}${canEditSurveys(me) ? "" : "/results"}`)}
        onOpenResults={(id) => onNavigate(`/surveys/${encodeURIComponent(id)}/results`)}
        address={location.search}
        onAddress={(query) => {
          listQuery = query;
          history.replaceState(null, "", `/${query}`);
        }}
      />,
  );
}

/** The Messages page with the organisation's surveys for its preview (published first, as respondents get them). */
function MessagesWithSurveys({ surveys: client, locale }: { surveys: SurveysClient; locale: HomeProps["locale"] }) {
  const [list, setList] = useState<{ id: string; title: string }[] | null>(null);

  useEffect(() => {
    client.list({}).then(
      (answer) => setList([...answer.items].sort((a, b) => Number(b.status === "published") - Number(a.status === "published")).map(({ id, title }) => ({ id, title }))),
      () => setList([]),
    );
  }, [client]);

  return list === null ? null : <MessagesPage client={messages} surveys={list} locale={locale} />;
}

const root = createRoot(document.getElementById("root")!);

if (import.meta.env.DEV && location.pathname === "/dev/controls") {
  const { ControlsPreview } = await import("./dev/ControlsPreview");
  root.render(
    <StrictMode>
      <ControlsPreview />
    </StrictMode>,
  );
} else if (import.meta.env.DEV && location.pathname === "/dev/designer") {
  const { DesignerPreview } = await import("./dev/DesignerPreview");
  root.render(
    <StrictMode>
      <DesignerPreview />
    </StrictMode>,
  );
} else if (/^\/embed\/(designer|results)$/.test(location.pathname)) {
  // The designer or Results inside a host product (AWK-13): the token is used once, so it leaves the address at once.
  const query = new URLSearchParams(location.search);
  const token = query.get("token") ?? "";
  query.delete("token");
  history.replaceState(null, "", `${location.pathname}${query.size > 0 ? `?${query.toString()}` : ""}`);
  root.render(
    <StrictMode>
      <EmbeddedApp
        token={token}
        view={location.pathname.endsWith("results") ? "results" : "designer"}
        lang={query.get("lang")}
        telemetry={telemetry}
        post={(event) => window.parent.postMessage(event, "*")}
        saveFile={saveFile}
      />
    </StrictMode>,
  );
} else {
  root.render(
    <StrictMode>
      <AccountApp
        client={auth}
        initialLocale={pickLocale(navigator.languages)}
        location={{ pathname: location.pathname, search: location.search }}
        navigate={(path) => history.replaceState(null, "", path)}
        home={Home}
        members={members}
        baseAddress={location.origin}
        timeZone={Intl.DateTimeFormat().resolvedOptions().timeZone}
      />
    </StrictMode>,
  );
}
