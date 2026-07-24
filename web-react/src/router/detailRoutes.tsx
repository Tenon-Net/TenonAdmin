import { lazy, Suspense, type ComponentType, type LazyExoticComponent, type ReactElement } from 'react'
import { matchPath, type RouteObject } from 'react-router-dom'
import type { RouteHandle } from './buildRoutes'

type DetailViewModule = { default: ComponentType }
export type DetailViewGlob = Record<string, () => Promise<DetailViewModule>>

const detailViews = import.meta.glob<DetailViewModule>('/src/views/**/detail.tsx') as DetailViewGlob
const lazyComponents = new WeakMap<() => Promise<DetailViewModule>, LazyExoticComponent<ComponentType>>()

export interface DetailRouteDescriptor {
  path: string
  name: string
  loader: () => Promise<DetailViewModule>
}

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

export function buildDetailRoutes(glob: DetailViewGlob = detailViews): RouteObject[] {
  return detailRouteDescriptors(glob).map((route) => ({
    path: route.path,
    element: elementFor(route.loader),
    handle: { name: route.name, title: 'common.detail' } satisfies RouteHandle,
  }))
}

export function detailMetaForPath(pathname: string, glob: DetailViewGlob = detailViews): { title: string; noCache: true } | undefined {
  return detailRouteDescriptors(glob).some((route) => matchPath({ path: route.path, end: true }, pathname))
    ? { title: 'common.detail', noCache: true }
    : undefined
}
