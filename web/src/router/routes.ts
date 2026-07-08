import type { RouteRecordRaw } from 'vue-router'

// 静态路由(白名单 + 布局壳 + 固定页)。业务页由菜单树动态注册到 'layout' 下。
export const staticRoutes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'login',
    component: () => import('@/views/login/index.vue'),
    meta: { public: true },
  },
  {
    path: '/module',
    name: 'module',
    component: () => import('@/views/module/index.vue'),
    meta: { title: '选择应用' },
  },
  {
    path: '/',
    name: 'layout',
    component: () => import('@/layouts/default.vue'),
    redirect: '/workbench',
    children: [
      {
        path: '/workbench',
        name: 'workbench',
        component: () => import('@/views/dashboard/workbench.vue'),
        meta: { title: 'menu.workbench' },
      },
      {
        path: '/personal/profile',
        name: 'personal-profile',
        component: () => import('@/views/personal/profile.vue'),
        meta: { title: 'menu.profile' },
      },
      {
        path: '/personal/password',
        name: 'personal-password',
        component: () => import('@/views/personal/password.vue'),
        meta: { title: 'menu.password' },
      },
    ],
  },
  {
    path: '/:pathMatch(.*)*',
    name: 'not-found',
    component: () => import('@/views/error/404.vue'),
    meta: { public: true },
  },
]
