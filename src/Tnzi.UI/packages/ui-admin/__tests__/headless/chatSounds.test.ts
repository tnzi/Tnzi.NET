import { describe, it, expect } from 'vitest'
import {
  isChatSoundSettingKey,
  NOTIFICATION_SOUND_EFFECTS,
  MESSAGE_SOUND_EFFECTS,
} from '../../src/headless/chatSounds'
import { ChatSoundEffect } from '@tnzi/core/services/chat'

describe('chatSounds helpers', () => {
  it('isChatSoundSettingKey matches the two chat sound setting keys (any casing)', () => {
    expect(isChatSoundSettingKey('Chat:NotificationSound')).toBe(true)
    expect(isChatSoundSettingKey('Chat:MessageSound')).toBe(true)
    expect(isChatSoundSettingKey('chat:notificationSound')).toBe(true)
    expect(isChatSoundSettingKey('Chat:EnableMessageSound')).toBe(false)
    expect(isChatSoundSettingKey('AI:DefaultModel')).toBe(false)
  })

  it('each category lists None first and only presets from its family', () => {
    expect(NOTIFICATION_SOUND_EFFECTS[0]).toBe(ChatSoundEffect.None)
    expect(MESSAGE_SOUND_EFFECTS[0]).toBe(ChatSoundEffect.None)
    expect(NOTIFICATION_SOUND_EFFECTS).toContain(ChatSoundEffect.Chime)
    expect(MESSAGE_SOUND_EFFECTS).toContain(ChatSoundEffect.Pop)
    // Families are disjoint apart from the shared None.
    const overlap = NOTIFICATION_SOUND_EFFECTS.filter(
      (e) => e !== ChatSoundEffect.None && MESSAGE_SOUND_EFFECTS.includes(e),
    )
    expect(overlap).toHaveLength(0)
  })
})
