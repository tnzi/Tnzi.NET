/**
 * @tnzi/core/adapters/icons/types
 *
 * Icon type definitions.
 */

/** Icon size */
export type IconSize = 'xs' | 'sm' | 'md' | 'lg' | 'xl' | '2xl' | '3xl';

/** Icon type */
export enum IconType {
  /** Solid icons */
  Solid = 'solid',
  /** Outline icons */
  Outline = 'outline',
  /** Two-tone icons */
  TwoTone = 'twotone',
}

/** Icon component props */
export interface IconProps {
  /** Icon name */
  name: string;
  /** Icon size */
  size?: IconSize | number | string;
  /** Icon type */
  type?: IconType;
  /** Icon color */
  color?: string;
  /** Whether spin (for loading) */
  spin?: boolean;
  /** Custom style class */
  class?: string | string[];
  /** Custom inline style */
  style?: string | Record<string, string | number>;
}

/** Icon registry entry */
export interface IconRegistryEntry {
  /** Icon name */
  name: string;
  /** Icon component (Vue component definition) */
  component: object;
  /** Icon type */
  type?: IconType;
}


