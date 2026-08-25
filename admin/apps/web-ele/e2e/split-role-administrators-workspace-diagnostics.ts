export type SplitRoleAdministratorsCreateCommandState =
  | 'absent'
  | 'not-observed'
  | 'present';

export type SplitRoleAdministratorsWorkspaceDiagnosticPriority =
  | 'create-command-exposed'
  | 'module-failure'
  | 'none';

export function resolveSplitRoleAdministratorsWorkspaceDiagnosticPriority(
  createCommandState: SplitRoleAdministratorsCreateCommandState,
  moduleFailureObserved: boolean,
): SplitRoleAdministratorsWorkspaceDiagnosticPriority {
  if (createCommandState === 'present') {
    return 'create-command-exposed';
  }

  return moduleFailureObserved ? 'module-failure' : 'none';
}
