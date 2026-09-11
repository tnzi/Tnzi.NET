import { describe, it, expect } from 'vitest';
import { SortController, toggleSort } from '../../src/headless/sort';

describe('toggleSort', () => {
  it('flips the direction when the same field is toggled again', () => {
    expect(toggleSort({ sortBy: 'name', sortDirection: 'asc' }, 'name')).toEqual({
      sortBy: 'name',
      sortDirection: 'desc',
    });
    expect(toggleSort({ sortBy: 'name', sortDirection: 'desc' }, 'name')).toEqual({
      sortBy: 'name',
      sortDirection: 'asc',
    });
  });

  it('starts a new field at the initial direction, ascending by default', () => {
    expect(toggleSort({ sortBy: 'name', sortDirection: 'desc' }, 'createdAt')).toEqual({
      sortBy: 'createdAt',
      sortDirection: 'asc',
    });
    expect(toggleSort({}, 'createdAt', 'desc')).toEqual({ sortBy: 'createdAt', sortDirection: 'desc' });
  });
});

describe('SortController in multiple mode', () => {
  it('appends new fields and flips only the toggled one', () => {
    const sort = new SortController({ multiple: true, defaultField: 'name' });

    sort.toggle('createdAt');
    expect(sort.sortFields).toEqual([
      { field: 'name', direction: 'asc' },
      { field: 'createdAt', direction: 'asc' },
    ]);

    sort.toggle('name');
    expect(sort.sortFields).toEqual([
      { field: 'name', direction: 'desc' },
      { field: 'createdAt', direction: 'asc' },
    ]);
  });

  it('setSort replaces an existing entry in place and appends an unknown field', () => {
    const sort = new SortController({ multiple: true, defaultField: 'name', defaultDirection: 'desc' });

    sort.setSort('name', 'asc');
    sort.setSort('price', 'desc');

    expect(sort.sortFields).toEqual([
      { field: 'name', direction: 'asc' },
      { field: 'price', direction: 'desc' },
    ]);
  });

  it('reports per-field direction for the UI and the primary field for the query', () => {
    const sort = new SortController({ multiple: true });
    sort.setSort('name', 'desc');
    sort.setSort('price', 'asc');

    expect(sort.getFieldDirection('name')).toBe('desc');
    expect(sort.getFieldDirection('price')).toBe('asc');
    expect(sort.getFieldDirection('missing')).toBeNull();
    expect(sort.toQuery()).toEqual({ sortBy: 'name', sortDescending: true });
    expect(sort.isDescending).toBe(true);
    expect(sort.isAscending).toBe(false);
  });

  it('clear drops every field and the derived state falls back to defaults', () => {
    const sort = new SortController({ multiple: true, defaultField: 'name' });

    sort.clear();

    expect(sort.hasSorting).toBe(false);
    expect(sort.sortBy).toBeNull();
    expect(sort.sortDirection).toBe('asc');
    expect(sort.toQuery()).toEqual({});
  });
});

describe('SortController in single mode', () => {
  it('setSort always replaces the whole list', () => {
    const sort = new SortController({ defaultField: 'name' });

    sort.setSort('price', 'desc');

    expect(sort.sortFields).toEqual([{ field: 'price', direction: 'desc' }]);
    expect(sort.toQuery()).toEqual({ sortBy: 'price', sortDescending: true });
  });
});
