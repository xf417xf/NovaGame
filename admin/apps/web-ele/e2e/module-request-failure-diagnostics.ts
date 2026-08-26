export type SplitRoleModuleRequestFailureOutcome =
  | 'request-aborted'
  | 'request-cache'
  | 'request-connection'
  | 'request-context'
  | 'request-failed'
  | 'request-missing-details'
  | 'request-name-resolution'
  | 'request-network-unclassified'
  | 'request-other'
  | 'request-policy'
  | 'request-protocol'
  | 'request-proxy'
  | 'request-reachability'
  | 'request-resource'
  | 'request-response'
  | 'request-timeout'
  | 'request-tls';

export type SplitRoleModuleResource =
  | 'bootstrap'
  | 'direct-node-module'
  | 'filesystem-app-source'
  | 'filesystem-dependency'
  | 'filesystem-unclassified'
  | 'filesystem-workspace-internal'
  | 'filesystem-workspace-package'
  | 'optimized-dependency'
  | 'source-outside-views'
  | 'source-view'
  | 'unknown'
  | 'vite-client'
  | 'vite-virtual';

export type SplitRoleEffectsPackageRoot =
  | 'access'
  | 'common-ui'
  | 'hooks'
  | 'layouts'
  | 'other'
  | 'plugins'
  | 'request';

export type SplitRoleWorkspacePackageRoot =
  | 'constants'
  | 'core'
  | 'effects'
  | 'icons'
  | 'locales'
  | 'other'
  | 'preferences'
  | 'stores'
  | 'styles'
  | 'types'
  | 'utils';

export type SplitRoleUnclassifiedNetworkModuleResource =
  | 'bootstrap'
  | 'direct node module'
  | 'filesystem application source'
  | 'filesystem dependency'
  | 'filesystem unclassified'
  | 'filesystem workspace internal'
  | 'filesystem workspace package'
  | 'optimized dependency'
  | 'source outside views'
  | 'source view'
  | 'unknown'
  | 'vite client'
  | 'vite virtual'
  | 'workspace constants'
  | 'workspace core'
  | 'workspace effects'
  | 'workspace effects access'
  | 'workspace effects common UI'
  | 'workspace effects hooks'
  | 'workspace effects layouts'
  | 'workspace effects other'
  | 'workspace effects plugins'
  | 'workspace effects request'
  | 'workspace icons'
  | 'workspace locales'
  | 'workspace other'
  | 'workspace preferences'
  | 'workspace stores'
  | 'workspace styles'
  | 'workspace types'
  | 'workspace utils';

export function classifySplitRoleModuleRequestFailureText(
  errorText: string | undefined,
): SplitRoleModuleRequestFailureOutcome {
  if (errorText === undefined) {
    return 'request-missing-details';
  }

  switch (errorText) {
    case 'net::ERR_ABORTED': {
      return 'request-aborted';
    }
    case 'net::ERR_FAILED': {
      return 'request-failed';
    }
  }
  if (
    ['net::ERR_CACHE_MISS', 'net::ERR_CACHE_OPEN_FAILURE'].includes(errorText)
  ) {
    return 'request-cache';
  }
  if (errorText === 'net::ERR_CONTEXT_SHUT_DOWN') {
    return 'request-context';
  }
  if (
    [
      'net::ERR_INSUFFICIENT_RESOURCES',
      'net::ERR_NO_BUFFER_SPACE',
      'net::ERR_OUT_OF_MEMORY',
      'net::ERR_PRECONNECT_MAX_SOCKET_LIMIT',
      'net::ERR_TEMPORARILY_THROTTLED',
    ].includes(errorText)
  ) {
    return 'request-resource';
  }
  if (
    [
      'net::ERR_DNS_MALFORMED_RESPONSE',
      'net::ERR_DNS_SERVER_FAILED',
      'net::ERR_DNS_TIMED_OUT',
      'net::ERR_NAME_NOT_RESOLVED',
    ].includes(errorText)
  ) {
    return 'request-name-resolution';
  }
  if (errorText === 'net::ERR_TIMED_OUT') {
    return 'request-timeout';
  }
  if (
    [
      'net::ERR_ADDRESS_INVALID',
      'net::ERR_ADDRESS_UNREACHABLE',
      'net::ERR_HOST_UNREACHABLE',
      'net::ERR_INTERNET_DISCONNECTED',
      'net::ERR_NETWORK_CHANGED',
      'net::ERR_NETWORK_UNREACHABLE',
    ].includes(errorText)
  ) {
    return 'request-reachability';
  }
  if (
    [
      'net::ERR_PROXY_CONNECTION_FAILED',
      'net::ERR_SOCKS_CONNECTION_FAILED',
      'net::ERR_SOCKS_CONNECTION_HOST_UNREACHABLE',
      'net::ERR_TUNNEL_CONNECTION_FAILED',
    ].includes(errorText)
  ) {
    return 'request-proxy';
  }
  if (
    [
      'net::ERR_ALPN_NEGOTIATION_FAILED',
      'net::ERR_HTTP2_PROTOCOL_ERROR',
      'net::ERR_QUIC_PROTOCOL_ERROR',
      'net::ERR_SPDY_PROTOCOL_ERROR',
    ].includes(errorText)
  ) {
    return 'request-protocol';
  }
  if (
    [
      'net::ERR_CONTENT_DECODING_FAILED',
      'net::ERR_CONTENT_LENGTH_MISMATCH',
      'net::ERR_EMPTY_RESPONSE',
      'net::ERR_INVALID_CHUNKED_ENCODING',
      'net::ERR_INVALID_RESPONSE',
      'net::ERR_RESPONSE_HEADERS_TRUNCATED',
    ].includes(errorText)
  ) {
    return 'request-response';
  }
  if (errorText.startsWith('net::ERR_BLOCKED_')) {
    return 'request-policy';
  }
  if (
    errorText.startsWith('net::ERR_CERT_') ||
    errorText.startsWith('net::ERR_SSL_') ||
    errorText === 'net::ERR_INSECURE_RESPONSE'
  ) {
    return 'request-tls';
  }
  if (errorText.startsWith('net::ERR_CONNECTION_')) {
    return 'request-connection';
  }
  if (errorText.startsWith('net::ERR_')) {
    return 'request-network-unclassified';
  }
  return 'request-other';
}

export function describeSplitRoleUnclassifiedNetworkModuleResource(
  resource: SplitRoleModuleResource,
  workspacePackageRoot?: SplitRoleWorkspacePackageRoot,
  effectsPackageRoot?: SplitRoleEffectsPackageRoot,
): SplitRoleUnclassifiedNetworkModuleResource {
  if (resource === 'filesystem-workspace-package') {
    switch (workspacePackageRoot) {
      case 'constants': {
        return 'workspace constants';
      }
      case 'core': {
        return 'workspace core';
      }
      case 'effects': {
        switch (effectsPackageRoot) {
          case 'access': {
            return 'workspace effects access';
          }
          case 'common-ui': {
            return 'workspace effects common UI';
          }
          case 'hooks': {
            return 'workspace effects hooks';
          }
          case 'layouts': {
            return 'workspace effects layouts';
          }
          case 'other': {
            return 'workspace effects other';
          }
          case 'plugins': {
            return 'workspace effects plugins';
          }
          case 'request': {
            return 'workspace effects request';
          }
        }
        return 'workspace effects';
      }
      case 'icons': {
        return 'workspace icons';
      }
      case 'locales': {
        return 'workspace locales';
      }
      case 'other': {
        return 'workspace other';
      }
      case 'preferences': {
        return 'workspace preferences';
      }
      case 'stores': {
        return 'workspace stores';
      }
      case 'styles': {
        return 'workspace styles';
      }
      case 'types': {
        return 'workspace types';
      }
      case 'utils': {
        return 'workspace utils';
      }
    }
    return 'filesystem workspace package';
  }

  switch (resource) {
    case 'bootstrap': {
      return 'bootstrap';
    }
    case 'direct-node-module': {
      return 'direct node module';
    }
    case 'filesystem-app-source': {
      return 'filesystem application source';
    }
    case 'filesystem-dependency': {
      return 'filesystem dependency';
    }
    case 'filesystem-unclassified': {
      return 'filesystem unclassified';
    }
    case 'filesystem-workspace-internal': {
      return 'filesystem workspace internal';
    }
    case 'optimized-dependency': {
      return 'optimized dependency';
    }
    case 'source-outside-views': {
      return 'source outside views';
    }
    case 'source-view': {
      return 'source view';
    }
    case 'unknown': {
      return 'unknown';
    }
    case 'vite-client': {
      return 'vite client';
    }
    case 'vite-virtual': {
      return 'vite virtual';
    }
  }
}
