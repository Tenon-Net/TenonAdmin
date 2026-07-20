// happy-dom 在 Node 22+ 下 `window.localStorage` 现存已知为 undefined(Node 自带的实验性全局
// localStorage 抢占了探测)。而 zustand 的 persist 中间件在**建 store 时**就把 `localStorage` 取走
// (`createJSONStorage(() => localStorage)`),取到 undefined 就整条挂掉 —— 症状是每个 `set()` 都抛
// `Cannot read properties of undefined (reading 'setItem')`,而报错栈指向业务 action,和存储八竿子打不着。
//
// 所以桩必须在**任何 store 模块被 import 之前**装好,这正是 setupFiles 的位置;写在 spec 顶部来不及。
// 用内存实现而不是修 happy-dom:Node 那个跨进程持久化的 localStorage 会让 spec 文件之间互相串味。
// 注意粒度:`setupFiles` 每个 spec **文件**执行一次,所以隔离是**文件级**的 —— 同一文件里的多个
// `test` 共享同一个 Map。要 test 级隔离的 spec 自己写 `beforeEach(() => localStorage.clear())`,
// 别指望这里给。(不在这里统一加,是因为 R4 要搬的那批 store spec 是照现在这个语义写的。)
function memoryStorage(): Storage {
  const data = new Map<string, string>()
  return {
    getItem: (k) => data.get(k) ?? null,
    setItem: (k, v) => void data.set(k, String(v)),
    removeItem: (k) => void data.delete(k),
    clear: () => data.clear(),
    key: (i) => Array.from(data.keys())[i] ?? null,
    get length() {
      return data.size
    },
  } as Storage
}

globalThis.localStorage = memoryStorage()
globalThis.sessionStorage = memoryStorage()
