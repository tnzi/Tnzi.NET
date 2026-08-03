import { describe, it, expect, beforeEach } from 'vitest';
import { SortController } from '../../src/headless/sort';

describe('SortController', () => {
  // ------------------------------------------
  // Constructor defaults
  // ------------------------------------------

  describe('constructor defaults', () => {
    it('should default sortBy to null', () => {
      const s = new SortController();
      expect(s.sortBy).toBeNull();
    });

    it('should default sortDirection to "asc"', () => {
      const s = new SortController();
      expect(s.sortDirection).toBe('asc');
    });

    it('should default multiple to false', () => {
      const s = new SortController();
      expect(s.multiple).toBe(false);
    });

    it('should default sortFields to empty array when no defaultField', () => {
      const s = new SortController();
      expect(s.sortFields).toEqual([]);
    });
  });

  describe('custom constructor options', () => {
    it('should accept defaultField and defaultDirection', () => {
      const s = new SortController({ defaultField: 'name', defaultDirection: 'desc' });
      expect(s.sortBy).toBe('name');
      expect(s.sortDirection).toBe('desc');
    });

    it('should populate sortFields when defaultField is provided', () => {
      const s = new SortController({ defaultField: 'createdAt', defaultDirection: 'asc' });
      expect(s.sortFields).toEqual([{ field: 'createdAt', direction: 'asc' }]);
    });

    it('should accept multiple option', () => {
      const s = new SortController({ multiple: true });
      expect(s.multiple).toBe(true);
    });
  });

  // ------------------------------------------
  // toggle
  // ------------------------------------------

  describe('toggle', () => {
    let s: SortController;

    beforeEach(() => {
      s = new SortController();
    });

    it('should set sortBy and direction asc when toggling a new field', () => {
      s.toggle('name');
      expect(s.sortBy).toBe('name');
      expect(s.sortDirection).toBe('asc');
    });

    it('should toggle direction when toggling the same field', () => {
      s.toggle('name');
      expect(s.sortDirection).toBe('asc');
      s.toggle('name');
      expect(s.sortDirection).toBe('desc');
      s.toggle('name');
      expect(s.sortDirection).toBe('asc');
    });

    it('should reset direction to asc when toggling a different field', () => {
      s.toggle('name');
      s.toggle('name'); // desc
      s.toggle('createdAt');
      expect(s.sortBy).toBe('createdAt');
      expect(s.sortDirection).toBe('asc');
    });

    it('should update sortFields in single mode', () => {
      s.toggle('name');
      expect(s.sortFields).toEqual([{ field: 'name', direction: 'asc' }]);
      s.toggle('name');
      expect(s.sortFields).toEqual([{ field: 'name', direction: 'desc' }]);
    });
  });

  // ------------------------------------------
  // setSort
  // ------------------------------------------

  describe('setSort', () => {
    it('should set sortBy and sortDirection', () => {
      const s = new SortController();
      s.setSort('price', 'desc');
      expect(s.sortBy).toBe('price');
      expect(s.sortDirection).toBe('desc');
    });

    it('should update sortFields in single mode', () => {
      const s = new SortController();
      s.setSort('price', 'desc');
      expect(s.sortFields).toEqual([{ field: 'price', direction: 'desc' }]);
    });
  });

  // ------------------------------------------
  // clear
  // ------------------------------------------

  describe('clear', () => {
    it('should reset sortBy to null and direction to asc', () => {
      const s = new SortController({ defaultField: 'name', defaultDirection: 'desc' });
      s.clear();
      expect(s.sortBy).toBeNull();
      expect(s.sortDirection).toBe('asc');
    });

    it('should clear sortFields', () => {
      const s = new SortController({ defaultField: 'name' });
      s.clear();
      expect(s.sortFields).toEqual([]);
    });
  });

  // ------------------------------------------
  // getFieldDirection
  // ------------------------------------------

  describe('getFieldDirection', () => {
    it('should return direction for the current sortBy field', () => {
      const s = new SortController();
      s.setSort('name', 'desc');
      expect(s.getFieldDirection('name')).toBe('desc');
    });

    it('should return null for a field that is not sorted', () => {
      const s = new SortController();
      s.setSort('name', 'asc');
      expect(s.getFieldDirection('age')).toBeNull();
    });

    it('should return null when no sorting is active', () => {
      const s = new SortController();
      expect(s.getFieldDirection('name')).toBeNull();
    });

    it('should find field in sortFields array', () => {
      const s = new SortController({ defaultField: 'name', defaultDirection: 'desc' });
      expect(s.getFieldDirection('name')).toBe('desc');
    });
  });

  // ------------------------------------------
  // toQuery
  // ------------------------------------------

  describe('toQuery', () => {
    it('should return empty object when no sort is active', () => {
      const s = new SortController();
      expect(s.toQuery()).toEqual({});
    });

    it('should return sortBy and sortDescending=false for asc', () => {
      const s = new SortController();
      s.setSort('name', 'asc');
      expect(s.toQuery()).toEqual({ sortBy: 'name', sortDescending: false });
    });

    it('should return sortBy and sortDescending=true for desc', () => {
      const s = new SortController();
      s.setSort('name', 'desc');
      expect(s.toQuery()).toEqual({ sortBy: 'name', sortDescending: true });
    });
  });

  // ------------------------------------------
  // Boolean getters
  // ------------------------------------------

  describe('hasSorting', () => {
    it('should return false when sortBy is null', () => {
      const s = new SortController();
      expect(s.hasSorting).toBe(false);
    });

    it('should return true when sortBy is set', () => {
      const s = new SortController({ defaultField: 'name' });
      expect(s.hasSorting).toBe(true);
    });
  });

  describe('isAscending', () => {
    it('should return true when direction is asc', () => {
      const s = new SortController();
      expect(s.isAscending).toBe(true);
    });

    it('should return false when direction is desc', () => {
      const s = new SortController({ defaultField: 'name', defaultDirection: 'desc' });
      expect(s.isAscending).toBe(false);
    });
  });

  describe('isDescending', () => {
    it('should return true when direction is desc', () => {
      const s = new SortController({ defaultField: 'name', defaultDirection: 'desc' });
      expect(s.isDescending).toBe(true);
    });

    it('should return false when direction is asc', () => {
      const s = new SortController();
      expect(s.isDescending).toBe(false);
    });
  });
});
