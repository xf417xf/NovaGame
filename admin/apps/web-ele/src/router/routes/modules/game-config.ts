import type { RouteRecordRaw } from 'vue-router';

import { GameConfigPermission } from '#/features/game-config/permissions';

const routes: RouteRecordRaw[] = [
  {
    component: () => import('#/views/game-config/index.vue'),
    meta: {
      affixTab: true,
      authority: [GameConfigPermission.Access],
      icon: 'lucide:sliders-horizontal',
      order: -1,
      title: 'page.gameConfig.title',
    },
    name: 'GameConfig',
    path: '/game-config',
  },
];

export default routes;
