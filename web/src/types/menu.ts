// 菜单 / 模块领域类型 —— UI 层与 composables 消费这份干净类型,不直接用 openapi 生成的 verbose 类型。
// 与后端 MenuNode / ModuleItem DTO 对齐(设计 §16 / M1.5)。

/** 菜单类型:后端整型枚举。门户菜单树已剥掉 Button(仅 Catalog + Menu 返回)。 */
export enum MenuType {
  Catalog = 1,
  Menu = 2,
  Button = 3,
}

/** 菜单树节点(后端 MenuNode;无 permission / moduleId 字段——moduleId 仅服务端分区用)。 */
export interface MenuNode {
  id: number
  parentId: number
  type: MenuType
  title: string
  path?: string
  component?: string
  icon?: string
  sort: number
  visible: boolean
  children: MenuNode[]
}

/** 应用/模块(后端 ModuleItem)。 */
export interface AppModule {
  id: number
  code: string
  title: string
  icon?: string
  defaultRoute?: string
  sort: number
}

/** 菜单管理端树节点(后端 MenuTreeNode;全字段,含权限码 / 启用 / 所属模块 / 按钮节点)。 */
export interface MenuTreeNode {
  id: number
  parentId: number
  type: MenuType
  title: string
  permission: string
  sort: number
  enabled: boolean
  moduleId?: number | null
  path?: string | null
  component?: string | null
  icon?: string | null
  visible: boolean
  children: MenuTreeNode[]
}

/** 菜单新增/编辑入参(后端 MenuInput;moduleId 仅顶级目录有效)。 */
export interface MenuInput {
  parentId: number
  type: MenuType
  title: string
  permission: string
  sort: number
  enabled: boolean
  moduleId?: number | null
  path?: string | null
  component?: string | null
  icon?: string | null
  visible: boolean
}

/** 菜单树节点 → 全量入参:菜单页 StatusSwitch 行内改状态与编辑弹窗回显共用(后端无独立启停端点,均走全量 update)。 */
export const toMenuInput = (r: MenuTreeNode): MenuInput => ({
  parentId: r.parentId, type: r.type, title: r.title, permission: r.permission, sort: r.sort,
  enabled: r.enabled, moduleId: r.moduleId ?? null,
  path: r.path ?? '', component: r.component ?? '', icon: r.icon ?? '', visible: r.visible,
})
