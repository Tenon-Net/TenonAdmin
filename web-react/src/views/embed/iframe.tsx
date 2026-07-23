/** 内嵌 iframe 视图:菜单 component 为外部 URL 时的通用承载页(见 menuRoutes 的 iframe 分支)。 */
export default function IframeView({ src }: { src: string }) {
  return (
    <iframe
      src={src}
      title={src}
      style={{ width: '100%', height: '100%', border: 'none', display: 'block' }}
    />
  )
}
