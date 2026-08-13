import type { UserInfo } from '@vben/types';

import type { AdministratorInfo } from '../nova-game-contracts';

import { requestClient } from '#/api/request';

export async function getUserInfoApi() {
  const administrator =
    await requestClient.get<AdministratorInfo>('/user/info');

  return {
    avatar: '',
    desc: administrator.roles.join(', '),
    homePath: '/player-equipment-grants',
    realName: administrator.displayName,
    roles: administrator.roles,
    token: '',
    userId: administrator.id,
    username: administrator.userName,
  } satisfies UserInfo;
}
