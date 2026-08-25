import type {
  SplitRoleAdministratorsCreateCommandState,
  SplitRoleAdministratorsWorkspaceDiagnosticPriority,
} from '../../../e2e/split-role-administrators-workspace-diagnostics';

import { describe, expect, it } from 'vitest';

import { resolveSplitRoleAdministratorsWorkspaceDiagnosticPriority } from '../../../e2e/split-role-administrators-workspace-diagnostics';

describe('split-role Administrators workspace diagnostics', () => {
  const cases: ReadonlyArray<
    readonly [
      SplitRoleAdministratorsCreateCommandState,
      boolean,
      SplitRoleAdministratorsWorkspaceDiagnosticPriority,
    ]
  > = [
    ['present', true, 'create-command-exposed'],
    ['absent', true, 'module-failure'],
    ['not-observed', true, 'module-failure'],
    ['present', false, 'create-command-exposed'],
    ['absent', false, 'none'],
    ['not-observed', false, 'none'],
  ];

  it.each(cases)(
    'preserves the concrete permission failure before an unrelated module diagnostic',
    (createCommandState, moduleFailureObserved, expectedPriority) => {
      expect(
        resolveSplitRoleAdministratorsWorkspaceDiagnosticPriority(
          createCommandState,
          moduleFailureObserved,
        ),
      ).toBe(expectedPriority);
    },
  );
});
