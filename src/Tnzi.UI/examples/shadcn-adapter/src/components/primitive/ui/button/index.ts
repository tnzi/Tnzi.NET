import type { VariantProps } from "class-variance-authority"
import { cva } from "class-variance-authority"

export { default as Button } from "./Button.vue"

/**
 * Button variants — naive-ui style
 *
 * Sizes: tiny(22px) / small(28px) / medium(34px) / large(40px)
 * Border-radius: 3px (var(--radius))
 * Transition: .3s cubic-bezier(.4,0,.2,1)
 * Focus: box-shadow ring (no outline)
 * Default type: transparent bg + border, hover changes border/text to primary
 */
export const buttonVariants = cva(
  "inline-flex items-center justify-center gap-1.5 whitespace-nowrap rounded-md font-normal cursor-pointer select-none transition-[color,background-color,border-color,box-shadow,opacity] duration-300 ease-[cubic-bezier(.4,0,.2,1)] outline-none disabled:opacity-50 disabled:cursor-not-allowed [&_svg]:pointer-events-none [&_svg:not([class*='size-'])]:size-[18px] shrink-0 [&_svg]:shrink-0",
  {
    variants: {
      variant: {
        // naive-ui default: transparent bg, gray border, hover → primary border+text
        default:
          "border border-border bg-transparent text-foreground hover:border-primary-hover hover:text-primary-hover active:border-primary-pressed active:text-primary-pressed focus-visible:border-primary-hover focus-visible:text-primary-hover",
        // outline = alias of default (backward compat)
        outline:
          "border border-border bg-transparent text-foreground hover:border-primary-hover hover:text-primary-hover active:border-primary-pressed active:text-primary-pressed focus-visible:border-primary-hover focus-visible:text-primary-hover",
        // naive-ui primary: solid bg, white text, hover/pressed darken
        primary:
          "border border-primary bg-primary text-primary-foreground hover:bg-primary-hover hover:border-primary-hover active:bg-primary-pressed active:border-primary-pressed focus-visible:bg-primary-hover focus-visible:border-primary-hover",
        // naive-ui error/destructive
        destructive:
          "border border-destructive bg-destructive text-destructive-foreground hover:brightness-110 active:brightness-90 focus-visible:brightness-110",
        // naive-ui info
        info:
          "border border-info bg-info text-info-foreground hover:brightness-110 active:brightness-90 focus-visible:brightness-110",
        // naive-ui warning
        warning:
          "border border-warning bg-warning text-warning-foreground hover:brightness-110 active:brightness-90 focus-visible:brightness-110",
        // naive-ui success
        success:
          "border border-success bg-success text-success-foreground hover:brightness-110 active:brightness-90 focus-visible:brightness-110",
        // naive-ui secondary/tertiary: subtle gray bg
        secondary:
          "border-0 bg-[rgba(46,51,56,0.05)] text-foreground hover:bg-[rgba(46,51,56,0.09)] active:bg-[rgba(46,51,56,0.13)] dark:bg-[rgba(255,255,255,0.08)] dark:hover:bg-[rgba(255,255,255,0.12)] dark:active:bg-[rgba(255,255,255,0.08)]",
        // naive-ui quaternary: transparent, hover shows bg (no border)
        ghost:
          "border border-transparent bg-transparent text-foreground hover:bg-[rgba(46,51,56,0.09)] hover:border-border/50 active:bg-[rgba(46,51,56,0.13)] dark:hover:bg-[rgba(255,255,255,0.12)] dark:active:bg-[rgba(255,255,255,0.08)]",
        // naive-ui text variant: text only, hover underline
        link: "border-0 bg-transparent text-primary hover:text-primary-hover active:text-primary-pressed hover:underline underline-offset-4",
      },
      size: {
        // naive-ui: tiny=22px, small=28px, medium=34px, large=40px
        "xs":      "h-[22px] px-[6px] text-xs [&_svg:not([class*='size-'])]:size-[14px]",
        "sm":      "h-[28px] px-[10px] text-sm [&_svg:not([class*='size-'])]:size-[18px]",
        "default": "h-[34px] px-[14px] text-sm [&_svg:not([class*='size-'])]:size-[18px]",
        "lg":      "h-[40px] px-[18px] text-lg [&_svg:not([class*='size-'])]:size-[20px]",
        "icon":    "size-[34px] p-0",
        "icon-sm": "size-[28px] p-0",
        "icon-xs": "size-[22px] p-0",
        "icon-lg": "size-[40px] p-0",
      },
    },
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  },
)
export type ButtonVariants = VariantProps<typeof buttonVariants>
