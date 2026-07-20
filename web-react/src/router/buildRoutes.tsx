import { lazy, Suspense, type ComponentType, type ReactElement } from 'react'
import type { RouteObject } from 'react-router-dom'
import { menuToRouteDescriptors, type RouteDescriptor } from './menuRoutes'
import IframeView from '@/views/embed/iframe'
import type { MenuNode } from '@/types/menu'

/**
 * 描述符 → react-router `RouteObject`。决策(哪些节点、建成什么)在 `menuRoutes.ts`,这里只做机械落地:
 * view 描述符 → `React.lazy` + `Suspense` 包起来的页面;iframe 描述符 → `IframeView`。
 *
 * **不需要 Vue 版 `router/index.ts` 那个 `return to.fullPath` 重解析 trick**:Vue 的动态路由是命令式
 * `addRoute` 挂上去的,重建后当前 URL 得手动重解析;React 这里路由是**从 `menuTree` 派生的普通数组**,
 * 交给 `useRoutes(routes)`,`menuTree` 一变 React 自然重渲染、重新匹配,没有"挂了但没匹配"的空窗。
 *
 * 标题/图标/路由名进 `handle`(react-router 的任意附加数据位),留给 B8 的布局壳读面包屑与选中态。
 */
export interface RouteHandle {
  name: string
  title: string
  icon?: string
}

// 所有页面组件:`/src/views/**/*.tsx` → loader。**排除 `.spec.tsx`**(测试文件不是页面,
// 混进来会让 `hasView` 对一个 `xxx.spec` 键返回真、也污染菜单管理的组件下拉)。
type ViewModule = { default: ComponentType }
export type ViewGlob = Record<string, () => Promise<ViewModule>>
const views = import.meta.glob<ViewModule>(['/src/views/**/*.tsx', '!/src/views/**/*.spec.tsx']) as ViewGlob

/**
 * glob 键(`/src/views/system/user/index.tsx`)→ 菜单 component 值(`system/user/index`)。
 * 供菜单管理表单的「组件路径」下拉:由这张 glob 表反推,天然不漂移。手敲错一个字符,
 * `menuToRouteDescriptors` 只会 warn 然后跳过,菜单项**静默消失**——给下拉就从根上没了这个坑。
 * 排除登录页(它是静态路由,不是菜单能配的落点)。
 */
export function viewKeysFrom(glob: ViewGlob): string[] {
  return Object.keys(glob)
    .map((k) => k.replace(/^\/src\/views\//, '').replace(/\.tsx$/, ''))
    .filter((k) => !k.startsWith('login/'))
    .sort()
}

export const viewComponentPaths: string[] = viewKeysFrom(views)

function elementFor(d: RouteDescriptor, glob: ViewGlob): ReactElement {
  if (d.iframeSrc !== undefined) return <IframeView src={d.iframeSrc} />
  // component 已由 menuToRouteDescriptors 用同一张表的 hasView 校验过存在,这里 glob[key] 必有。
  const loader = glob[`/src/views/${d.component}.tsx`]!
  const Lazy = lazy(loader)
  return (
    <Suspense fallback={null}>
      <Lazy />
    </Suspense>
  )
}

/**
 * 菜单树 → 路由数组(喂 `useRoutes`)。`glob` 注入,默认用真实 `import.meta.glob`;测试传假表。
 * `hasView` 由同一张表派生,保证「校验存在」与「取 loader」用的是同一个真相源。
 */
export function buildRoutes(tree: MenuNode[], glob: ViewGlob = views): RouteObject[] {
  const hasView = (component: string) => `/src/views/${component}.tsx` in glob
  return menuToRouteDescriptors(tree, hasView).map((d) => ({
    path: d.path,
    element: elementFor(d, glob),
    handle: { name: d.name, title: d.title, icon: d.icon } satisfies RouteHandle,
  }))
}
