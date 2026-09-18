import { describe, it, expect } from 'vitest';
import { userColumns, type UserRow } from '../../src/pages/identity/user-config';

/**
 * The status column reads `pendingActions`, which the backend serialises as the
 * member NAME because a global `JsonStringEnumConverter` is registered. The column
 * used to test it with `&`; against a string that coerces to `NaN`, and
 * `NaN & anything` is `0`, so every branch answered "no" and an invited account
 * rendered as "Locked".
 *
 * These cases therefore feed the WIRE spelling, not the numbers. Feeding numbers
 * would keep passing against the broken version and prove nothing.
 */
const statusColumn = userColumns.find((c) => c.key === 'isLockedOut')!;

function labelOf(row: UserRow): string {
  const vnode = statusColumn.render!(row) as { props?: Record<string, unknown> };
  return String(vnode.props?.labelKey ?? '');
}

describe('user list status column, against the wire spelling of pendingActions', () => {
  it('shows "invited" for an account that has not accepted yet', () => {
    // Invited accounts are locked in the database too - the badge must not say so.
    expect(labelOf({ pendingActions: 'InvitationPending', isLockedOut: true })).toBe(
      'admin.identity.users.status.invited'
    );
  });

  it('shows "must change password" when that is what is owed', () => {
    expect(labelOf({ pendingActions: 'ChangePassword' })).toBe(
      'admin.identity.users.status.mustChangePassword'
    );
  });

  it('finds the flag inside a comma-separated list', () => {
    expect(labelOf({ pendingActions: 'ChangePassword, EnrollTotp' })).toBe(
      'admin.identity.users.status.mustChangePassword'
    );
  });

  it('still reports a genuine lockout when nothing is owed', () => {
    expect(labelOf({ pendingActions: 'None', isLockedOut: true })).toBe(
      'admin.shared.status.locked'
    );
  });

  it('reports active when nothing is owed and nothing is locked', () => {
    expect(labelOf({ pendingActions: 'None' })).toBe('admin.shared.status.active');
    expect(labelOf({})).toBe('admin.shared.status.active');
  });

  it('still accepts the numeric form, for a caller or deployment that sends it', () => {
    expect(labelOf({ pendingActions: 1 })).toBe('admin.identity.users.status.invited');
  });

  // The backend names two composites and serialises an exact match as that single
  // name. A local table without them parsed "Obligations" to 0 and rendered an
  // account owing all three obligations as plain "active".
  it('reads the named composite "Obligations" as owing a password change', () => {
    expect(labelOf({ pendingActions: 'Obligations' })).toBe(
      'admin.identity.users.status.mustChangePassword'
    );
  });

  it('reads the named composite "Blocking" as an un-accepted invitation', () => {
    expect(labelOf({ pendingActions: 'Blocking', isLockedOut: true })).toBe(
      'admin.identity.users.status.invited'
    );
  });
});
