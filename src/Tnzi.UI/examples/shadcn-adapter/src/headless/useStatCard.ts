import { computed, type ComputedRef } from 'vue'

export type StatCardColor = 'blue' | 'green' | 'orange' | 'red' | 'purple'
export type StatCardSize = 'small' | 'medium' | 'large'

export interface UseStatCardOptions {
  trend?: number
  color?: StatCardColor
  size?: StatCardSize
}

export interface UseStatCardReturn {
  cardColorClass: ComputedRef<string>
  sizeClass: ComputedRef<string>
  trendClass: ComputedRef<string>
  trendText: ComputedRef<string>
}

export function useStatCard(options: UseStatCardOptions = {}): UseStatCardReturn {
  const color = options.color ?? 'blue'
  const size = options.size ?? 'medium'

  const cardColorClass = computed(() => {
    const map: Record<StatCardColor, string> = {
      blue: 'border-l-blue-500',
      green: 'border-l-emerald-500',
      orange: 'border-l-amber-500',
      red: 'border-l-rose-500',
      purple: 'border-l-violet-500',
    }
    return map[color]
  })

  const sizeClass = computed(() => {
    const map: Record<StatCardSize, string> = {
      small: 'p-4 text-sm',
      medium: 'p-5 text-base',
      large: 'p-6 text-lg',
    }
    return map[size]
  })

  const trendClass = computed(() => {
    if (options.trend == null) return ''
    return options.trend >= 0 ? 'text-emerald-600' : 'text-rose-600'
  })

  const trendText = computed(() => {
    if (options.trend == null) return ''
    const sign = options.trend >= 0 ? '+' : ''
    return `${sign}${options.trend}%`
  })

  return {
    cardColorClass,
    sizeClass,
    trendClass,
    trendText,
  }
}
