export interface UseNavBarOptions {
  onBack?: () => void
  onClose?: () => void
  onLeftClick?: () => void
  onRightClick?: () => void
}

export interface UseNavBarReturn {
  handleBack: () => void
  handleClose: () => void
  handleLeftClick: () => void
  handleRightClick: () => void
}

export function useNavBar(options: UseNavBarOptions = {}): UseNavBarReturn {
  function handleBack(): void { options.onBack?.() }
  function handleClose(): void { options.onClose?.() }
  function handleLeftClick(): void { options.onLeftClick?.() }
  function handleRightClick(): void { options.onRightClick?.() }

  return {
    handleBack,
    handleClose,
    handleLeftClick,
    handleRightClick,
  }
}
