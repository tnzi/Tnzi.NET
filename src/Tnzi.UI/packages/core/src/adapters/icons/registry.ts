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
 */
export function registerIcon(entry: IconRegistryEntry): void {
  registry.set(entry.name, entry);
}

/**
 * Get an icon from registry
 */
export function getIcon(name: string): IconRegistryEntry | undefined {
  return registry.get(name);
}

/**
 * Check if icon exists in registry
 */
export function hasIcon(name: string): boolean {
  return registry.has(name);
}

/**
 * Get all registered icon names
 */
export function getIconNames(): string[] {
  return Array.from(registry.keys());
}

/**
 * Clear all icons from registry
 */
export function clearIcons(): void {
  registry.clear();
}

export type { IconRegistryEntry };

