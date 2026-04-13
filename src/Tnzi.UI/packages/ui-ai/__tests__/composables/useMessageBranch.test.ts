import { describe, it, expect } from 'vitest';
import { useMessageBranch } from '../../src/composables/useMessageBranch';

describe('useMessageBranch', () => {
  it('should initialize with default values', () => {
    const branch = useMessageBranch();
    expect(branch.currentBranch.value).toBe(0);
    expect(branch.totalBranches.value).toBe(1);
    expect(branch.displayText.value).toBe('1/1');
  });

  it('should initialize with given count', () => {
    const branch = useMessageBranch(3);
    expect(branch.currentBranch.value).toBe(0);
    expect(branch.totalBranches.value).toBe(3);
    expect(branch.displayText.value).toBe('1/3');
  });

  it('should navigate to next branch', () => {
    const branch = useMessageBranch(3);
    branch.goToNext();
    expect(branch.currentBranch.value).toBe(1);
    expect(branch.displayText.value).toBe('2/3');
  });

  it('should wrap around when navigating next past last branch', () => {
    const branch = useMessageBranch(3);
    branch.goToNext(); // 1
    branch.goToNext(); // 2
    branch.goToNext(); // wraps to 0
    expect(branch.currentBranch.value).toBe(0);
    expect(branch.displayText.value).toBe('1/3');
  });

  it('should navigate to previous branch', () => {
    const branch = useMessageBranch(3);
    branch.goToNext(); // 1
    branch.goToPrev(); // 0
    expect(branch.currentBranch.value).toBe(0);
  });

  it('should wrap around when navigating prev before first branch', () => {
    const branch = useMessageBranch(3);
    branch.goToPrev(); // wraps to 2
    expect(branch.currentBranch.value).toBe(2);
    expect(branch.displayText.value).toBe('3/3');
  });

  it('should not navigate when there is only 1 branch', () => {
    const branch = useMessageBranch(1);
    branch.goToNext();
    expect(branch.currentBranch.value).toBe(0);
    branch.goToPrev();
    expect(branch.currentBranch.value).toBe(0);
  });

  it('should add a branch and navigate to it', () => {
    const branch = useMessageBranch(2);
    branch.addBranch();
    expect(branch.totalBranches.value).toBe(3);
    expect(branch.currentBranch.value).toBe(2);
    expect(branch.displayText.value).toBe('3/3');
  });

  it('should add multiple branches', () => {
    const branch = useMessageBranch(1);
    branch.addBranch();
    branch.addBranch();
    branch.addBranch();
    expect(branch.totalBranches.value).toBe(4);
    expect(branch.currentBranch.value).toBe(3);
  });

  it('should support circular navigation after adding branches', () => {
    const branch = useMessageBranch(1);
    branch.addBranch(); // total=2, current=1
    branch.goToNext(); // wraps to 0
    expect(branch.currentBranch.value).toBe(0);
    branch.goToPrev(); // wraps to 1
    expect(branch.currentBranch.value).toBe(1);
  });

  it('should enforce minimum of 1 branch', () => {
    const branch = useMessageBranch(0);
    expect(branch.totalBranches.value).toBe(1);
  });
});
