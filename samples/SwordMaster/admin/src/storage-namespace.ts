const swordMasterAdminNamespace = 'swordmaster-admin';

interface StorageNamespaceEnvironment {
  PROD?: boolean;
  VITE_APP_NAMESPACE?: string;
  VITE_APP_VERSION?: string;
}

export function createStorageNamespace(
  environment: StorageNamespaceEnvironment,
): string {
  const configuredNamespace = environment.VITE_APP_NAMESPACE?.trim();
  if (configuredNamespace !== swordMasterAdminNamespace) {
    throw new Error(
      `VITE_APP_NAMESPACE must be '${swordMasterAdminNamespace}'.`,
    );
  }

  const appVersion = environment.VITE_APP_VERSION?.trim() || '0';
  const mode = environment.PROD ? 'prod' : 'dev';
  return `${swordMasterAdminNamespace}-${appVersion}-${mode}`;
}
