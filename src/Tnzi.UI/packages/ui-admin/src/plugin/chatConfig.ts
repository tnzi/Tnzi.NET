import type { App, InjectionKey } from 'vue'
import { inject } from 'vue'

export interface AdminChatConfig {
  /** When false, hides the chat launcher from the admin header. Default: true. */
  enabled?: boolean
  /**
   * Override the SignalR chat hub URL. Default '/hubs/chat' (root-relative,
   * resolved against the page origin). Set e.g. '/api/hubs/chat' when the API
   * is hosted under a sub-path.
   */
  hubUrl?: string
}

export const ADMIN_CHAT_CONFIG_KEY: InjectionKey<AdminChatConfig> = Symbol(
  'tnzi-admin-chat-config',
)

export function provideAdminChatConfig(app: App, config: AdminChatConfig): void {
  app.provide(ADMIN_CHAT_CONFIG_KEY, config)
}

export function useAdminChatConfig(): AdminChatConfig | null {
  return inject(ADMIN_CHAT_CONFIG_KEY, null)
}
