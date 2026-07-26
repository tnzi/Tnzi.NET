import { ref } from 'vue'
import { ChatSoundEffect } from '@tnzi/core/services/chat'
import { playSoundEffect, unlockChatAudio } from './chatSounds'

/**
 * Two-tier chat sound player.
 *
 * Config is module-level so every `useChatSound()` call shares it: `TChatHost`
 * configures it once from the deployment config, and any component (e.g. the
 * composer in `TChatWindow`) can play the same tones.
 *
 *  - `playNotification()` - attention tone for a message while the window is
 *    closed or in a non-active conversation.
 *  - `playMessage()` - subtle tone for send/receive within the active thread.
 *
 * Both respect the master `enabled` flag and the per-category effect (`None` =
 * silent). `preview()` always plays (an explicit user action, e.g. in Settings).
 */
const enabled = ref(true)
const notificationEffect = ref<ChatSoundEffect>(ChatSoundEffect.Chime)
const messageEffect = ref<ChatSoundEffect>(ChatSoundEffect.Pop)

export function useChatSound() {
  unlockChatAudio()

  function configure(opts: { enabled?: boolean; notification?: ChatSoundEffect; message?: ChatSoundEffect }): void {
    if (opts.enabled !== undefined) enabled.value = opts.enabled
    if (opts.notification !== undefined) notificationEffect.value = opts.notification
    if (opts.message !== undefined) messageEffect.value = opts.message
  }

  function playNotification(): void {
    if (enabled.value) playSoundEffect(notificationEffect.value)
  }

  function playMessage(): void {
    if (enabled.value) playSoundEffect(messageEffect.value)
  }

  /** Play a specific effect regardless of `enabled` - for previews. */
  function preview(effect: ChatSoundEffect): void {
    playSoundEffect(effect)
  }

  return { configure, playNotification, playMessage, preview, enabled, notificationEffect, messageEffect }
}
