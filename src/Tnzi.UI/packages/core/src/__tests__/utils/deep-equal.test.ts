import { describe, it, expect } from 'vitest';
import { deepEqual } from '../../utils/deep-equal';

describe('deepEqual', () => {
  // ------------------------------------------
  // Primitives
  // ------------------------------------------

  describe('primitives', () => {
    it('should return true for identical numbers', () => {
      expect(deepEqual(1, 1)).toBe(true);
    });

    it('should return false for different numbers', () => {
      expect(deepEqual(1, 2)).toBe(false);
    });

    it('should return true for identical strings', () => {
      expect(deepEqual('hello', 'hello')).toBe(true);
    });

    it('should return false for different strings', () => {
      expect(deepEqual('hello', 'world')).toBe(false);
    });

    it('should return true for identical booleans', () => {
      expect(deepEqual(true, true)).toBe(true);
      expect(deepEqual(false, false)).toBe(true);
    });

    it('should return false for different booleans', () => {
      expect(deepEqual(true, false)).toBe(false);
    });

    it('should return true for NaN === NaN via reference check (both not equal)', () => {
      // NaN !== NaN, and a === b is false, then null check passes,
      // typeof NaN is 'number', typeof check passes, but typeof a !== 'object' returns false
      expect(deepEqual(NaN, NaN)).toBe(false);
    });

    it('should return true for zero and zero', () => {
      expect(deepEqual(0, 0)).toBe(true);
    });
  });

  // ------------------------------------------
  // null and undefined
  // ------------------------------------------

  describe('null and undefined', () => {
    it('should return true for null === null', () => {
      expect(deepEqual(null, null)).toBe(true);
    });

    it('should return true for undefined === undefined', () => {
      expect(deepEqual(undefined, undefined)).toBe(true);
    });

    it('should return false for null vs undefined', () => {
      expect(deepEqual(null, undefined)).toBe(false);
    });

    it('should return false for null vs object', () => {
      expect(deepEqual(null, {})).toBe(false);
    });

    it('should return false for undefined vs object', () => {
      expect(deepEqual(undefined, {})).toBe(false);
    });

    it('should return false for value vs null', () => {
      expect(deepEqual(42, null)).toBe(false);
    });

    it('should return false for value vs undefined', () => {
      expect(deepEqual('hello', undefined)).toBe(false);
    });
  });

  // ------------------------------------------
  // Date comparisons
  // ------------------------------------------

  describe('dates', () => {
    it('should return true for same date values', () => {
      const d1 = new Date('2024-01-01T00:00:00Z');
      const d2 = new Date('2024-01-01T00:00:00Z');
      expect(deepEqual(d1, d2)).toBe(true);
    });

    it('should return false for different date values', () => {
      const d1 = new Date('2024-01-01');
      const d2 = new Date('2024-12-31');
      expect(deepEqual(d1, d2)).toBe(false);
    });

    it('should return true for the same Date reference', () => {
      const d = new Date();
      expect(deepEqual(d, d)).toBe(true);
    });
  });

  // ------------------------------------------
  // Array comparisons
  // ------------------------------------------

  describe('arrays', () => {
    it('should return true for identical arrays', () => {
      expect(deepEqual([1, 2, 3], [1, 2, 3])).toBe(true);
    });

    it('should return false for arrays with different length', () => {
      expect(deepEqual([1, 2], [1, 2, 3])).toBe(false);
    });

    it('should return false for arrays with same length but different values', () => {
      expect(deepEqual([1, 2, 3], [1, 2, 4])).toBe(false);
    });

    it('should return true for empty arrays', () => {
      expect(deepEqual([], [])).toBe(true);
    });

    it('should handle nested arrays', () => {
      expect(deepEqual([[1, 2], [3, 4]], [[1, 2], [3, 4]])).toBe(true);
      expect(deepEqual([[1, 2], [3, 4]], [[1, 2], [3, 5]])).toBe(false);
    });

    it('should return false for array vs non-array', () => {
      expect(deepEqual([1, 2], { 0: 1, 1: 2 })).toBe(false);
    });
  });

  // ------------------------------------------
  // Object comparisons
  // ------------------------------------------

  describe('objects', () => {
    it('should return true for identical objects', () => {
      expect(deepEqual({ a: 1, b: 2 }, { a: 1, b: 2 })).toBe(true);
    });

    it('should return false for objects with different keys', () => {
      expect(deepEqual({ a: 1 }, { b: 1 })).toBe(false);
    });

    it('should return false for objects with different number of keys', () => {
      expect(deepEqual({ a: 1 }, { a: 1, b: 2 })).toBe(false);
    });

    it('should return false for objects with same keys but different values', () => {
      expect(deepEqual({ a: 1 }, { a: 2 })).toBe(false);
    });

    it('should return true for empty objects', () => {
      expect(deepEqual({}, {})).toBe(true);
    });

    it('should handle nested objects', () => {
      const a = { x: { y: { z: 1 } } };
      const b = { x: { y: { z: 1 } } };
      expect(deepEqual(a, b)).toBe(true);
    });

    it('should detect deeply nested differences', () => {
      const a = { x: { y: { z: 1 } } };
      const b = { x: { y: { z: 2 } } };
      expect(deepEqual(a, b)).toBe(false);
    });

    it('should handle objects with array values', () => {
      const a = { items: [1, 2, 3] };
      const b = { items: [1, 2, 3] };
      expect(deepEqual(a, b)).toBe(true);
    });
  });

  // ------------------------------------------
  // Mixed type comparisons
  // ------------------------------------------

  describe('mixed types', () => {
    it('should return false for number vs string', () => {
      expect(deepEqual(1, '1')).toBe(false);
    });

    it('should return false for boolean vs number', () => {
      expect(deepEqual(true, 1)).toBe(false);
    });

    it('should return false for object vs array', () => {
      expect(deepEqual({}, [])).toBe(false);
    });

    it('should return false for string vs object', () => {
      expect(deepEqual('hello', { value: 'hello' })).toBe(false);
    });

    it('should return false for number vs object', () => {
      expect(deepEqual(42, { value: 42 })).toBe(false);
    });
  });

  // ------------------------------------------
  // Referential equality shortcut
  // ------------------------------------------

  describe('referential equality', () => {
    it('should return true immediately for the same reference', () => {
      const obj = { a: 1, b: { c: 2 } };
      expect(deepEqual(obj, obj)).toBe(true);
    });

    it('should return true immediately for the same array reference', () => {
      const arr = [1, 2, 3];
      expect(deepEqual(arr, arr)).toBe(true);
    });
  });
});
