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

// 菜单可落地的页面组件:`/src/views/**/*.tsx`,减去几类**不是菜单落点**的文件:
//   `.spec.tsx`      —— 测试文件(否则 `hasView('x.spec')` 返真、也污染菜单管理下拉)。
//   `login/**`       —— 登录页是**静态路由**(App 直接 import),不是菜单能配的组件。
//   `module/**`      —— 应用选择页是 Protected 里的**静态路由**,同理。
//   `embed/**`       —— iframe 视图由 buildRoutes **静态 import**,菜单走的是 component=URL 那条 iframe 分支。
//   `_placeholder/**`—— 内部共用占位实现,不是独立页面。
// 这几类若留在 glob 里,既会同时被静态 + 动态 import 而**永远无法 code-split**(build 告警的来源),
// 又会让管理员在下拉里选到一个坏页(embed/iframe 无 src、_placeholder 占位、login/module 是壳)。
// **加静态路由页时都要同步排进来**,否则这个冲突会复发。
type ViewModule = { default: ComponentType }
export type ViewGlob = Record<string, () => Promise<ViewModule>>
const views = import.meta.glob<ViewModule>([
  '/src/views/**/*.tsx',
  '!/src/views/**/*.spec.tsx',
  '!/src/views/login/**',
  '!/src/views/module/**',
  '!/src/views/embed/**',
  '!/src/views/personal/**',
  '!/src/views/_placeholder/**',
]) as ViewGlob

// 上面 glob 已排除的目录前缀 —— `viewKeysFrom` 也照它过滤,好让**注入假表**的测试与真实 glob 结论一致
// (假表不经 glob 的负模式,只能靠这里的显式过滤把内部组件挡在下拉外)。
const NON_PAGE_PREFIXES = ['login/', 'module/', 'embed/', 'personal/', '_placeholder/']

/**
 * glob 键(`/src/views/system/user/index.tsx`)→ 菜单 component 值(`system/user/index`)。
 * 供菜单管理表单的「组件路径」下拉:由这张 glob 表反推,天然不漂移。手敲错一个字符,
 * `menuToRouteDescriptors` 只会 warn 然后跳过,菜单项**静默消失**——给下拉就从根上没了这个坑。
 * 排除登录页(它是静态路由,不是菜单能配的落点)。
 */
export function viewKeysFrom(glob: ViewGlob): string[] {
  return Object.keys(glob)
    .map((k) => k.replace(/^\/src\/views\//, '').replace(/\.tsx$/, ''))
    .filter((k) => !NON_PAGE_PREFIXES.some((p) => k.startsWith(p)))
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
