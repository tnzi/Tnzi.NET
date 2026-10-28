import type { AdminChatConfig } from './chat-config'
import type { AdminSettingsConfig } from './settings-config'

/**
 * Derive the chat / settings SignalR hub URLs from a single `apiBase`
 * (`defineAdminApp({ apiBase })`) so a consumer under an IIS sub-app (REST at
 * '/api') doesn't override each hub URL by hand.
 *
 * Rules:
 * - No `apiBase` → return the configs unchanged (root-relative '/hubs/*'
 *   defaults apply downstream). Opt-in.
 * - An explicit `chat.hubUrl` / `settings.hubUrl` always wins.
 * - Both hubs derive whether or not their config was passed. An omitted `chat`
 *   is the default chat, which is enabled (only `enabled: false` turns the
 *   launcher off), so it needs the derived URL as much as an explicit one;
 *   the settings realtime hub runs regardless.
 */
export function resolveHubConfigs(
  apiBase: string | undefined,
  chat: AdminChatConfig | undefined,
  settings: AdminSettingsConfig | undefined,
): { chat: AdminChatConfig | undefined; settings: AdminSettingsConfig | undefined } {
  const hubBase = (apiBase ?? '').replace(/\/+$/, '')

  const resolvedChat =
    hubBase && !chat?.hubUrl ? { ...(chat ?? {}), hubUrl: `${hubBase}/hubs/chat` } : chat

  const resolvedSettings =
    hubBase && !settings?.hubUrl
      ? { ...(settings ?? {}), hubUrl: `${hubBase}/hubs/settings` }
      : settings

  return { chat: resolvedChat, settings: resolvedSettings }
}
