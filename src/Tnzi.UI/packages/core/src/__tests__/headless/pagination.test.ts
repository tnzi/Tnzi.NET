import { describe, it, expect, beforeEach } from 'vitest';
import { PaginationController } from '../../headless/pagination';

describe('PaginationController', () => {
  let pagination: PaginationController;

  beforeEach(() => {
    pagination = new PaginationController();
  });

  // ------------------------------------------
  // Constructor defaults
  // ------------------------------------------

  describe('constructor defaults', () => {
    it('should default pageIndex to 1', () => {
      expect(pagination.pageIndex).toBe(1);
    });

    it('should default pageSize to 20', () => {
      expect(pagination.pageSize).toBe(20);
    });

    it('should default totalCount to 0', () => {
      expect(pagination.totalCount).toBe(0);
    });

    it('should default pageSizes to [10, 20, 50, 100]', () => {
      expect([...pagination.pageSizes]).toEqual([10, 20, 50, 100]);
    });
  });

  describe('custom constructor options', () => {
    it('should accept custom initialPage', () => {
      const p = new PaginationController({ initialPage: 3 });
      expect(p.pageIndex).toBe(3);
    });

    it('should accept custom initialPageSize', () => {
      const p = new PaginationController({ initialPageSize: 50 });
      expect(p.pageSize).toBe(50);
    });

    it('should accept custom pageSizes', () => {
      const p = new PaginationController({ pageSizes: [5, 15, 25] });
      expect([...p.pageSizes]).toEqual([5, 15, 25]);
    });

    it('should accept all options together', () => {
      const p = new PaginationController({
        initialPage: 2,
        initialPageSize: 10,
        pageSizes: [10, 25],
      });
      expect(p.pageIndex).toBe(2);
      expect(p.pageSize).toBe(10);
      expect([...p.pageSizes]).toEqual([10, 25]);
    });
  });

  // ------------------------------------------
  // Computed getters
  // ------------------------------------------

  describe('computed getters', () => {
    describe('totalPages', () => {
      it('should return 0 when totalCount is 0', () => {
        expect(pagination.totalPages).toBe(0);
      });

      it('should calculate total pages correctly (exact division)', () => {
        pagination.totalCount = 100;
        pagination.pageSize = 20;
        expect(pagination.totalPages).toBe(5);
      });

      it('should ceil when items do not divide evenly', () => {
        pagination.totalCount = 101;
        pagination.pageSize = 20;
        expect(pagination.totalPages).toBe(6);
      });

      it('should return 1 when totalCount is less than pageSize', () => {
        pagination.totalCount = 5;
        pagination.pageSize = 20;
        expect(pagination.totalPages).toBe(1);
      });
    });

    describe('hasNextPage', () => {
      it('should return false when totalCount is 0', () => {
        expect(pagination.hasNextPage).toBe(false);
      });

      it('should return true when not on the last page', () => {
        pagination.totalCount = 100;
        pagination.pageIndex = 1;
        expect(pagination.hasNextPage).toBe(true);
      });

      it('should return false when on the last page', () => {
        pagination.totalCount = 100;
        pagination.pageIndex = 5;
        expect(pagination.hasNextPage).toBe(false);
      });
    });

    describe('hasPrevPage', () => {
      it('should return false when on page 1', () => {
        expect(pagination.hasPrevPage).toBe(false);
      });

      it('should return true when on page > 1', () => {
        pagination.totalCount = 100;
        pagination.pageIndex = 3;
        expect(pagination.hasPrevPage).toBe(true);
      });
    });

    describe('isFirstPage', () => {
      it('should return true when pageIndex is 1', () => {
        expect(pagination.isFirstPage).toBe(true);
      });

      it('should return false when pageIndex is not 1', () => {
        pagination.totalCount = 100;
        pagination.pageIndex = 2;
        expect(pagination.isFirstPage).toBe(false);
      });
    });

    describe('isLastPage', () => {
      it('should return true when totalPages is 0', () => {
        expect(pagination.isLastPage).toBe(true);
      });

      it('should return true when on the last page', () => {
        pagination.totalCount = 100;
        pagination.pageSize = 20;
        pagination.pageIndex = 5;
        expect(pagination.isLastPage).toBe(true);
      });

      it('should return false when not on the last page', () => {
        pagination.totalCount = 100;
        pagination.pageSize = 20;
        pagination.pageIndex = 3;
        expect(pagination.isLastPage).toBe(false);
      });
    });

    describe('startIndex', () => {
      it('should return 0 for page 1', () => {
        expect(pagination.startIndex).toBe(0);
      });

      it('should calculate correct start index', () => {
        pagination.pageIndex = 3;
        pagination.pageSize = 20;
        expect(pagination.startIndex).toBe(40);
      });
    });

    describe('endIndex', () => {
      it('should return 0 when totalCount is 0', () => {
        expect(pagination.endIndex).toBe(0);
      });

      it('should return min(startIndex + pageSize, totalCount)', () => {
        pagination.totalCount = 100;
        pagination.pageSize = 20;
        pagination.pageIndex = 1;
        expect(pagination.endIndex).toBe(20);
      });

      it('should cap at totalCount on the last page', () => {
        pagination.totalCount = 95;
        pagination.pageSize = 20;
        pagination.pageIndex = 5;
        // startIndex = 80, 80 + 20 = 100 > 95
        expect(pagination.endIndex).toBe(95);
      });
    });
  });

  // ------------------------------------------
  // Navigation actions
  // ------------------------------------------

  describe('navigation', () => {
    beforeEach(() => {
      pagination.totalCount = 100;
      pagination.pageSize = 20;
      // totalPages = 5
    });

    describe('goTo', () => {
      it('should go to specified page', () => {
        pagination.goTo(3);
        expect(pagination.pageIndex).toBe(3);
      });

      it('should clamp to 1 when page < 1', () => {
        pagination.goTo(-5);
        expect(pagination.pageIndex).toBe(1);
      });

      it('should clamp to totalPages when page > totalPages', () => {
        pagination.goTo(999);
        expect(pagination.pageIndex).toBe(5);
      });

      it('should set pageIndex to 1 when totalPages is 0', () => {
        pagination.totalCount = 0;
        pagination.goTo(5);
        expect(pagination.pageIndex).toBe(1);
      });
    });

    describe('next', () => {
      it('should advance to the next page', () => {
        pagination.pageIndex = 2;
        pagination.next();
        expect(pagination.pageIndex).toBe(3);
      });

      it('should not go beyond last page', () => {
        pagination.pageIndex = 5;
        pagination.next();
        expect(pagination.pageIndex).toBe(5);
      });
    });

    describe('prev', () => {
      it('should go to the previous page', () => {
        pagination.pageIndex = 3;
        pagination.prev();
        expect(pagination.pageIndex).toBe(2);
      });

      it('should not go below page 1', () => {
        pagination.pageIndex = 1;
        pagination.prev();
        expect(pagination.pageIndex).toBe(1);
      });
    });

    describe('first', () => {
      it('should go to page 1', () => {
        pagination.pageIndex = 4;
        pagination.first();
        expect(pagination.pageIndex).toBe(1);
      });
    });

    describe('last', () => {
      it('should go to the last page', () => {
        pagination.pageIndex = 1;
        pagination.last();
        expect(pagination.pageIndex).toBe(5);
      });

      it('should go to page 1 when totalPages is 0', () => {
        pagination.totalCount = 0;
        pagination.last();
        expect(pagination.pageIndex).toBe(1);
      });
    });
  });

  // ------------------------------------------
  // setPageSize
  // ------------------------------------------

  describe('setPageSize', () => {
    it('should update pageSize', () => {
      pagination.setPageSize(50);
      expect(pagination.pageSize).toBe(50);
    });

    it('should reset pageIndex to 1', () => {
      pagination.totalCount = 100;
      pagination.pageIndex = 3;
      pagination.setPageSize(10);
      expect(pagination.pageIndex).toBe(1);
    });
  });

  // ------------------------------------------
  // updateFromResponse
  // ------------------------------------------

  describe('updateFromResponse', () => {
    it('should update totalCount', () => {
      pagination.updateFromResponse(250);
      expect(pagination.totalCount).toBe(250);
    });

    it('should not adjust pageIndex if within range', () => {
      pagination.pageIndex = 2;
      pagination.updateFromResponse(100); // 5 pages
      expect(pagination.pageIndex).toBe(2);
    });

    it('should adjust pageIndex to last page if beyond totalPages', () => {
      pagination.pageIndex = 10;
      pagination.updateFromResponse(60); // 3 pages
      expect(pagination.pageIndex).toBe(3);
    });

    it('should not adjust pageIndex if totalPages is 0', () => {
      pagination.pageIndex = 5;
      pagination.updateFromResponse(0);
      // totalPages = 0, condition: totalPages > 0 is false, no adjustment
      expect(pagination.pageIndex).toBe(5);
    });
  });

  // ------------------------------------------
  // toQuery
  // ------------------------------------------

  describe('toQuery', () => {
    it('should return correct PagedQueryDto with defaults', () => {
      const query = pagination.toQuery();
      expect(query.pageIndex).toBe(1);
      expect(query.pageSize).toBe(20);
      expect(query.skip).toBe(0);
      expect(query.take).toBe(20);
    });

    it('should return correct skip/take for page 3, pageSize 10', () => {
      pagination.pageIndex = 3;
      pagination.pageSize = 10;
      const query = pagination.toQuery();
      expect(query.pageIndex).toBe(3);
      expect(query.pageSize).toBe(10);
      expect(query.skip).toBe(20);
      expect(query.take).toBe(10);
    });
  });

  // ------------------------------------------
  // reset
  // ------------------------------------------

  describe('reset', () => {
    it('should reset to default state', () => {
      pagination.pageIndex = 5;
      pagination.totalCount = 200;
      pagination.reset();
      expect(pagination.pageIndex).toBe(1);
      expect(pagination.totalCount).toBe(0);
    });

    it('should use provided options for reset', () => {
      pagination.pageIndex = 5;
      pagination.pageSize = 50;
      pagination.totalCount = 200;
      pagination.reset({ initialPage: 2, initialPageSize: 10 });
      expect(pagination.pageIndex).toBe(2);
      expect(pagination.pageSize).toBe(10);
      expect(pagination.totalCount).toBe(0);
    });

    it('should keep current pageSize when not specified in options', () => {
      pagination.pageSize = 50;
      pagination.reset();
      expect(pagination.pageSize).toBe(50);
    });
  });
});
