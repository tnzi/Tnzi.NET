import type { HubConnection } from '@microsoft/signalr';
import { createHubClient, type HubClientOptions } from '../signalr/hub-client';
import type { ChatMessageDto, UserPresenceStatus } from './types';

export interface NewMessagePayload {
  conversationId: string;
  messageId: string;
  senderId?: string | null;
  /**
   * Numeric MessageContentType. Stays a number: the SignalR handler emits an
   * anonymous object with an explicit `(int)` cast, so this scalar is NOT
   * affected by the global JsonStringEnumConverter. The full `message` body
   * below (a ChatMessageDto) carries the string-enum `contentType`.
   */
  contentType: number;
  preview: string;
  /** Full message body for incremental append (backend pushes it so clients need no refetch). */
  message?: ChatMessageDto | null;
}

export interface MessageReadPayload {
  conversationId: string;
  userId: string;
  readAt: string;
}

export interface ConversationChangedPayload {
  conversationId: string;
  /** Numeric ConversationChangeType - emitted via an explicit `(int)` cast, so
   *  it is not string-serialized (unlike DTO enum fields). */
  changeType: number;
}

export interface PresenceChangedPayload {
  userId: string;
  /** Effective UserPresenceStatus - pushed as a UserPresenceDto whose enum
   *  field IS string-serialized by the global converter. */
  status: UserPresenceStatus;
  lastSeenAt?: string | null;
}

export type ChatSignalRClientOptions = HubClientOptions;

type ChatEvent = 'Chat.NewMessage' | 'Chat.MessageRead' | 'Chat.ConversationChanged' | 'Chat.PresenceChanged';

export interface ChatSignalRClient {
  start(): Promise<void>;
  stop(): Promise<void>;
  isConnected(): boolean;
  on(event: ChatEvent, handler: (payload: unknown) => void): void;
  off(event: ChatEvent, handler: (payload: unknown) => void): void;
  readonly connection: HubConnection;
}

/**
 * Client for the `/hubs/chat` hub. Connection plumbing (auto-reconnect,
 * state-safe `start()`) lives in the shared {@link createHubClient} factory.
 */
export function createChatSignalRClient(opts: ChatSignalRClientOptions): ChatSignalRClient {
  return createHubClient<ChatEvent>(opts);
}
