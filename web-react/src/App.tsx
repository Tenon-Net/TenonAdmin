/**
 * R3 的壳,只为让四件套跑得起来。R4 落地框架无关层之后,这里会换成 B1 那个**探针页**
 * ——它故意不是 hello world:只渲染文字的壳在主题桥/i18n/store 任何一条假设坏掉时照样绿,
 * 所以那些假设要被逐条渲染成肉眼可见的红字。到 B8 布局壳落地时整个删掉。
 */
export default function App() {
  return (
    <main style={{ padding: 24, fontFamily: 'system-ui' }}>
      <h1>TenonAdmin · React 模板</h1>
      <p>脚手架已就位(R3)。框架无关层见 R4。</p>
      <p style={{ color: '#888' }}>v{__APP_VERSION__}</p>
    </main>
  )
}
