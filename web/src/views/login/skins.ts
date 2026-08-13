import type { Component } from 'vue'
import AuroraGlass from './skins/AuroraGlass.vue'
import SplitPanel from './skins/SplitPanel.vue'
import Spotlight from './skins/Spotlight.vue'

export interface LoginSkin {
  id: string
  label: string
  component: Component
}

export const LOGIN_SKINS: LoginSkin[] = [
  { id: 'aurora', label: 'login.skinAurora', component: AuroraGlass },
  { id: 'split', label: 'login.skinSplit', component: SplitPanel },
  { id: 'spotlight', label: 'login.skinSpotlight', component: Spotlight },
]

export const DEFAULT_SKIN = 'aurora'
