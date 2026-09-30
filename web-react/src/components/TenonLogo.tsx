/** 品牌仓库的透明版 PNG，亮暗主题共用。对应 Vue 侧 `TenonLogo.vue`。 */
export function TenonLogo({ size = 28 }: { size?: number }) {
  return <img src="/tenon-mark.png" width={size} height={size} alt="Tenon" draggable={false} />
}
