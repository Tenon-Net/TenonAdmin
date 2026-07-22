import type { ComponentType } from 'react'
import { AuroraGlass } from './skins/AuroraGlass'
import { SplitPanel } from './skins/SplitPanel'
import { Spotlight } from './skins/Spotlight'

/** 登录皮肤注册表(移植 Vue `views/login/skins.ts`)。壳按 id 选择、切换、记忆。 */
export interface LoginSkin {
  id: string
  label: string
  component: ComponentType
}

export const LOGIN_SKINS: LoginSkin[] = [
  { id: 'aurora', label: '极光', component: AuroraGlass },
  { id: 'split', label: '双栏', component: SplitPanel },
  { id: 'spotlight', label: '聚光', component: Spotlight },
]

export const DEFAULT_SKIN = 'aurora'
