// SPDX-License-Identifier: AGPL-3.0-only
// A hand-written MessagesClient for the Messages page's tests.
import { ApiError, type EmailPreview, type EmailRequest, type MessageKind, type MessageText, type MessagesClient, type SavedMessage } from "@askwerk/app-client";

const both = (de: string, en: string) => ({ de, en });

export const defaultMessages: MessageText[] = [
  { kind: "invitation", subject: both("{organisation}: Ihre Meinung zählt", "{organisation}: your opinion counts"), body: both("Guten Tag {name},\n\n{link}", "Hello {name},\n\n{link}"), answerInEmail: true, isDefault: true },
  { kind: "reminder", subject: both("Erinnerung: {survey}", "Reminder: {survey}"), body: both("Noch bis {deadline}.\n\n{link}", "Until {deadline}.\n\n{link}"), answerInEmail: false, isDefault: true },
  { kind: "thanks", subject: both("Danke", "Thank you"), body: both("Danke.", "Thank you."), answerInEmail: false, isDefault: true },
];

export class FakeMessagesClient implements MessagesClient {
  readonly calls: [string, ...unknown[]][] = [];
  testAnswer: "ok" | ApiError = "ok";

  list = () => {
    this.calls.push(["list"]);
    return Promise.resolve(defaultMessages);
  };

  save = (kind: MessageKind, message: SavedMessage) => {
    this.calls.push(["save", kind, message]);
    return Promise.resolve();
  };

  preview = (kind: MessageKind, request: EmailRequest) => {
    this.calls.push(["preview", kind, request]);
    return Promise.resolve<EmailPreview>({ subject: "Acme GmbH: your opinion counts", html: `<table><tr><td>Preview of ${request.surveyId}</td></tr></table>`, text: "Hello" });
  };

  sendTest = (kind: MessageKind, request: EmailRequest) => {
    this.calls.push(["sendTest", kind, request]);
    return this.testAnswer === "ok" ? Promise.resolve() : Promise.reject(this.testAnswer);
  };
}

export function emailNotSetUp(): ApiError {
  return new ApiError(409, "4bf92f3577b34da6a3ce929d0e0e4736", "conflict", { code: "email-not-set-up" });
}
