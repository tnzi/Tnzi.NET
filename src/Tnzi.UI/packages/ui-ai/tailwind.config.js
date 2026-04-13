import typography from '@tailwindcss/typography';

/** @type {import('tailwindcss').Config} */
export default {
  darkMode: 'class',
  content: ['./src/**/*.{vue,ts,tsx}'],
  theme: {
    extend: {
      colors: {
        // 基础 shadcn 色彩系统 (与 @tnzi/ui 保持一致)
        border: 'hsl(var(--border))',
        input: 'hsl(var(--input))',
        ring: 'hsl(var(--ring))',
        background: 'hsl(var(--background))',
        foreground: 'hsl(var(--foreground))',
        primary: {
          DEFAULT: 'hsl(var(--primary))',
          foreground: 'hsl(var(--primary-foreground))',
        },
        secondary: {
          DEFAULT: 'hsl(var(--secondary))',
          foreground: 'hsl(var(--secondary-foreground))',
        },
        muted: {
          DEFAULT: 'hsl(var(--muted))',
          foreground: 'hsl(var(--muted-foreground))',
        },
        accent: {
          DEFAULT: 'hsl(var(--accent))',
          foreground: 'hsl(var(--accent-foreground))',
        },
        destructive: {
          DEFAULT: 'hsl(var(--destructive))',
          foreground: 'hsl(var(--destructive-foreground))',
        },
        // AI 语义色彩
        ai: {
          'user-bubble': 'hsl(var(--ai-user-bubble))',
          'assistant-bubble': 'hsl(var(--ai-assistant-bubble))',
          'reasoning-bg': 'hsl(var(--ai-reasoning-bg))',
          'tool-call-bg': 'hsl(var(--ai-tool-call-bg))',
          'streaming-cursor': 'hsl(var(--ai-streaming-cursor))',
          'code-bg': 'hsl(var(--ai-code-bg))',
          'node-active': 'hsl(var(--ai-node-active))',
          'node-completed': 'hsl(var(--ai-node-completed))',
          'node-failed': 'hsl(var(--ai-node-failed))',
          'handoff-accent': 'hsl(var(--ai-handoff-accent))',
        },
      },
      borderRadius: {
        lg: 'var(--radius)',
        md: 'calc(var(--radius) - 2px)',
        sm: 'calc(var(--radius) - 4px)',
      },
      keyframes: {
        'fade-in': {
          from: { opacity: '0', transform: 'translateY(4px)' },
          to: { opacity: '1', transform: 'translateY(0)' },
        },
        shimmer: {
          '0%': { backgroundPosition: '-200% 0' },
          '100%': { backgroundPosition: '200% 0' },
        },
      },
      animation: {
        'fade-in': 'fade-in 0.2s ease-out',
        shimmer: 'shimmer 2s linear infinite',
      },
    },
  },
  plugins: [typography],
};
