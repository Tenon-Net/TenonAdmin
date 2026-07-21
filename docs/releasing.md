# 发布流程(Release Runbook)

TenonAdmin 前后端同仓、**同版本号一起发**。后端以 NuGet 包发布(版本由 tag 的 `-p:Version` 注入),前端版本号是登录页页脚的构建期常量(`web/package.json`)——两半必须一起 bump,否则用户看到的版本和实际跑的包对不上。

节奏:**开发在 `dev`,发布在 `main`**——先把 `dev` 合进 `main`,再在 **`main`** 上打 `v*` tag。tag 与分支无关,但 `backend-release` 有闸门:被打 tag 的提交若不在 `main` 上,发布直接拒。**推 tag = 触发发布到 nuget.org,不可逆**(nuget 只能 unlist,不能删)。

## 一、准备(可逆,在 `dev` 上做)

1. **定版 CHANGELOG**:把 `## Unreleased` 改成 `## X.Y.Z - YYYY-MM-DD`,按 Keep a Changelog 归类(Added / Fixed / Changed / Removed)。条目按**真实 diff** 写,别照提交标题猜——快速拉自上个 tag 以来、排除巡检 ledger 的提交:

   ```bash
   git log vX.Y.<上一个>..dev --oneline | grep -v "chore(review): patrol ledger"
   ```

2. **bump 前端版本**(后端不用改文件,tag 注入):
   - `web/package.json` 的 `version`
   - `web/package-lock.json` 两处:顶层 `version` + `packages[""].version`
   - 核对无残留:`grep -n '"version": "X.Y.<旧>"' web/package.json web/package-lock.json` 应为空。

3. **本地验绿**(CI 会再跑一遍,但先本地确认省得白打 tag;本机内存紧,**别并发**跑 vue-tsc 和 dotnet test,一次一个):

   ```bash
   cd web && npm test && npm run build              # 前端:单测 + 类型检查 + 打包
   dotnet test backend/TenonAdmin.slnx -c Release   # 后端:默认 SQLite(exit 0 即全绿)
   ```

4. 提交:`chore(release): prepare X.Y.Z — changelog + web version bump`。

## 二、发布(不可逆,合 `main` + 打 tag)

先确认 `main` 能干净快进(理想情况,`dev` 一路领先没分叉):

```bash
git fetch origin
git merge-base --is-ancestor origin/main dev && echo "可 ff" || echo "有分叉,需真合并"
```

然后:

```bash
# 1. 推 dev
git push origin dev

# 2. main 快进到 dev
git branch -f main origin/main    # 本地没有 main 时先建跟踪点
git checkout main
git merge --ff-only dev
git push origin main

# 3. 在 main 上打 tag 并推(← 这一步触发 backend-release 发布 nuget)
git tag vX.Y.Z
git push origin vX.Y.Z
```

`--ff-only` 失败,说明有人直接改了 `main`(它有 `dev` 没有的提交):先 `git merge dev` 解冲突,确认版本号/CHANGELOG 没被顶回旧值,再推。

## 三、发布后

- 盯 `backend-release`:`gh run watch` 或看 GitHub Actions 页。
- 它先跑 build + test + `dotnet new tenon-app` 冒烟,**全绿才推 nuget.org**。红了**不发包**——修完在 `dev` 上重来,删 tag 重打即可,包没出去,安全。
- 删错打的 tag(仅在 workflow 尚未成功发包前有意义):

  ```bash
  git tag -d vX.Y.Z
  git push origin :refs/tags/vX.Y.Z
  ```

- **核对 GitHub Release 说明文字**:`archive-openapi` job 用 `gh release create --generate-notes` 起草说明,这是 GitHub 按**已合并 PR** 拼的"What's Changed",不读 CHANGELOG.md——功能提交若是直接推 `dev`(这仓库常见),说明里就会漏掉,只剩 docs/CI 这类无关 PR。去 https://github.com/Tenon-Net/TenonAdmin/releases 核对一遍,对不上就用 CHANGELOG.md 对应版本段落替换掉:

  ```bash
  gh release edit vX.Y.Z --notes-file <(awk '/^## X\.Y\.Z - /{f=1;next} /^## /{f=0} f' CHANGELOG.md)
  ```

- 发布成功后,`web/package.json` 已是新版本,`dev` 继续开发,下一版从新的 `## Unreleased` 攒起。

> 发布节奏与闸门的**为什么**在 [CHANGELOG.md](../CHANGELOG.md) 顶部;本页是**怎么做**的操作清单。
