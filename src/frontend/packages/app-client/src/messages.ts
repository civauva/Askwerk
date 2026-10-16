// SPDX-License-Identifier: AGPL-3.0-only
import { createRequest, type AppClientOptions } from "./client";

export type MessageKind = "invitation" | "reminder" | "thanks";

/** Texts per language code. */
export type Texts = Readonly<Record<string, string>>;

/** One message text; Askwerk's own while isDefault. */
export interface MessageText {
  readonly kind: MessageKind;
  readonly subject: Texts;
  readonly body: Texts;
  readonly answerInEmail: boolean;
  readonly isDefault: boolean;
}

export interface SavedMessage {
  readonly subject: Texts;
  readonly body: Texts;
  readonly answerInEmail: boolean;
}

/** Which email to draw; texts not saved yet may come along for the preview. */
export interface EmailRequest {
  readonly surveyId: string;
  readonly language: string;
  readonly subject?: Texts;
  readonly body?: Texts;
  readonly answerInEmail?: boolean;
}

export interface EmailPreview {
  readonly subject: string;
  readonly html: string;
  readonly text: string;
}

/** Message texts of the current organisation (AWK-17 step 6): list, save, preview, test send to the signed-in user. */
export interface MessagesClient {
  list(): Promise<MessageText[]>;
  save(kind: MessageKind, message: SavedMessage): Promise<void>;
  preview(kind: MessageKind, request: EmailRequest): Promise<EmailPreview>;
  sendTest(kind: MessageKind, request: EmailRequest): Promise<void>;
}

export function createMessagesClient(options: AppClientOptions): MessagesClient {
  const request = createRequest(options);
  const at = (kind: MessageKind) => `/api/app/messages/${kind}`;

  return {
    list: () => request<{ items: MessageText[] }>("GET", "/api/app/messages").then((answer) => answer.items),
    save: (kind, message) => request("PUT", at(kind), message),
    preview: (kind, body) => request<EmailPreview>("POST", `${at(kind)}/preview`, body),
    sendTest: (kind, body) => request("POST", `${at(kind)}/test`, body),
  };
}
