# TenonAdmin 发布新版本（tenon-release）

**仅用于 TenonAdmin（榫卯）本仓。** 前后端同仓、**同版本号一起发**。本 skill 是 agent 可执行的发布流程；操作细节与「为什么」见 [`docs/releasing.md`](../docs/releasing.md)（人类 runbook，环境侧真源）。

**节奏**：开发在 `dev`，发布在 `main`。先合 `dev` → `main`，再在 **`main` 上**打 `v*` tag。推 tag 触发 `backend-release`，全绿才推 nuget.org。**推 tag = 不可逆发包闸门已扣下**（nuget 只能 unlist）。

**本 skill 不代用户拍板版本号、不代推 tag。** 每到不可逆步，先陈述现状并请用户确认后再执行。

---

## 触发

用户说「TenonAdmin 发版 / tenon-release / 打 tag / 推 nuget / 准备 X.Y.Z」或 `/tenon-release` 时用本 skill。不要在普通 feature 提交流程里自动进入，也不要在其他仓库误用。

---

## 0. 定版

1. 确认发版源分支：默认 `dev`；若用户指定其他分支，设为 `$source`，下文都使用该值。同步并切到它（PowerShell）：

   ```powershell
   $source = 'dev' # 替换为用户确认的发版源分支
   git fetch origin --tags
   if (git show-ref --verify --quiet "refs/heads/$source") {
     git switch $source
   } else {
     git switch --track -c $source "origin/$source"
   }
   git pull --ff-only origin $source
   ```
2. 读当前：
   - 最新发布 tag：`git tag -l 'v*' --sort=-v:refname | Select-Object -First 5`
   - 前端：`web/package.json`、`web-react/package.json` 的 `version`
   - **文档站导航徽章**：`site/.vitepress/config.ts` 里中/英两处 `{ text: '…', link: '…/CHANGELOG.md' }`（当前应是上一发布版）
   - CHANGELOG：`## Unreleased` 下有无实质条目
3. 与用户确认目标版本 `X.Y.Z`（semver）与发布日 `YYYY-MM-DD`。

**完成标准**：用户明确说出目标版本号；本地在 `$source` 且工作区干净，或用户知情接受脏树。

---

## 1. 准备（可逆，在 `$source`）

### 1.1 CHANGELOG

保留顶部空的 `## Unreleased`，把待发布条目移到紧随其后的 `## X.Y.Z - YYYY-MM-DD`。条目按 **真实 diff** 写，按 Keep a Changelog 归类（Added / Fixed / Changed / Removed）。别照提交标题猜。

拉自上个 tag 以来、排除巡检 ledger 的提交：

```powershell
git log "v<上一版>..$source" --oneline | Select-String -NotMatch 'chore\(review\): patrol ledger'
```

**完成标准**：`CHANGELOG.md` 已有 `## X.Y.Z - ` 标题；`## Unreleased` 空段保留在顶部（给下一版攒）；用户看过条目无异议。

### 1.2 bump 版本号（前端 + 文档站，四处齐）

后端不用改文件版本，tag 的 `-p:Version` 注入。**前端两套模板 + 文档站导航徽章必须一起改到 `X.Y.Z`**——漏文档站会在站顶导航显示旧版（0.3.0 发版时漏过一次）。

| 位置 | 改什么 |
|------|--------|
| `web/package.json` | `version` |
| `web-react/package.json` | `version` |
| `web/package-lock.json` | 顶层 `version` + `packages[""].version` |
| `web-react/package-lock.json` | 同上 |
| **`site/.vitepress/config.ts`** | **英文 nav + 中文（`zh`）nav 各一处**版本徽章 `{ text: 'X.Y.Z', link: 'https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md' }`。搜 `text: 'P.Q.R'` 应正好两处，都改成新版 |

不要改文档正文里的历史版本号（如 getting-started 举例、pro-table 的 npm 包 pin）——那些不是发布徽章。

核对无残留（旧版 `P.Q.R`）：

```bash
# PowerShell
Select-String -Path web/package*.json,web-react/package*.json -Pattern '"version": "P.Q.R"'
Select-String -Path site/.vitepress/config.ts -Pattern "P\.Q\.R"
# 文档站徽章必须已是新版（期望 2 处）
Select-String -Path site/.vitepress/config.ts -Pattern "text: 'X.Y.Z'"
```

**完成标准**：两套前端 version、两处文档站徽章、CHANGELOG 标题三者一致；`config.ts` 无旧版 `text: 'P.Q.R'`。

### 1.3 本地验绿

CI 会再跑；本地先绿，避免白打 tag。本机内存紧时**串行**，不要并行 vue-tsc 与 `dotnet test`：

CI（`web-ci.yml`/`web-react-ci.yml`）在 test/build 前还跑 `lint`，本地也一起过一遍，别等推上去才红。`web-react` 的 vitest 是**串行配置**（`pool: forks`），CI 里为此把单测拆成 3 个 `--shard` 分开跑——直接跑不分片的 `npm test` 会把主进程堆撑爆 OOM（见 `web-react-ci.yml` 里的注释），本地验绿也要分片跑，不要图省事合成一条 `npm test`：

```powershell
Push-Location web
try {
  npm run lint
  if ($LASTEXITCODE -ne 0) { throw 'Vue lint failed' }
  npm test
  if ($LASTEXITCODE -ne 0) { throw 'Vue tests failed' }
  npm run build
  if ($LASTEXITCODE -ne 0) { throw 'Vue build failed' }
} finally { Pop-Location }

Push-Location web-react
try {
  npm run lint
  if ($LASTEXITCODE -ne 0) { throw 'React lint failed' }
  1..3 | ForEach-Object {
    npx vitest run --shard=$_/3
    if ($LASTEXITCODE -ne 0) { throw "React tests failed (shard $_/3)" }
  }
  npm run build
  if ($LASTEXITCODE -ne 0) { throw 'React build failed' }
} finally { Pop-Location }

dotnet test backend/TenonAdmin.slnx -c Release
if ($LASTEXITCODE -ne 0) { throw '.NET tests failed' }
```

**完成标准**：lint + 测试 + build（含 dotnet test）全部 exit 0。任一步红则停，修在 `$source`，不进入第二节。

### 1.4 提交准备提交

```text
chore(release): prepare X.Y.Z — changelog + frontend/site version bumps
```

**完成标准**：该提交在 `$source` 上，且 diff 含 `site/.vitepress/config.ts`；`git status` 干净。

---

## 2. 发布（不可逆：合 main + 打 tag）

### 2.1 推 dev 与快进检查

```powershell
git fetch origin
git push origin $source
git merge-base --is-ancestor origin/main "origin/$source"
if ($LASTEXITCODE -eq 0) { Write-Host '可 ff' } else { Write-Host '有分叉，需真合并' }
```

有分叉时：真合并 `origin/$source`，确认 CHANGELOG / 版本号没被顶回旧值，再继续。

### 2.2 合 main（需用户确认）

向用户复述将执行：

1. `main` 快进（或合并）到含准备提交的 `$source`
2. `git push origin main`

用户同意后再跑。此处绝不重置或强推本地 `main`；本地 `main` 与远端不一致时停下，查明未推送提交的归属后再继续。PowerShell：

```powershell
git fetch origin
if (git show-ref --verify --quiet refs/heads/main) {
  git switch main
} else {
  git switch --track -c main origin/main
}
if ((git rev-parse main) -ne (git rev-parse origin/main)) {
  throw 'Local main differs from origin/main; inspect it before merging.'
}
git merge --ff-only "origin/$source" # 有分叉时，用户确认后改为 git merge "origin/$source"
git push origin main
```

**完成标准**：`origin/main` 的 tip 含 `chore(release): prepare X.Y.Z`。

### 2.3 打 tag 并推（需用户二次确认）

再次 `git fetch origin`，并确认 `HEAD`、`main`、`origin/main` 是**同一 SHA**；只作为 `origin/main` 的祖先并不足够，可能会把 tag 打在过期提交上。

```powershell
git fetch origin
$head = git rev-parse HEAD
$main = git rev-parse main
$originMain = git rev-parse origin/main
if (($head -ne $main) -or ($head -ne $originMain)) {
  throw 'Tag target is not the current origin/main tip.'
}
git tag vX.Y.Z
git push origin vX.Y.Z
```

**完成标准**：`origin` 上存在 `vX.Y.Z`；获取对应 `backend-release` run ID 后可执行 `gh run watch <run-id> --exit-status`。

> 闸门：tag 必须在 `main` 上，否则 workflow 直接拒。推错 tag 且尚未发包成功时，可 `git tag -d vX.Y.Z` 与 `git push origin :refs/tags/vX.Y.Z` 撤回后重来。

---

## 3. 发布后

1. **盯 CI**：获取这次 `backend-release` 的 run ID 后执行 `gh run watch <run-id> --exit-status`，或在 Actions 页面等它完成。`verify`（build + test + template smoke + openapi）全绿才 `pack-push`。红了**不发包**——在 `$source` 修，删 tag 重打。
2. **GitHub Release 说明**：`gh release create --generate-notes` 只看已合并 PR，本仓常见「直推 dev」会漏功能。用 CHANGELOG 对应段替换：

   ```bash
   # bash / Git Bash；PowerShell 请先把段落抽到临时文件再 --notes-file
   gh release edit vX.Y.Z --notes-file <(awk '/^## X\.Y\.Z - /{f=1;next} /^## /{f=0} f' CHANGELOG.md)
   ```

3. **文档站**：准备提交若改了 `site/**`（**必须含导航徽章**），合 `main` 会触发 `docs.yml`。确认 lint/build/deploy 绿：  
   获取这次 `docs.yml` 的 run ID 后执行 `gh run watch <run-id> --exit-status`。  
   若徽章是事后补提，也要单独确认这一 run 绿。
4. 切回 `$source` 继续开发；下一版从新的 `## Unreleased` 攒。

**完成标准**：nuget.org 可见 `TenonAdmin` 的 `X.Y.Z`；Release 说明与 CHANGELOG 该节一致；两前端 version **与文档站导航徽章**均为 `X.Y.Z`；`docs.yml` 在带 site 改动时已绿。

---

## 禁区与提醒

- **不要在 `$source` 上打发布 tag 并推**（workflow 会拒，但浪费时间）。
- **不要只 bump 一个前端**；Vue / React / lockfile / **文档站中英徽章** 必须一起改（四处齐）。
- **不要漏 `site/.vitepress/config.ts`**——站顶版本号靠它，不是 `web/package.json`。
- **不要**在本地 `dotnet nuget push` 旁路 CI。
- 版本与「发了什么」的真源：tag + `CHANGELOG.md`，不是 GitHub Release 自动草稿。
- 更多闸门说明见 `docs/releasing.md` 与 `CHANGELOG.md` 文首。
