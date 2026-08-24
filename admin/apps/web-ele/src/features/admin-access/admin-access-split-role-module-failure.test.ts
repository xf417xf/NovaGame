import type {
  SplitRoleEffectsPackageRoot,
  SplitRoleModuleRequestFailureOutcome,
  SplitRoleModuleResource,
  SplitRoleUnclassifiedNetworkModuleResource,
  SplitRoleWorkspacePackageRoot,
} from '../../../e2e/module-request-failure-diagnostics';

import { describe, expect, it } from 'vitest';

import {
  classifySplitRoleModuleRequestFailureText,
  describeSplitRoleUnclassifiedNetworkModuleResource,
} from '../../../e2e/module-request-failure-diagnostics';

describe('split-role module request failure diagnostics', () => {
  const cases: ReadonlyArray<
    readonly [string | undefined, SplitRoleModuleRequestFailureOutcome]
  > = [
    [undefined, 'request-missing-details'],
    ['net::ERR_ABORTED', 'request-aborted'],
    ['net::ERR_FAILED', 'request-failed'],
    ['net::ERR_NAME_NOT_RESOLVED', 'request-name-resolution'],
    ['net::ERR_TIMED_OUT', 'request-timeout'],
    ['net::ERR_NETWORK_UNREACHABLE', 'request-reachability'],
    ['net::ERR_PROXY_CONNECTION_FAILED', 'request-proxy'],
    ['net::ERR_HTTP2_PROTOCOL_ERROR', 'request-protocol'],
    ['net::ERR_INVALID_RESPONSE', 'request-response'],
    ['net::ERR_BLOCKED_BY_CLIENT', 'request-policy'],
    ['net::ERR_CERT_AUTHORITY_INVALID', 'request-tls'],
    ['net::ERR_CONNECTION_REFUSED', 'request-connection'],
    ['net::ERR_CACHE_MISS', 'request-network-unclassified'],
    ['unrecognized failure', 'request-other'],
  ];
  const resourceCases: ReadonlyArray<
    readonly [
      SplitRoleModuleResource,
      SplitRoleWorkspacePackageRoot | undefined,
      SplitRoleEffectsPackageRoot | undefined,
      SplitRoleUnclassifiedNetworkModuleResource,
    ]
  > = [
    ['bootstrap', undefined, undefined, 'bootstrap'],
    ['direct-node-module', undefined, undefined, 'direct node module'],
    [
      'filesystem-app-source',
      undefined,
      undefined,
      'filesystem application source',
    ],
    ['filesystem-dependency', undefined, undefined, 'filesystem dependency'],
    [
      'filesystem-unclassified',
      undefined,
      undefined,
      'filesystem unclassified',
    ],
    [
      'filesystem-workspace-internal',
      undefined,
      undefined,
      'filesystem workspace internal',
    ],
    [
      'filesystem-workspace-package',
      undefined,
      undefined,
      'filesystem workspace package',
    ],
    ['optimized-dependency', undefined, undefined, 'optimized dependency'],
    ['source-outside-views', undefined, undefined, 'source outside views'],
    ['source-view', undefined, undefined, 'source view'],
    ['unknown', undefined, undefined, 'unknown'],
    ['vite-client', undefined, undefined, 'vite client'],
    ['vite-virtual', undefined, undefined, 'vite virtual'],
    [
      'filesystem-workspace-package',
      'constants',
      undefined,
      'workspace constants',
    ],
    ['filesystem-workspace-package', 'core', undefined, 'workspace core'],
    ['filesystem-workspace-package', 'effects', undefined, 'workspace effects'],
    [
      'filesystem-workspace-package',
      'effects',
      'access',
      'workspace effects access',
    ],
    [
      'filesystem-workspace-package',
      'effects',
      'common-ui',
      'workspace effects common UI',
    ],
    [
      'filesystem-workspace-package',
      'effects',
      'hooks',
      'workspace effects hooks',
    ],
    [
      'filesystem-workspace-package',
      'effects',
      'layouts',
      'workspace effects layouts',
    ],
    [
      'filesystem-workspace-package',
      'effects',
      'other',
      'workspace effects other',
    ],
    [
      'filesystem-workspace-package',
      'effects',
      'plugins',
      'workspace effects plugins',
    ],
    [
      'filesystem-workspace-package',
      'effects',
      'request',
      'workspace effects request',
    ],
    ['filesystem-workspace-package', 'icons', undefined, 'workspace icons'],
    ['filesystem-workspace-package', 'locales', undefined, 'workspace locales'],
    ['filesystem-workspace-package', 'other', undefined, 'workspace other'],
    [
      'filesystem-workspace-package',
      'preferences',
      undefined,
      'workspace preferences',
    ],
    ['filesystem-workspace-package', 'stores', undefined, 'workspace stores'],
    ['filesystem-workspace-package', 'styles', undefined, 'workspace styles'],
    ['filesystem-workspace-package', 'types', undefined, 'workspace types'],
    ['filesystem-workspace-package', 'utils', undefined, 'workspace utils'],
  ];

  it.each(cases)(
    'classifies a request failure without exposing its text',
    (errorText, expectedOutcome) => {
      expect(classifySplitRoleModuleRequestFailureText(errorText)).toBe(
        expectedOutcome,
      );
    },
  );

  it.each(resourceCases)(
    'describes an unclassified request with finite module metadata',
    (resource, workspacePackageRoot, effectsPackageRoot, expectedResource) => {
      expect(
        describeSplitRoleUnclassifiedNetworkModuleResource(
          resource,
          workspacePackageRoot,
          effectsPackageRoot,
        ),
      ).toBe(expectedResource);
    },
  );
});
