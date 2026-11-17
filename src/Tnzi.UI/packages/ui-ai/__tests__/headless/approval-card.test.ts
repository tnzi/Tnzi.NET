import { describe, expect, it } from 'vitest';
import {
  allowsKind,
  codeDigits,
  formatApprovalCountdown,
  permissionChoices,
  secondsLeft,
} from '../../src/headless/approval-card';
import {
  answersOf,
  EMPTY_PICK,
  isAnswered,
  pickOption,
  pickOwn,
  planChoices,
  questionAnswers,
  questionOptionIds,
  ANSWER_MAX_LENGTH,
} from '../../src/headless/interaction-card';
import { hasMoreBelow } from '../../src/headless/scroll-hint';

const LABELS = { allowOnce: 'Allow once', allowAlways: 'Always', reject: 'Deny' };

describe('approval card model', () => {
  it('reads a typed code only when it has exactly the digits asked for', () => {
    expect(codeDigits('12 34-56', 6)).toBe('123456');
    expect(codeDigits('12345', 6)).toBeNull();
    expect(codeDigits('12a456', 6)).toBeNull();
  });

  it('counts whole seconds down to zero, and an unreadable time has none left', () => {
    const now = Date.parse('2026-11-08T10:00:00Z');
    expect(secondsLeft('2026-11-08T10:02:13.200Z', now)).toBe(134);
    expect(secondsLeft('2026-11-08T09:00:00Z', now)).toBe(0);
    expect(secondsLeft('soon', now)).toBe(0);
    expect(formatApprovalCountdown(133)).toBe('2:13');
    expect(formatApprovalCountdown(-4)).toBe('0:00');
  });

  it('allows only the two allow kinds', () => {
    expect(allowsKind('allow_once')).toBe(true);
    expect(allowsKind('allow_always')).toBe(true);
    expect(allowsKind('reject_once')).toBe(false);
    expect(allowsKind('something_new')).toBe(false);
  });

  it('orders a permission request allow once (main), allow always, then one refusal', () => {
    const choices = permissionChoices(
      [
        { id: 'r1', name: 'No', kind: 'reject_once' },
        { id: 'r2', name: 'Never', kind: 'reject_always' },
        { id: 'a2', name: 'Always', kind: 'allow_always' },
        { id: 'a1', name: 'Yes', kind: 'allow_once' },
      ],
      LABELS,
    );
    expect(choices).toEqual([
      { id: 'a1', label: 'Allow once', tone: 'allow', primary: true },
      { id: 'a2', label: 'Always', tone: 'allow' },
      { id: 'r1', label: 'Deny', tone: 'deny' },
    ]);
  });
});

describe('question cards', () => {
  const single = { question: 'Which DB?', header: 'DB', options: [{ label: 'Postgres' }, { label: 'SQLite' }], multiSelect: false };
  const multi = { question: 'Which parts?', options: [{ label: 'API' }, { label: 'UI' }], multiSelect: true };

  it('a single-choice question takes one option, or the own answer instead of it', () => {
    let pick = pickOption(single, EMPTY_PICK, 'Postgres');
    pick = pickOption(single, pick, 'SQLite');
    expect(answersOf(single, pick)).toEqual(['SQLite']);
    pick = pickOwn(single, { ...pick, text: '  MySQL ' }, true);
    expect(pick.selected).toEqual([]);
    expect(answersOf(single, pick)).toEqual(['MySQL']);
    expect(pickOwn(single, pick, false).own).toBe(false);
  });

  it('a multiple-choice question toggles options and adds the own answer to them', () => {
    let pick = pickOption(multi, EMPTY_PICK, 'API');
    pick = pickOption(multi, pick, 'UI');
    pick = pickOption(multi, pick, 'API');
    pick = pickOwn(multi, { ...pick, text: 'Docs' }, true);
    expect(answersOf(multi, pick)).toEqual(['UI', 'Docs']);
    expect(pickOwn(multi, pick, false).selected).toEqual(['UI']);
  });

  it('is answered only with an answer, none of them too long, and an unknown option does not count', () => {
    expect(isAnswered(single, EMPTY_PICK)).toBe(false);
    expect(isAnswered(single, { selected: ['Oracle'], own: false, text: '' })).toBe(false);
    expect(isAnswered(single, { selected: [], own: true, text: '   ' })).toBe(false);
    expect(isAnswered(single, { selected: [], own: true, text: 'x'.repeat(ANSWER_MAX_LENGTH + 1) })).toBe(false);
    expect(isAnswered(single, { selected: ['SQLite'], own: false, text: '' })).toBe(true);
  });

  it('gives the answers keyed by question text once every question has one', () => {
    const picks = [{ selected: ['Postgres'], own: false, text: '' }];
    expect(questionAnswers([single, multi], picks)).toBeNull();
    expect(questionAnswers([single, multi], [...picks, { selected: ['UI'], own: false, text: '' }])).toEqual({
      'Which DB?': ['Postgres'],
      'Which parts?': ['UI'],
    });
  });

  it('finds the submit and decline options, by id or else by kind', () => {
    expect(questionOptionIds([{ id: 'decline', name: 'd', kind: 'reject_once' }, { id: 'submit', name: 's', kind: 'allow_once' }])).toEqual({
      submit: 'submit',
      decline: 'decline',
    });
    expect(questionOptionIds([{ id: 'go', name: 'g', kind: 'allow_once' }, { id: 'no', name: 'n', kind: 'reject_once' }])).toEqual({
      submit: 'go',
      decline: 'no',
    });
    expect(questionOptionIds([])).toEqual({ submit: 'submit', decline: 'decline' });
  });
});

describe('plan cards', () => {
  const labels = { approve: 'Approve', approveAskEdits: 'Approve, ask', approveAcceptEdits: 'Approve, accept', reject: 'Revise' };

  it('a run that started planning says the plain approval asks before each change', () => {
    const choices = planChoices(
      [
        { id: 'reject', name: 'r', kind: 'reject_once' },
        { id: 'approve_accept_edits', name: 'a', kind: 'allow_always' },
        { id: 'approve', name: 'a', kind: 'allow_once' },
      ],
      labels,
    );
    expect(choices.map((c) => [c.id, c.label, c.tone, !!c.primary])).toEqual([
      ['approve', 'Approve, ask', 'allow', true],
      ['approve_accept_edits', 'Approve, accept', 'allow', false],
      ['reject', 'Revise', 'deny', false],
    ]);
  });

  it('an agent that planned on its own approves plainly, and an unknown option keeps its name and kind', () => {
    const choices = planChoices(
      [
        { id: 'later', name: 'Later', kind: 'reject_once' },
        { id: 'extra', name: 'Go fast', kind: 'allow_once' },
        { id: 'approve', name: 'a', kind: 'allow_once' },
      ],
      labels,
    );
    expect(choices.map((c) => [c.id, c.label, c.tone])).toEqual([
      ['approve', 'Approve', 'allow'],
      ['extra', 'Go fast', 'allow'],
      ['later', 'Later', 'deny'],
    ]);
  });
});

describe('scroll hint', () => {
  it('counts content as hidden below beyond a couple of pixels of rounding', () => {
    expect(hasMoreBelow({ scrollHeight: 500, scrollTop: 0, clientHeight: 200 })).toBe(true);
    expect(hasMoreBelow({ scrollHeight: 500, scrollTop: 299, clientHeight: 200 })).toBe(false);
    expect(hasMoreBelow({ scrollHeight: 200, scrollTop: 0, clientHeight: 200 })).toBe(false);
  });
});
