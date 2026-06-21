import { ref } from 'vue'

let ctx: AudioContext | null = null
function getCtx(): AudioContext | null {
  try {
    const Ctor = (globalThis as unknown as { AudioContext?: typeof AudioContext; webkitAudioContext?: typeof AudioContext })
      .AudioContext ?? (globalThis as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
    if (!Ctor) return null
    ctx ??= new Ctor()
    if (ctx.state === 'suspended') void ctx.resume()
    return ctx
  } catch { return null }
}

// Browsers create an AudioContext in the 'suspended' state and only let it
// resume from inside a user-gesture handler. The first chime is triggered by an
// incoming message (not a gesture), so without this it is silently dropped.
// Resume the context on the first pointer/keyboard interaction anywhere in the
// app — after that every subsequent chime plays.
let unlockBound = false
function bindUnlock() {
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

const enabled = ref(true)

export function useNotificationSound() {
  bindUnlock()
  function setEnabled(b: boolean) { enabled.value = b }
  function play() {
    if (!enabled.value) return
    try {
      const c = getCtx(); if (!c) return
      const now = c.currentTime
      const osc = c.createOscillator(); const gain = c.createGain()
      osc.connect(gain); gain.connect(c.destination)
      osc.frequency.setValueAtTime(880, now)
      osc.frequency.setValueAtTime(660, now + 0.08)
      gain.gain.setValueAtTime(0.18, now)
      gain.gain.exponentialRampToValueAtTime(0.0001, now + 0.22)
      osc.start(now); osc.stop(now + 0.22)
    } catch { /* autoplay blocked / no audio — silent */ }
  }
  return { play, setEnabled, enabled }
}
