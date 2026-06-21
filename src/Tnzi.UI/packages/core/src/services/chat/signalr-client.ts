import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr';
import type { ChatMessageDto } from './types';

export interface NewMessagePayload {
  conversationId: string;
  messageId: string;
  senderId?: string | null;
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
  changeType: number;
}

export interface PresenceChangedPayload {
  userId: string;
  status: number; // UserPresenceStatus
  lastSeenAt?: string | null;
}

export interface ChatSignalRClientOptions {
  /** Hub URL, e.g. '/hubs/chat' (relative — resolved against page origin / vite proxy). */
  url: string;
  /** Returns the current JWT (called on each (re)connect). */
  accessTokenFactory: () => string;
}

type ChatEvent = 'Chat.NewMessage' | 'Chat.MessageRead' | 'Chat.ConversationChanged' | 'Chat.PresenceChanged';

export interface ChatSignalRClient {
  start(): Promise<void>;
  stop(): Promise<void>;
  isConnected(): boolean;
  on(event: ChatEvent, handler: (payload: unknown) => void): void;
  off(event: ChatEvent, handler: (payload: unknown) => void): void;
  readonly connection: HubConnection;
}

export function createChatSignalRClient(opts: ChatSignalRClientOptions): ChatSignalRClient {
  const connection = new HubConnectionBuilder()
    .withUrl(opts.url, { accessTokenFactory: opts.accessTokenFactory })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(LogLevel.Warning)
    .build();

  return {
    connection,
    isConnected: () => (connection.state as unknown as string) === 'Connected',
    async start() { if ((connection.state as unknown as string) !== 'Connected') await connection.start(); },
    async stop() { await connection.stop(); },
    on(event, handler) { connection.on(event, handler); },
    off(event, handler) { connection.off(event, handler); },
  };
}
