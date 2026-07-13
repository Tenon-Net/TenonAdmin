import type { RouteRecordRaw } from 'vue-router'
import { namedPage } from './namedPage'

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
    // 不设 redirect:它在 resolve 阶段求值,早于全局守卫——那时动态路由还没重建、menuTree 还空,
    // 算出的首页必然是错的。'/' 的落点改由守卫在路由就绪后决定(见 router/index.ts)。
    children: [
      // 工作台不在此:它是每个应用自己的一条菜单(后端播种),由 useAuthMenu 动态注册。
      {
        path: '/personal/profile',
        name: 'personal-profile',
        component: namedPage('personal-profile', () => import('@/views/personal/profile.vue')),
        meta: { title: 'menu.profile' },
      },
      {
        path: '/personal/password',
        name: 'personal-password',
        component: namedPage('personal-password', () => import('@/views/personal/password.vue')),
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
