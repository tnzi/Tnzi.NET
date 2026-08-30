import { describe, it, expect, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAdminThemeStore } from '../../src/stores/useAdminThemeStore'

const read = (name: string): string => document.documentElement.style.getPropertyValue(name)

describe('desktop theme surfaces', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    document.documentElement.removeAttribute('style')
  })

  it('writes and clears the wallpaper token', () => {
    const theme = useAdminThemeStore()
    theme.setDesktopWallpaperBg('#204070')
    expect(read('--tnzi-desktop-wallpaper')).toBe('#204070')

    theme.resetDesktopWallpaperBg()
    expect(read('--tnzi-desktop-wallpaper')).toBe('')
  })

  it('swaps the built-in blue blooms for neutral ones on a custom wallpaper', () => {
    const theme = useAdminThemeStore()
    // The defaults are tuned for the built-in blue; on another hue they read as
    // a second colour smeared across it.
    expect(read('--tnzi-desktop-glow-1')).toBe('')

    theme.setDesktopWallpaperBg('#7a2020')
    expect(read('--tnzi-desktop-glow-1')).toContain('255 255 255')
    expect(read('--tnzi-desktop-glow-2')).toContain('0 0 0')

    theme.resetDesktopWallpaperBg()
    expect(read('--tnzi-desktop-glow-1')).toBe('')
  })

  it('flips icon labels to dark on a light wallpaper', () => {
    const theme = useAdminThemeStore()
    theme.setDesktopWallpaperBg('#f2f4f8')
    // White labels on a pale wallpaper are unreadable no matter what the
    // text-shadow does.
    expect(read('--tnzi-desktop-icon-color')).toContain('surface-light-text')

    theme.setDesktopWallpaperBg('#101828')
    expect(read('--tnzi-desktop-icon-color')).toBe('')
  })

  it('flips taskbar text with the taskbar colour', () => {
    const theme = useAdminThemeStore()
    theme.setDesktopTaskbarBg('#ffffff')
    expect(read('--tnzi-desktop-taskbar-fg')).toContain('surface-light-text')

    theme.setDesktopTaskbarBg('#111111')
    expect(read('--tnzi-desktop-taskbar-fg')).toContain('inverted-text')

    theme.resetDesktopTaskbarBg()
    expect(read('--tnzi-desktop-taskbar-bg')).toBe('')
    expect(read('--tnzi-desktop-taskbar-fg')).toBe('')
  })

  it('flips the window title bar the same way', () => {
    const theme = useAdminThemeStore()
    theme.setDesktopWindowBarBg('#1b1b1b')
    expect(read('--tnzi-desktop-window-bar-fg')).toContain('inverted-text')
  })

  describe('wallpaper image', () => {
    it('accepts an https url and wraps it for css', () => {
      const theme = useAdminThemeStore()
      theme.setDesktopWallpaperImage('https://cdn.example.com/bg.jpg')
      expect(read('--tnzi-desktop-wallpaper-image')).toBe('url("https://cdn.example.com/bg.jpg")')
    })

    it('accepts a same-origin path and a data image', () => {
      const theme = useAdminThemeStore()
      theme.setDesktopWallpaperImage('/api/files/abc/preview')
      expect(read('--tnzi-desktop-wallpaper-image')).toContain('/api/files/abc/preview')

      theme.setDesktopWallpaperImage('data:image/png;base64,AAAA')
      expect(read('--tnzi-desktop-wallpaper-image')).toContain('data:image/png')
    })

    it('refuses anything that could break out of the url() token', () => {
      const theme = useAdminThemeStore()
      // The value ships to every user through the global theme snapshot, so it
      // is not the author's own browser it can break.
      for (const hostile of [
        'https://x/a.png"); background: red; --x: url("',
        'https://x/a.png) ; color: red',
        'javascript:alert(1)',
        'https://x/a b.png',
      ]) {
        theme.setDesktopWallpaperImage(hostile)
        expect(read('--tnzi-desktop-wallpaper-image')).toBe('')
      }
    })

    it('clamps the dim and clears it with the image', () => {
      const theme = useAdminThemeStore()
      theme.setDesktopWallpaperImage('https://cdn.example.com/bg.jpg')
      theme.setDesktopWallpaperScrim(200)
      expect(theme.desktopWallpaperScrim).toBe(80)
      expect(read('--tnzi-desktop-wallpaper-scrim')).toBe('0.8')

      theme.resetDesktopWallpaperImage()
      expect(read('--tnzi-desktop-wallpaper-image')).toBe('')
      expect(read('--tnzi-desktop-wallpaper-scrim')).toBe('')
    })

    it('darkens icon labels under a heavy dim regardless of the picture', () => {
      const theme = useAdminThemeStore()
      theme.setDesktopWallpaperImage('https://cdn.example.com/bright-sky.jpg')
      theme.setDesktopWallpaperScrim(50)
      // A heavy scrim is dark whatever the photo is, so white labels are right.
      expect(read('--tnzi-desktop-icon-color')).toBe('')
    })
  })

  it('clamps the taskbar height', () => {
    const theme = useAdminThemeStore()
    theme.setDesktopTaskbarHeight(500)
    expect(theme.desktopTaskbarHeight).toBe(72)
    expect(read('--tnzi-desktop-taskbar-height')).toBe('72px')
  })

  it('leaves the height token alone at the default', () => {
    const theme = useAdminThemeStore()
    // An inline `:root` style beats any stylesheet, so writing the default
    // number would stomp a consumer's own token override.
    theme.setDesktopTaskbarHeight(56)
    expect(read('--tnzi-desktop-taskbar-height')).toBe('56px')

    theme.setDesktopTaskbarHeight(40)
    expect(read('--tnzi-desktop-taskbar-height')).toBe('')
  })

  describe('vibrancy (the frosted-glass material)', () => {
    it('writes nothing at the default so the stylesheet keeps control', () => {
      const theme = useAdminThemeStore()
      expect(theme.desktopVibrancy).toBe(60)
      expect(read('--tnzi-desktop-solidity')).toBe('')
      expect(read('--tnzi-desktop-backdrop')).toBe('')
    })

    it('moves solidity and blur together', () => {
      const theme = useAdminThemeStore()
      // Transparency WITHOUT the blur is not frosted glass - it is a muddy
      // surface with the wallpaper legible straight through the text on it.
      theme.setDesktopVibrancy(100)
      expect(read('--tnzi-desktop-solidity')).toBe('60%')
      expect(read('--tnzi-desktop-backdrop')).toBe('blur(50px) saturate(150%)')
    })

    it('drops the filter entirely at zero rather than leaving blur(0px)', () => {
      const theme = useAdminThemeStore()
      // A no-op `blur(0px)` still promotes every chrome surface to its own
      // backdrop root and pays for a composite that changes nothing.
      theme.setDesktopVibrancy(0)
      expect(read('--tnzi-desktop-solidity')).toBe('100%')
      expect(read('--tnzi-desktop-backdrop')).toBe('none')
    })

    it('never lets the chrome get thinner than readable', () => {
      const theme = useAdminThemeStore()
      // The slider buys glass, not invisibility: titles and taskbar labels sit
      // over a wallpaper the user may replace with any photograph.
      theme.setDesktopVibrancy(500)
      expect(theme.desktopVibrancy).toBe(100)
      expect(Number.parseFloat(read('--tnzi-desktop-solidity'))).toBeGreaterThanOrEqual(60)
    })

    it('is perceptible across its whole travel, not just at the top', () => {
      const theme = useAdminThemeStore()
      // A first cut only spanned 100%->70%: a quarter along was 92.5% opaque,
      // which reads as solid. Half the control did nothing anyone could see.
      theme.setDesktopVibrancy(25)
      expect(Number.parseFloat(read('--tnzi-desktop-solidity'))).toBeLessThanOrEqual(90)
    })
  })

  it('factory reset takes the desktop surfaces with it', () => {
    const theme = useAdminThemeStore()
    theme.setDesktopWallpaperBg('#7a2020')
    theme.setDesktopWallpaperImage('https://cdn.example.com/bg.jpg')
    theme.setDesktopWallpaperScrim(70)
    theme.setDesktopTaskbarBg('#ffffff')
    theme.setDesktopTaskbarHeight(64)
    theme.setDesktopWindowBarBg('#1b1b1b')
    theme.setDesktopVibrancy(0)

    theme.reset()

    // Under the global-theme flow, Reset also PUBLISHES the factory snapshot to
    // every user - a surface left behind here goes out as if it were chosen.
    expect(theme.desktopWallpaperBg).toBeNull()
    expect(theme.desktopWallpaperImage).toBeNull()
    expect(theme.desktopWallpaperScrim).toBe(40)
    expect(theme.desktopTaskbarBg).toBeNull()
    expect(theme.desktopTaskbarHeight).toBe(40)
    expect(theme.desktopWindowBarBg).toBeNull()
    expect(theme.desktopVibrancy).toBe(60)
    for (const token of [
      '--tnzi-desktop-wallpaper',
      '--tnzi-desktop-wallpaper-image',
      '--tnzi-desktop-taskbar-bg',
      '--tnzi-desktop-taskbar-fg',
      '--tnzi-desktop-taskbar-height',
      '--tnzi-desktop-window-bar-bg',
      '--tnzi-desktop-icon-color',
      '--tnzi-desktop-solidity',
      '--tnzi-desktop-backdrop',
    ]) {
      expect(read(token)).toBe('')
    }
  })
})
