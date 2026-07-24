import { lazy, Suspense, type ComponentType, type LazyExoticComponent, type ReactElement } from 'react'
import { matchPath, type RouteObject } from 'react-router-dom'
import type { RouteHandle } from './buildRoutes'

type DetailViewModule = { default: ComponentType }
/** Vite 详情文件 glob 产生的绝对源码键与懒加载器表。 */
export type DetailViewGlob = Record<string, () => Promise<DetailViewModule>>

const detailViews = import.meta.glob<DetailViewModule>('/src/views/**/detail.tsx') as DetailViewGlob
const lazyComponents = new WeakMap<() => Promise<DetailViewModule>, LazyExoticComponent<ComponentType>>()

/** 一份详情文件生成的稳定路由契约。 */
export interface DetailRouteDescriptor {
  /** `/<views 下目录>/:id/detail`，参数名固定为 `id`。 */
  path: string
  /** `detail-<目录段以短横线连接>`，供路由元数据与诊断使用。 */
  name: string
  /** 原始 glob loader；组件只在路由命中时加载。 */
  loader: () => Promise<DetailViewModule>
}

/**
 * 把详情 glob 转成与文件枚举顺序无关的稳定描述符；真实构建和注入假 glob 的测试共用这条边界。
 */
export function detailRouteDescriptors(glob: DetailViewGlob): DetailRouteDescriptor[] {
  return Object.entries(glob)
    .map(([key, loader]) => {
      const directory = key.replace(/^\/src\/views\//, '').replace(/\/detail\.tsx$/, '')
      return { path: `/${directory}/:id/detail`, name: `detail-${directory.replaceAll('/', '-')}`, loader }
    })
    .sort((a, b) => a.path.localeCompare(b.path))
}

function elementFor(loader: () => Promise<DetailViewModule>): ReactElement {
  let Lazy = lazyComponents.get(loader)
  if (!Lazy) {
    Lazy = lazy(loader)
    lazyComponents.set(loader, Lazy)
  }
  return <Suspense fallback={null}><Lazy /></Suspense>
}

/** 将全部约定式详情文件物化成挂入 `LayoutShell` 的懒加载 RouteObject。 */
export function buildDetailRoutes(glob: DetailViewGlob = detailViews): RouteObject[] {
  return detailRouteDescriptors(glob).map((route) => ({
    path: route.path,
    element: elementFor(route.loader),
    handle: { name: route.name, title: 'common.detail' } satisfies RouteHandle,
  }))
}

/**
 * 为约定式详情地址提供标签元数据。标签键由调用方保留具体 pathname，因此不同记录可同时打开；
 * `noCache` 固定为 true，保证复访重新取数。
 */
export function detailMetaForPath(pathname: string, glob: DetailViewGlob = detailViews): { title: string; noCache: true } | undefined {
  return detailRouteDescriptors(glob).some((route) => matchPath({ path: route.path, end: true }, pathname))
    ? { title: 'common.detail', noCache: true }
    : undefined
}
