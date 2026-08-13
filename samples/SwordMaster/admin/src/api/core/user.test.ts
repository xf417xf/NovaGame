import { beforeEach, describe, expect, it, vi } from 'vitest';

const get = vi.hoisted(() => vi.fn());

vi.mock('#/api/request', () => ({
  requestClient: { get },
}));

import { getUserInfoApi } from './user';

describe('administrator user API', () => {
  beforeEach(() => get.mockReset());

  it('maps the current administrator and SwordMaster home route', async () => {
    get.mockResolvedValue({
      displayName: 'Operations Admin',
      id: '17b40e7d-01ca-45f7-b017-84180449678e',
      permissionCodes: ['SwordMaster.PlayerAssets.EquipmentGrants.Manage'],
      roles: ['Administrators'],
      userName: 'operator',
    });

    await expect(getUserInfoApi()).resolves.toEqual({
      avatar: '',
      desc: 'Administrators',
      homePath: '/player-equipment-grants',
      realName: 'Operations Admin',
      roles: ['Administrators'],
      token: '',
      userId: '17b40e7d-01ca-45f7-b017-84180449678e',
      username: 'operator',
    });
    expect(get).toHaveBeenCalledWith('/user/info');
  });
});
