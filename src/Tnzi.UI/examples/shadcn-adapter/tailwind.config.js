/** @type {import('tailwindcss').Config} */
export default {
  darkMode: ['class'],
  content: [
    './index.html',
    './src/**/*.{vue,js,ts}',
    '../../packages/core/src/**/*.{js,ts}',
  ],
  theme: {
    extend: {
      colors: {
        border: 'hsl(var(--border))',
        input: 'hsl(var(--input))',
        ring: 'hsl(var(--ring))',
        placeholder: 'hsl(var(--placeholder))',
        background: 'hsl(var(--background))',
        foreground: 'hsl(var(--foreground))',
        primary: {
          DEFAULT: 'hsl(var(--primary))',
          foreground: 'hsl(var(--primary-foreground))',
          hover: 'hsl(var(--primary-hover))',
          pressed: 'hsl(var(--primary-pressed))',
        },
        secondary: {
          DEFAULT: 'hsl(var(--secondary))',
          foreground: 'hsl(var(--secondary-foreground))',
        },
        destructive: {
          DEFAULT: 'hsl(var(--destructive))',
          foreground: 'hsl(var(--destructive-foreground))',
        },
        info: {
          DEFAULT: 'hsl(var(--info))',
          foreground: 'hsl(var(--info-foreground))',
        },
        warning: {
          DEFAULT: 'hsl(var(--warning))',
          foreground: 'hsl(var(--warning-foreground))',
        },
        success: {
          DEFAULT: 'hsl(var(--success))',
          foreground: 'hsl(var(--success-foreground))',
        },
        muted: {
          DEFAULT: 'hsl(var(--muted))',
          foreground: 'hsl(var(--muted-foreground))',
        },
        accent: {
          DEFAULT: 'hsl(var(--accent))',
          foreground: 'hsl(var(--accent-foreground))',
        },
        card: {
          DEFAULT: 'hsl(var(--card))',
          foreground: 'hsl(var(--card-foreground))',
        },
        popover: {
          DEFAULT: 'hsl(var(--popover))',
          foreground: 'hsl(var(--popover-foreground))',
        },
      },
      borderRadius: {
        xl: 'calc(var(--radius) + 4px)',
        lg: 'calc(var(--radius) + 2px)',
        md: 'var(--radius)',
        sm: 'calc(var(--radius) - 1px)',
      },
      boxShadow: {
        'sm': 'var(--shadow-1)',
        'DEFAULT': 'var(--shadow-1)',
        'md': 'var(--shadow-2)',
        'lg': 'var(--shadow-3)',
      },
      transitionTimingFunction: {
        'n-ease': 'cubic-bezier(.4, 0, .2, 1)',
        'n-ease-out': 'cubic-bezier(0, 0, .2, 1)',
        'n-ease-in': 'cubic-bezier(.4, 0, 1, 1)',
      },
      transitionDuration: {
        'DEFAULT': '300ms',
      },
      keyframes: {
        'wave-spread': {
          from: { 'box-shadow': '0 0 0.5px 0 hsl(var(--primary))' },
          to: { 'box-shadow': '0 0 0.5px 4.5px hsl(var(--primary))' },
        },
        'wave-opacity': {
          from: { opacity: '0.6' },
          to: { opacity: '0' },
        },
        'fade-in-scale-up': {
          from: { opacity: '0', transform: 'scale(0.9)' },
          to: { opacity: '1', transform: 'scale(1)' },
        },
      },
      animation: {
        'wave': 'wave-spread 0.6s cubic-bezier(0,0,.2,1), wave-opacity 0.6s cubic-bezier(0,0,.2,1)',
        'fade-in-scale': 'fade-in-scale-up 0.2s cubic-bezier(0,0,.2,1)',
      },
      fontSize: {
        'xs': ['12px', { lineHeight: '1.6' }],
        'sm': ['14px', { lineHeight: '1.6' }],
        'base': ['14px', { lineHeight: '1.6' }],
        'lg': ['15px', { lineHeight: '1.6' }],
        'xl': ['16px', { lineHeight: '1.6' }],
      },
    },
  },
  plugins: [],
}
