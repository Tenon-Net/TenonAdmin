/**
 * 是否 http(s) 绝对 URL。外链/iframe 菜单的约定判据:
 * - 外链菜单:节点 path 为 URL(component 空)→ 点击 window.open,不建路由。
 * - 内嵌 iframe 菜单:节点 component 为 URL(path 为内部路径)→ 注册通用 iframe 视图,URL 进 meta.iframeSrc。
 */
export function isHttpUrl(s: string | undefined | null): boolean {
  return !!s && /^https?:\/\//i.test(s)
}
