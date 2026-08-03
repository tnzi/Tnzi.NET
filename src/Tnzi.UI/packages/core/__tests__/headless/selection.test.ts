import { describe, it, expect, beforeEach } from 'vitest';
import { SelectionController } from '../../src/headless/selection';

describe('SelectionController', () => {
  // ------------------------------------------
  // Constructor defaults
  // ------------------------------------------

  describe('constructor defaults', () => {
    it('should default mode to "multiple"', () => {
      const s = new SelectionController();
      expect(s.mode).toBe('multiple');
    });

    it('should default selectedKeys to empty array', () => {
      const s = new SelectionController();
      expect(s.selectedKeys).toEqual([]);
    });

    it('should default selectedCount to 0', () => {
      const s = new SelectionController();
      expect(s.selectedCount).toBe(0);
    });

    it('should default hasSelection to false', () => {
      const s = new SelectionController();
      expect(s.hasSelection).toBe(false);
    });
  });

  describe('constructor with initial keys', () => {
    it('should accept initial selected keys', () => {
      const s = new SelectionController({ initialKeys: ['a', 'b'] });
      expect(s.selectedKeys).toEqual(['a', 'b']);
      expect(s.selectedCount).toBe(2);
      expect(s.hasSelection).toBe(true);
    });

    it('should clone initial keys (no reference sharing)', () => {
      const keys = ['a', 'b'];
      const s = new SelectionController({ initialKeys: keys });
      keys.push('c');
      expect(s.selectedKeys).toEqual(['a', 'b']);
    });

    it('should report isSelected correctly for initial keys', () => {
      const s = new SelectionController({ initialKeys: ['x', 'y'] });
      expect(s.isSelected('x')).toBe(true);
      expect(s.isSelected('z')).toBe(false);
    });
  });

  // ------------------------------------------
  // Single mode
  // ------------------------------------------

  describe('single mode', () => {
    let s: SelectionController<string>;

    beforeEach(() => {
      s = new SelectionController({ mode: 'single' });
    });

    it('should select a key', () => {
      s.select('a');
      expect(s.selectedKeys).toEqual(['a']);
    });

    it('should replace previous selection on new select', () => {
      s.select('a');
      s.select('b');
      expect(s.selectedKeys).toEqual(['b']);
      expect(s.isSelected('a')).toBe(false);
      expect(s.isSelected('b')).toBe(true);
    });

    it('should deselect a key', () => {
      s.select('a');
      s.deselect('a');
      expect(s.selectedKeys).toEqual([]);
    });

    it('should toggle: select then deselect', () => {
      s.toggle('a');
      expect(s.isSelected('a')).toBe(true);
      s.toggle('a');
      expect(s.isSelected('a')).toBe(false);
    });
  });

  // ------------------------------------------
  // Multiple mode
  // ------------------------------------------

  describe('multiple mode', () => {
    let s: SelectionController<string>;

    beforeEach(() => {
      s = new SelectionController({ mode: 'multiple' });
    });

    it('should add keys on select', () => {
      s.select('a');
      s.select('b');
      expect(s.selectedKeys).toEqual(['a', 'b']);
    });

    it('should not add duplicate keys', () => {
      s.select('a');
      s.select('a');
      expect(s.selectedKeys).toEqual(['a']);
    });

    it('should remove key on deselect', () => {
      s.select('a');
      s.select('b');
      s.deselect('a');
      expect(s.selectedKeys).toEqual(['b']);
      expect(s.isSelected('a')).toBe(false);
    });

    it('should handle deselect of non-selected key gracefully', () => {
      s.select('a');
      s.deselect('x');
      expect(s.selectedKeys).toEqual(['a']);
    });
  });

  // ------------------------------------------
  // toggle
  // ------------------------------------------

  describe('toggle', () => {
    it('should select if not selected', () => {
      const s = new SelectionController<string>();
      s.toggle('a');
      expect(s.isSelected('a')).toBe(true);
    });

    it('should deselect if already selected', () => {
      const s = new SelectionController<string>();
      s.select('a');
      s.toggle('a');
      expect(s.isSelected('a')).toBe(false);
    });
  });

  // ------------------------------------------
  // isSelected (O(1) via Set)
  // ------------------------------------------

  describe('isSelected', () => {
    it('should return true for selected key', () => {
      const s = new SelectionController<string>();
      s.select('item-1');
      expect(s.isSelected('item-1')).toBe(true);
    });

    it('should return false for non-selected key', () => {
      const s = new SelectionController<string>();
      expect(s.isSelected('item-1')).toBe(false);
    });

    it('should work with numeric keys', () => {
      const s = new SelectionController<number>();
      s.select(42);
      expect(s.isSelected(42)).toBe(true);
      expect(s.isSelected(99)).toBe(false);
    });
  });

  // ------------------------------------------
  // setAllKeys, toggleAll, isAllSelected, isIndeterminate
  // ------------------------------------------

  describe('select all behavior', () => {
    let s: SelectionController<string>;

    beforeEach(() => {
      s = new SelectionController<string>();
      s.setAllKeys(['a', 'b', 'c']);
    });

    it('should not be all-selected initially', () => {
      expect(s.isAllSelected).toBe(false);
    });

    it('should not be indeterminate when nothing is selected', () => {
      expect(s.isIndeterminate).toBe(false);
    });

    it('should be indeterminate when some (but not all) are selected', () => {
      s.select('a');
      expect(s.isIndeterminate).toBe(true);
      expect(s.isAllSelected).toBe(false);
    });

    it('should be all-selected when all keys are selected', () => {
      s.select('a');
      s.select('b');
      s.select('c');
      expect(s.isAllSelected).toBe(true);
      expect(s.isIndeterminate).toBe(false);
    });

    describe('toggleAll', () => {
      it('should select all when not all selected', () => {
        s.toggleAll();
        expect(s.isAllSelected).toBe(true);
        expect(s.selectedKeys).toEqual(['a', 'b', 'c']);
      });

      it('should deselect all when all are selected', () => {
        s.toggleAll(); // select all
        s.toggleAll(); // deselect all
        expect(s.selectedKeys).toEqual([]);
        expect(s.isAllSelected).toBe(false);
      });

      it('should select all when some are selected (indeterminate)', () => {
        s.select('a');
        s.toggleAll();
        expect(s.isAllSelected).toBe(true);
      });
    });

    it('should deduplicate keys passed to setAllKeys', () => {
      s.setAllKeys(['x', 'x', 'y']);
      s.toggleAll();
      expect(s.selectedKeys).toEqual(['x', 'y']);
    });

    it('isAllSelected should be false when allKeys is empty', () => {
      const s2 = new SelectionController<string>();
      expect(s2.isAllSelected).toBe(false);
    });
  });

  // ------------------------------------------
  // clear
  // ------------------------------------------

  describe('clear', () => {
    it('should remove all selections', () => {
      const s = new SelectionController<string>();
      s.select('a');
      s.select('b');
      s.clear();
      expect(s.selectedKeys).toEqual([]);
      expect(s.hasSelection).toBe(false);
      expect(s.isSelected('a')).toBe(false);
    });
  });

  // ------------------------------------------
  // setSelectedKeys
  // ------------------------------------------

  describe('setSelectedKeys', () => {
    it('should replace all selected keys', () => {
      const s = new SelectionController<string>();
      s.select('a');
      s.setSelectedKeys(['x', 'y']);
      expect(s.selectedKeys).toEqual(['x', 'y']);
      expect(s.isSelected('a')).toBe(false);
      expect(s.isSelected('x')).toBe(true);
    });

    it('should update the internal Set', () => {
      const s = new SelectionController<string>();
      s.setSelectedKeys(['p', 'q']);
      expect(s.isSelected('p')).toBe(true);
      expect(s.isSelected('q')).toBe(true);
      expect(s.isSelected('r')).toBe(false);
    });
  });
});
