import { beforeEach, describe, expect, it, vi } from 'vitest';

const request = vi.hoisted(() => ({
  get: vi.fn(),
  publicPost: vi.fn(),
  publicPostVoid: vi.fn(),
  refreshAccessToken: vi.fn(),
}));

vi.mock('#/api/request', () => ({
  publicPost: request.publicPost,
  publicPostVoid: request.publicPostVoid,
  refreshAccessToken: request.refreshAccessToken,
  requestClient: { get: request.get },
}));

import {
  getAccessCodesApi,
  loginApi,
  logoutApi,
  refreshTokenApi,
} from './auth';

describe('administrator authentication API', () => {
  beforeEach(() => {
    request.get.mockReset();
    request.publicPost.mockReset();
    request.publicPostVoid.mockReset();
    request.refreshAccessToken.mockReset();
  });

  it('maps the Vben login model to the existing server contract', async () => {
    request.publicPost.mockResolvedValue({ accessToken: 'access-token' });

    await expect(
      loginApi({ password: 'secret', username: 'operator' }),
    ).resolves.toEqual({ accessToken: 'access-token' });
    expect(request.publicPost).toHaveBeenCalledWith('/auth/login', {
      password: 'secret',
      userName: 'operator',
    });
  });

  it('uses the cookie-backed refresh operation without a request body', async () => {
    request.refreshAccessToken.mockResolvedValue({
      accessToken: 'rotated-access-token',
    });

    await expect(refreshTokenApi()).resolves.toEqual({
      accessToken: 'rotated-access-token',
    });
    expect(request.refreshAccessToken).toHaveBeenCalledWith();
  });

  it('uses the cookie-backed logout operation without a request body', async () => {
    request.publicPostVoid.mockResolvedValue(undefined);

    await expect(logoutApi()).resolves.toBeUndefined();
    expect(request.publicPostVoid).toHaveBeenCalledWith('/auth/logout');
  });

  it('loads permission codes from the protected server endpoint', async () => {
    request.get.mockResolvedValue([
      'SwordMaster.PlayerAssets.EquipmentGrants.Manage',
    ]);

    await expect(getAccessCodesApi()).resolves.toEqual([
      'SwordMaster.PlayerAssets.EquipmentGrants.Manage',
    ]);
    expect(request.get).toHaveBeenCalledWith('/auth/codes');
  });
});
