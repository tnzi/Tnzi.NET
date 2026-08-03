import type { AdminSettingsConfig } from './settings-config'

/** The inline chat config shape carried by `defineAdminApp({ chat })`. */
export interface AdminChatConfig {
  enabled?: boolean
  hubUrl?: string
}

/**
 * Derive the chat / settings SignalR hub URLs from a single `apiBase`
 * (`defineAdminApp({ apiBase })`) so a consumer under an IIS sub-app (REST at
 * '/api') doesn't override each hub URL by hand.
 *
 * Rules:
 * - No `apiBase` → return the configs unchanged (root-relative '/hubs/*'
 *   defaults apply downstream). Opt-in.
 * - An explicit `chat.hubUrl` / `settings.hubUrl` always wins.
 * - Chat is only derived when the consumer opted into chat (a bare `{ hubUrl }`
 *   would enable the launcher). The settings realtime hub runs regardless, so it
 *   derives even without a settings config.
 */
export function resolveHubConfigs(
  apiBase: string | undefined,
  chat: AdminChatConfig | undefined,
  settings: AdminSettingsConfig | undefined,
): { chat: AdminChatConfig | undefined; settings: AdminSettingsConfig | undefined } {
  const hubBase = (apiBase ?? '').replace(/\/+$/, '')

  const resolvedChat = chat
    ? hubBase && !chat.hubUrl
      ? { ...chat, hubUrl: `${hubBase}/hubs/chat` }
      : chat
    : undefined

  const resolvedSettings =
    hubBase && !settings?.hubUrl
      ? { ...(settings ?? {}), hubUrl: `${hubBase}/hubs/settings` }
      : settings

  return { chat: resolvedChat, settings: resolvedSettings }
}
