import { describe, it, expect } from 'vitest';
import { parseFlags, hasFlag, flagNames, formatFlags } from '../../src/utils/flags';

/** Mirrors the backend `PendingUserActions`, composites included. */
enum Owed {
  None = 0,
  InvitationPending = 1 << 0,
  ChangePassword = 1 << 8,
  EnrollTotp = 1 << 9,
  ConfirmEmail = 1 << 10,
}

/** A separate fixture for the composite case, which the real enum also has. */
enum WithComposite {
  None = 0,
  A = 1,
  B = 2,
  C = 4,
  AB = 3,
}

describe('parseFlags', () => {
  it('reads the single member name the server actually sends', () => {
    expect(parseFlags('InvitationPending', Owed)).toBe(Owed.InvitationPending);
  });

  it('reads a comma-separated list, with or without spaces', () => {
    const both = Owed.ChangePassword | Owed.EnrollTotp;
    expect(parseFlags('ChangePassword, EnrollTotp', Owed)).toBe(both);
    expect(parseFlags('ChangePassword,EnrollTotp', Owed)).toBe(both);
  });

  it('reads the zero member as 0', () => {
    expect(parseFlags('None', Owed)).toBe(0);
  });

  it('passes a number through, for callers and older deployments', () => {
    expect(parseFlags(Owed.EnrollTotp, Owed)).toBe(Owed.EnrollTotp);
    expect(parseFlags('512', Owed)).toBe(512);
  });

  it('treats null / undefined / empty as nothing set', () => {
    expect(parseFlags(null, Owed)).toBe(0);
    expect(parseFlags(undefined, Owed)).toBe(0);
    expect(parseFlags('', Owed)).toBe(0);
  });

  it('reads a composite member name as all of its bits', () => {
    expect(parseFlags('AB', WithComposite)).toBe(3);
  });

  // A server that grows a flag must not break every older client at once.
  it('ignores a name it does not know, keeping the ones it does', () => {
    expect(parseFlags('InvitationPending, SomethingNewer', Owed)).toBe(Owed.InvitationPending);
  });

  it('tolerates a case difference rather than silently matching nothing', () => {
    expect(parseFlags('invitationpending', Owed)).toBe(Owed.InvitationPending);
  });
});

describe('hasFlag', () => {
  // The whole reason this module exists: `'InvitationPending' & 1` is NaN & 1 === 0,
  // so the naive test answers "no" for every input without throwing.
  it('answers correctly where a bitwise AND silently answers no', () => {
    const wire = 'InvitationPending';
    expect(((wire as unknown as number) & Owed.InvitationPending) !== 0).toBe(false);
    expect(hasFlag(wire, Owed.InvitationPending, Owed)).toBe(true);
  });

  it('finds one flag inside a list', () => {
    const wire = 'ChangePassword, ConfirmEmail';
    expect(hasFlag(wire, Owed.ConfirmEmail, Owed)).toBe(true);
    expect(hasFlag(wire, Owed.EnrollTotp, Owed)).toBe(false);
  });

  it('asking for the zero member means "nothing set"', () => {
    expect(hasFlag('None', Owed.None, Owed)).toBe(true);
    expect(hasFlag('ChangePassword', Owed.None, Owed)).toBe(false);
  });

  it('a composite flag matches when any of its bits are set', () => {
    expect(hasFlag('A', WithComposite.AB, WithComposite)).toBe(true);
  });
});

describe('flagNames / formatFlags', () => {
  it('names the bits that are set, and nothing else', () => {
    expect(flagNames(Owed.ChangePassword | Owed.ConfirmEmail, Owed)).toEqual([
      'ConfirmEmail',
      'ChangePassword',
    ]);
  });

  it('prefers a composite over its parts', () => {
    expect(flagNames(3, WithComposite)).toEqual(['AB']);
  });

  it('renders the wire spelling, and the zero member when nothing is set', () => {
    expect(formatFlags(Owed.InvitationPending | Owed.EnrollTotp, Owed)).toBe(
      'EnrollTotp, InvitationPending'
    );
    expect(formatFlags(0, Owed)).toBe('None');
  });

  it('round-trips through parseFlags', () => {
    const mask = Owed.ChangePassword | Owed.EnrollTotp;
    expect(parseFlags(formatFlags(mask, Owed), Owed)).toBe(mask);
  });
});
