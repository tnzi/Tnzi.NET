/**
 * useEmbedMode - Embed mode control (floating/sidebar/inline)
 */

import { ref, readonly, type Ref } from 'vue';

export type EmbedMode = 'floating' | 'sidebar' | 'inline';

export interface UseEmbedModeReturn {
  mode: Readonly<Ref<EmbedMode>>;
  /** Writable on purpose: embed widgets v-model this against a host-controlled
   *  `open` prop. Use `open()` / `close()` / `toggle()` where you can. */
  isOpen: Ref<boolean>;
  isMinimized: Readonly<Ref<boolean>>;
  setMode: (mode: EmbedMode) => void;
  open: () => void;
  close: () => void;
  toggle: () => void;
  minimize: () => void;
  expand: () => void;
}

export function useEmbedMode(initialMode: EmbedMode = 'floating'): UseEmbedModeReturn {
  const mode = ref<EmbedMode>(initialMode);
  const isOpen = ref(false);
  const isMinimized = ref(false);

  function setMode(newMode: EmbedMode): void {
    mode.value = newMode;
  }

  function open(): void {
    isOpen.value = true;
    isMinimized.value = false;
  }

  function close(): void {
    isOpen.value = false;
    isMinimized.value = false;
  }

  function toggle(): void {
    if (isOpen.value) {
      close();
    } else {
      open();
    }
  }

  function minimize(): void {
    isMinimized.value = true;
  }

  function expand(): void {
    isMinimized.value = false;
  }

  return {
    mode: readonly(mode),
    isOpen,
    isMinimized: readonly(isMinimized),
    setMode,
    open,
    close,
    toggle,
    minimize,
    expand,
  };
}
