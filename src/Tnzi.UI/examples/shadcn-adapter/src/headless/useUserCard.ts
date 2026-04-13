export interface UserCardUser {
  id: string | number
  name: string
  avatar?: string
  email?: string
  role?: string
  status?: 'active' | 'inactive' | 'pending'
  description?: string
}

export interface UseUserCardOptions {
  user?: UserCardUser
  clickable?: boolean
  onClick?: (user: UserCardUser) => void
  onEdit?: (user: UserCardUser) => void
  onDelete?: (user: UserCardUser) => void
  onView?: (user: UserCardUser) => void
}

export interface UseUserCardReturn {
  getInitials: () => string
  handleCardClick: () => void
  handleActionClick: (action: 'edit' | 'delete' | 'view') => void
}

export function useUserCard(options: UseUserCardOptions = {}): UseUserCardReturn {
  const user = options.user ?? { id: '', name: '' }

  function getInitials(): string {
    if (!user.name) return 'U'
    return user.name
      .split(' ')
      .map((word) => word[0])
      .slice(0, 2)
      .join('')
      .toUpperCase()
  }

  function handleCardClick(): void {
    if (options.clickable !== false) {
      options.onClick?.(user)
    }
  }

  function handleActionClick(action: 'edit' | 'delete' | 'view'): void {
    if (action === 'edit') options.onEdit?.(user)
    else if (action === 'delete') options.onDelete?.(user)
    else options.onView?.(user)
  }

  return {
    getInitials,
    handleCardClick,
    handleActionClick,
  }
}
