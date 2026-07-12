/**
 * Chat sound library — WebAudio-synthesised message tones.
 *
 * All effects are generated at runtime from oscillators, so there are NO binary
 * assets to bundle, NO network requests (CSP-safe), and every deployment gets
 * the same sound regardless of hosting. Two families, tuned from how mainstream
 * chat apps design their audio:
 *
 *  - **Attention** (notification): longer, multi-note, rising/falling motifs that
 *    cut through when the window is closed or the message is in another thread
 *    (Chime / DingDong / TriTone / Marimba / Pulse / Bell).
 *  - **Subtle** (in-conversation): a single short, low-volume blip that is pure
 *    UX feedback while you are actively chatting (Pop / Tick / Blip / Soft / Drop).
 *
 * `ChatSoundEffect.None` is silent for that category.
 */
import { ChatSoundEffect } from '@tnzi/core/services/chat'

/** A recipe schedules oscillators/gains on the shared context starting at `now`. */
type SoundRecipe = (ctx: AudioContext, now: number) => void

interface ToneSpec {
  /** Constant frequency, or `[from, to]` for an exponential glide over `dur`. */
  freq: number | [number, number]
  type?: OscillatorType
  /** Start offset from `now`, seconds. */
  start: number
  /** Duration, seconds. */
  dur: number
  /** Peak gain (0..1). */
  gain?: number
  /** Attack ramp, seconds (default 5ms). */
  attack?: number
}

/** Schedule a single enveloped tone. Shared building block for every recipe. */
function tone(ctx: AudioContext, now: number, s: ToneSpec): void {
  const osc = ctx.createOscillator()
  const g = ctx.createGain()
  osc.connect(g)
  g.connect(ctx.destination)
  osc.type = s.type ?? 'sine'
  const t0 = now + s.start
  const peak = s.gain ?? 0.15
  const attack = s.attack ?? 0.005
  if (Array.isArray(s.freq)) {
    osc.frequency.setValueAtTime(s.freq[0], t0)
    osc.frequency.exponentialRampToValueAtTime(Math.max(1, s.freq[1]), t0 + s.dur)
  } else {
    osc.frequency.setValueAtTime(s.freq, t0)
  }
  // Exponential envelope (can't ramp to exactly 0 — use a tiny floor).
  g.gain.setValueAtTime(0.0001, t0)
  g.gain.exponentialRampToValueAtTime(peak, t0 + attack)
  g.gain.exponentialRampToValueAtTime(0.0001, t0 + s.dur)
  osc.start(t0)
  osc.stop(t0 + s.dur + 0.02)
}

// ── Recipes ─────────────────────────────────────────────────────────────────
const RECIPES: Partial<Record<ChatSoundEffect, SoundRecipe>> = {
  // Attention family
  [ChatSoundEffect.Chime]: (c, n) => {
    tone(c, n, { freq: 988, type: 'triangle', start: 0, dur: 0.38, gain: 0.16 })
    tone(c, n, { freq: 659, type: 'triangle', start: 0.12, dur: 0.46, gain: 0.16 })
  },
  [ChatSoundEffect.DingDong]: (c, n) => {
    tone(c, n, { freq: 659, type: 'triangle', start: 0, dur: 0.28, gain: 0.18 })
    tone(c, n, { freq: 523, type: 'triangle', start: 0.22, dur: 0.5, gain: 0.18 })
  },
  [ChatSoundEffect.TriTone]: (c, n) => {
    tone(c, n, { freq: 523, start: 0, dur: 0.1, gain: 0.14 })
    tone(c, n, { freq: 659, start: 0.09, dur: 0.1, gain: 0.14 })
    tone(c, n, { freq: 784, start: 0.18, dur: 0.24, gain: 0.16 })
  },
  [ChatSoundEffect.Marimba]: (c, n) => {
    tone(c, n, { freq: 587, type: 'triangle', start: 0, dur: 0.14, gain: 0.16, attack: 0.008 })
    tone(c, n, { freq: 784, type: 'triangle', start: 0.1, dur: 0.14, gain: 0.16, attack: 0.008 })
    tone(c, n, { freq: 880, type: 'triangle', start: 0.2, dur: 0.3, gain: 0.18, attack: 0.008 })
  },
  [ChatSoundEffect.Pulse]: (c, n) => {
    tone(c, n, { freq: 620, type: 'square', start: 0, dur: 0.07, gain: 0.07 })
    tone(c, n, { freq: 620, type: 'square', start: 0.13, dur: 0.09, gain: 0.07 })
  },
  [ChatSoundEffect.Bell]: (c, n) => {
    tone(c, n, { freq: 660, type: 'sine', start: 0, dur: 0.6, gain: 0.16 })
    tone(c, n, { freq: 1320, type: 'sine', start: 0, dur: 0.45, gain: 0.06 })
    tone(c, n, { freq: 1980, type: 'sine', start: 0, dur: 0.3, gain: 0.03 })
  },
  // Subtle family
  [ChatSoundEffect.Pop]: (c, n) => {
    tone(c, n, { freq: [520, 380], type: 'sine', start: 0, dur: 0.1, gain: 0.12 })
  },
  [ChatSoundEffect.Tick]: (c, n) => {
    tone(c, n, { freq: 1200, type: 'sine', start: 0, dur: 0.035, gain: 0.08 })
  },
  [ChatSoundEffect.Blip]: (c, n) => {
    tone(c, n, { freq: 660, type: 'sine', start: 0, dur: 0.09, gain: 0.1 })
  },
  [ChatSoundEffect.Soft]: (c, n) => {
    tone(c, n, { freq: 523, type: 'triangle', start: 0, dur: 0.16, gain: 0.09, attack: 0.03 })
  },
  [ChatSoundEffect.Drop]: (c, n) => {
    tone(c, n, { freq: [420, 820], type: 'sine', start: 0, dur: 0.05, gain: 0.1 })
    tone(c, n, { freq: [820, 500], type: 'sine', start: 0.05, dur: 0.09, gain: 0.11 })
  },
}

/** Ordered presets for the notification (attention) category — for UI/docs. */
export const NOTIFICATION_SOUND_EFFECTS: ChatSoundEffect[] = [
  ChatSoundEffect.None,
  ChatSoundEffect.Chime,
  ChatSoundEffect.DingDong,
  ChatSoundEffect.TriTone,
  ChatSoundEffect.Marimba,
  ChatSoundEffect.Pulse,
  ChatSoundEffect.Bell,
]

/** Ordered presets for the in-conversation (subtle) category — for UI/docs. */
export const MESSAGE_SOUND_EFFECTS: ChatSoundEffect[] = [
  ChatSoundEffect.None,
  ChatSoundEffect.Pop,
  ChatSoundEffect.Tick,
  ChatSoundEffect.Blip,
  ChatSoundEffect.Soft,
  ChatSoundEffect.Drop,
]

// ── Shared AudioContext + autoplay unlock ────────────────────────────────────
let ctx: AudioContext | null = null
function getCtx(): AudioContext | null {
  try {
    const Ctor =
      (globalThis as unknown as { AudioContext?: typeof AudioContext; webkitAudioContext?: typeof AudioContext }).AudioContext ??
      (globalThis as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
    if (!Ctor) return null
    ctx ??= new Ctor()
    if (ctx.state === 'suspended') void ctx.resume()
    return ctx
  } catch {
    return null
  }
}

// Browsers create an AudioContext 'suspended' and only resume it from inside a
// user-gesture handler. The first message arrives without a gesture, so resume
// the context on the first pointer/keyboard interaction anywhere in the app.
let unlockBound = false
export function unlockChatAudio(): void {
  if (unlockBound || typeof window === 'undefined') return
  unlockBound = true
  const handler = () => {
    const c = getCtx()
    if (c && c.state === 'suspended') void c.resume()
    window.removeEventListener('pointerdown', handler)
    window.removeEventListener('keydown', handler)
  }
  window.addEventListener('pointerdown', handler)
  window.addEventListener('keydown', handler)
}

/** Play a preset by id. Unknown / `None` / empty is a silent no-op. */
export function playSoundEffect(effect: ChatSoundEffect | string | null | undefined): void {
  if (!effect || effect === ChatSoundEffect.None) return
  const recipe = RECIPES[effect as ChatSoundEffect]
  if (!recipe) return
  try {
    const c = getCtx()
    if (!c) return
    recipe(c, c.currentTime)
  } catch {
    /* autoplay blocked / no audio device — silent */
  }
}

/** True for the two chat-sound Select settings (`Chat:NotificationSound` / `Chat:MessageSound`). */
export function isChatSoundSettingKey(key: string): boolean {
  return /:(notificationSound|messageSound)$/i.test(key)
}
