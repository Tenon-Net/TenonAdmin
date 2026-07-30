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
  // 外部登录 / SSO 回调结果页(批次 D):IdP → 后端回调 → 302 到此,凭票据换令牌或提示错误。
  // 公开路由(登录前也要能到);不进壳,独立整屏处理。
  {
    path: '/oauth/callback',
    name: 'oauth-callback',
    component: () => import('@/views/oauth/callback.vue'),
    meta: { public: true },
  },
  {
    path: '/mfa/bind',
    name: 'mfa-bind',
    component: () => import('@/views/mfa/index.vue'),
    meta: { public: true },
  },
  {
    path: '/module',
    name: 'module',
    component: () => import('@/views/module/index.vue'),
    meta: { title: 'module.choose' },   // i18n 键(与兄弟静态路由 menu.* 同法),不硬编码中文
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
      // 我的通知:走 [ActiveSession] 的 notice/mine,人人可读 —— 故是静态路由而非菜单
      // (进菜单就得播种 + 给每个角色授权,那才是真正的多余功课)。入口在顶栏铃铛的「查看全部」。
      {
        path: '/personal/notice',
        name: 'personal-notice',
        component: namedPage('personal-notice', () => import('@/views/personal/notice.vue')),
        meta: { title: 'menu.notice' },
      },
      // 我的会话:理由同上 —— [ActiveSession] 人人可用,静态路由不进菜单;入口在顶栏用户下拉。
      {
        path: '/personal/sessions',
        name: 'personal-sessions',
        component: namedPage('personal-sessions', () => import('@/views/personal/sessions.vue')),
        meta: { title: 'menu.sessions' },
      },
      // 账号绑定(外部登录 / SSO,批次 D):[ActiveSession] 人人可用,静态路由不进菜单;入口在顶栏用户下拉。
      {
        path: '/personal/bindings',
        name: 'personal-bindings',
        component: namedPage('personal-bindings', () => import('@/views/personal/bindings.vue')),
        meta: { title: 'menu.bindings' },
      },
      // 404 挂在壳内(而非顶级):打错一个 URL 不该把人甩出侧边栏、标签栏和退出按钮之外。
      // 未登录者到不了这里——守卫先于 public 判定就把他弹去登录页;深链刷新也不会闪 404,
      // 守卫在导航确认前先重建动态路由再重解析(见 router/index.ts)。
      // afterEach 按 name 'not-found' 拦了 addTab,所以它不会在标签栏留下一条。
      {
        path: '/:pathMatch(.*)*',
        name: 'not-found',
        component: namedPage('not-found', () => import('@/views/error/404.vue')),
        meta: { public: true },
      },
    ],
  },
]
