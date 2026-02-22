/**
 * Common Types - Shared utility types
 */

/**
 * Nullable type helper
 */
export type Nullable<T> = T | null;

/**
 * Optional type helper
 */
export type Optional<T> = T | undefined;

/**
 * Deep partial type
 */
export type DeepPartial<T> = {
  [P in keyof T]?: T[P] extends object ? DeepPartial<T[P]> : T[P];
};

/**
 * Make all properties optional recursively
 */
export type DeepOptional<T> = {
  [P in keyof T]?: T[P] extends object ? DeepOptional<T[P]> : T[P];
};

/**
 * Pick properties by value type
 */
export type PickByType<T, U> = {
  [P in keyof T as T[P] extends U ? P : never]: T[P];
};

/**
 * Omit properties by value type
 */
export type OmitByType<T, U> = {
  [P in keyof T as T[P] extends U ? never : P]: T[P];
};

/**
 * String literal union helper
 */
export type StringLiteral<T> = T extends string ? (string extends T ? never : T) : never;

/**
 * Extract keys of specific type
 */
export type KeysOfType<T, U> = { [K in keyof T]: T[K] extends U ? K : never }[keyof T];

/**
 * Make specific keys required
 */
export type RequireKeys<T, K extends keyof T> = T & Required<Pick<T, K>>;

/**
 * Make specific keys optional
 */
export type PartialKeys<T, K extends keyof T> = Omit<T, K> & Partial<Pick<T, K>>;

/**
 * ID type (can be string GUID or number)
 */
export type Id = string | number;

/**
 * Timestamp type (ISO string or Date)
 */
export type Timestamp = string | Date;

/**
 * Generic key-value pair
 */
export interface KeyValue<K = string, V = unknown> {
  key: K;
  value: V;
}

/**
 * Generic name-value pair
 */
export interface NameValue<T = unknown> {
  name: string;
  value: T;
}

/**
 * Generic option for selects/dropdowns
 */
export interface SelectOption<T = string> {
  label: string;
  value: T;
  disabled?: boolean;
  description?: string;
  icon?: string;
}

/**
 * Generic tree node structure
 */
export interface TreeNode<T = unknown> {
  id: Id;
  parentId?: Id | null;
  children?: TreeNode<T>[];
  data?: T;
}

/**
 * Generic result wrapper
 */
export interface Result<T, E = Error> {
  success: boolean;
  value?: T;
  error?: E;
}

/**
 * Async result type
 */
export type AsyncResult<T, E = Error> = Promise<Result<T, E>>;
