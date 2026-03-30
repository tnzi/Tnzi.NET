/**
 * @tnzi/core/adapters/icons/registry
 *
 * Icon registry for dynamic icon loading.
 */

import type { IconRegistryEntry } from './types';

/** Icon registry */
const registry = new Map<string, IconRegistryEntry>();

/**
 * Register an icon
 * @deprecated Use unplugin-icons or @iconify/vue instead. Will be removed in next major version.
 */
export function registerIcon(entry: IconRegistryEntry): void {
  registry.set(entry.name, entry);
}

/**
 * Get an icon from registry
 * @deprecated Use unplugin-icons or @iconify/vue instead. Will be removed in next major version.
 */
export function getIcon(name: string): IconRegistryEntry | undefined {
  return registry.get(name);
}

/**
 * Check if icon exists in registry
 * @deprecated Use unplugin-icons or @iconify/vue instead. Will be removed in next major version.
 */
export function hasIcon(name: string): boolean {
  return registry.has(name);
}

/**
 * Get all registered icon names
 * @deprecated Use unplugin-icons or @iconify/vue instead. Will be removed in next major version.
 */
export function getIconNames(): string[] {
  return Array.from(registry.keys());
}

/**
 * Clear all icons from registry
 * @deprecated Use unplugin-icons or @iconify/vue instead. Will be removed in next major version.
 */
export function clearIcons(): void {
  registry.clear();
}

/**
 * Reset icon registry (alias for clearIcons). For tests and SSR isolation.
 * @deprecated Use unplugin-icons or @iconify/vue instead. Will be removed in next major version.
 */
export function resetIconRegistry(): void {
  registry.clear();
}

export type { IconRegistryEntry };

