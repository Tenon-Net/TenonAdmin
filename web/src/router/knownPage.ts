// 已登录但菜单里没有这条路由时，命中真实页面应是 403，而不是「页面不存在」。
const views = import.meta.glob([
  '/src/views/**/*.vue',
  '!/src/views/**/components/**',
  '!/src/views/login/**',
  '!/src/views/module/**',
  '!/src/views/oauth/**',
  '!/src/views/error/**',
  '!/src/views/embed/**',
  '!/src/views/personal/**',
  '!/src/views/_placeholder/**',
])

const pageKeys = new Set(
  Object.keys(views).map((key) => key.replace(/^\/src\/views\//, '').replace(/\.vue$/, '')),
)

/** 路径对应一份真实页面（含 index），但当前用户的动态路由没有把它挂出来。 */
export function isKnownPagePath(pathname: string): boolean {
  const trimmed = pathname.replace(/^\/+/, '').replace(/\/+$/, '')
  if (!trimmed || trimmed.split('/').some((part) => part === '..' || part === 'components')) return false
  return pageKeys.has(trimmed) || pageKeys.has(`${trimmed}/index`)
}
