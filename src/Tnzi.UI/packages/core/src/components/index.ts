/**
 * @tnzi/core/components
 *
 * Component interface standards for all Tnzi UI packages.
 * All UI implementations must conform to these interfaces.
 */

export * from './auth';
export * from './table';
export * from './form';
export * from './card';
export * from './list';
export * from './layout';
export * from './navigation';
export * from './data';

// Keep a tiny runtime marker so this subpath does not emit an empty JS chunk.
export const COMPONENT_CONTRACTS_VERSION = '2.0.0';
