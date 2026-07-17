import { defineComponent } from 'vue'
import { useLoadingBar, type LoadingBarApi } from 'naive-ui'

// 路由加载进度条桥:useLoadingBar 只能在 Provider 内的 setup 里调,
// 路由守卫在组件树外,故用模块单例转接。Provider 未挂载前静默 no-op。
let api: LoadingBarApi | null = null

export const loadingBar = {
  start: () => api?.start(),
  finish: () => api?.finish(),
  error: () => api?.error(),
}

/** 挂在 n-loading-bar-provider 内的空渲染组件,负责把实例交给模块单例。 */
export const LoadingBarBridge = defineComponent({
  name: 'LoadingBarBridge',
  setup() {
    api = useLoadingBar()
    return () => null
  },
})
