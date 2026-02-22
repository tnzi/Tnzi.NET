/**
 * ID generation utilities.
 */

let _counter = 0;

/**
 * Generate a unique ID string.
 * Uses timestamp + random string for uniqueness.
 */
export function generateId(): string {
  _counter = (_counter + 1) % 1000000;
  return `${Date.now().toString(36)}-${_counter.toString(36)}-${Math.random().toString(36).substring(2, 8)}`;
}

/**
 * Generate a UUID v4 string.
 */
export function generateUuid(): string {
  if (typeof crypto !== 'undefined' && crypto.randomUUID) {
    return crypto.randomUUID();
  }
  // Fallback for environments without crypto.randomUUID
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === 'x' ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}
