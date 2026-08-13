import type { RouteRecordRaw } from 'vue-router';

import { PlayerEquipmentGrantPermission } from '#/features/player-equipment-grants/permissions';
import { $t } from '#/locales';

const routes: RouteRecordRaw[] = [
  {
    component: () => import('#/views/player-equipment-grants/index.vue'),
    meta: {
      affixTab: true,
      authority: [PlayerEquipmentGrantPermission.Manage],
      icon: 'lucide:package-plus',
      order: -1,
      title: $t('page.playerEquipmentGrants.title'),
    },
    name: 'PlayerEquipmentGrants',
    path: '/player-equipment-grants',
  },
];

export default routes;
