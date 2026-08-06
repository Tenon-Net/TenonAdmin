# 发布流程(Release Runbook)

TenonAdmin 前后端同仓、**同版本号一起发**。后端以 NuGet 包发布(版本由 tag 的 `-p:Version` 注入);前端版本号是登录页页脚的构建期常量(`web/package.json` 与 `web-react/package.json`,两套模板各一份);文档站顶栏徽章在 `site/.vitepress/config.ts`(中/英各一处)——三处必须一起 bump,否则用户在 UI / 文档站看到的版本和实际跑的包对不上。

节奏:**开发在 `dev`,发布在 `main`**——先把 `dev` 合进 `main`,再在 **`main`** 上打 `v*` tag。tag 与分支无关,但 `backend-release` 有闸门:被打 tag 的提交若不在 `main` 上,发布直接拒。**推 tag = 触发发布到 nuget.org,不可逆**(nuget 只能 unlist,不能删)。

## 一、准备(可逆,在 `dev` 上做)

1. **定版 CHANGELOG**:保留顶部空的 `## Unreleased`,把待发布条目移到紧随其后的 `## X.Y.Z - YYYY-MM-DD`,按 Keep a Changelog 归类(Added / Fixed / Changed / Removed)。条目按**真实 diff** 写,别照提交标题猜——快速拉自上个 tag 以来、排除巡检 ledger 的提交:

   ```powershell
   git log vX.Y.<上一个>..dev --oneline | Select-String -NotMatch 'chore\(review\): patrol ledger'
   ```

2. **bump 版本号**(后端不用改文件,tag 注入;前端两套模板 + **文档站导航徽章**一起改):
   - `web/package.json` 与 `web-react/package.json` 的 `version`
   - `web/package-lock.json` 与 `web-react/package-lock.json` 各两处:顶层 `version` + `packages[""].version`
   - **`site/.vitepress/config.ts`**：英文 nav 与中文（`zh`）nav **各一处**版本徽章 `{ text: 'X.Y.Z', link: 'https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md' }`（文档站顶栏显示的版本号;漏了会跟实际发布的对不上——0.3.0 发布时就漏过一次）。agent 流程见 `skills/tenon-release.md`（`/tenon-release`）。
   - 核对无残留（PowerShell）：`Select-String -Path web/package*.json,web-react/package*.json -Pattern '"version": "X.Y.<旧>"'` 与 `Select-String -Path site/.vitepress/config.ts -Pattern "'X.Y.<旧>'"` 都应为空；`Select-String -Path site/.vitepress/config.ts -Pattern "text: 'X.Y.Z'"` 应正好 2 处。

3. **本地验绿**(CI 会再跑一遍,但先本地确认省得白打 tag;本机内存紧,**别并发**跑 vue-tsc 和 dotnet test,一次一个)。CI 的 `web-ci.yml`/`web-react-ci.yml` 在 test/build 前还跑 `lint`,本地一起过;`web-react` 的 vitest 是串行配置(`pool: forks`),CI 里为此把单测拆成 3 个 `--shard` 分开跑——不分片直接 `npm test` 会把主进程堆撑爆 OOM(见该 workflow 里的注释),本地也要分片跑:

   ```powershell
   Push-Location web; try { npm run lint; if ($LASTEXITCODE -ne 0) { throw 'Vue lint failed' }; npm test; if ($LASTEXITCODE -ne 0) { throw 'Vue tests failed' }; npm run build; if ($LASTEXITCODE -ne 0) { throw 'Vue build failed' } } finally { Pop-Location }
   Push-Location web-react; try { npm run lint; if ($LASTEXITCODE -ne 0) { throw 'React lint failed' }; 1..3 | ForEach-Object { npx vitest run --shard=$_/3; if ($LASTEXITCODE -ne 0) { throw "React tests failed (shard $_/3)" } }; npm run build; if ($LASTEXITCODE -ne 0) { throw 'React build failed' } } finally { Pop-Location }
   dotnet test backend/TenonAdmin.slnx -c Release; if ($LASTEXITCODE -ne 0) { throw '.NET tests failed' }
   ```

4. 提交:`chore(release): prepare X.Y.Z — changelog + frontend/site version bumps`。

## 二、发布(不可逆,合 `main` + 打 tag)

先确认 `main` 能干净快进(理想情况,`dev` 一路领先没分叉):

```powershell
git fetch origin
git merge-base --is-ancestor origin/main origin/dev
if ($LASTEXITCODE -eq 0) { Write-Host '可 ff' } else { Write-Host '有分叉，需真合并' }
```

然后:

```powershell
# 1. 推 dev
git push origin dev

# 2. main 快进到 dev；不重置本地分支
git fetch origin
if (git show-ref --verify --quiet refs/heads/main) { git switch main } else { git switch --track -c main origin/main }
if ((git rev-parse main) -ne (git rev-parse origin/main)) { throw 'Local main differs from origin/main; inspect it before merging.' }
git merge --ff-only origin/dev
git push origin main

# 3. 在当前 origin/main tip 打 tag 并推(← 这一步触发 backend-release 发布 nuget)
git fetch origin
$head = git rev-parse HEAD; $main = git rev-parse main; $originMain = git rev-parse origin/main
if (($head -ne $main) -or ($head -ne $originMain)) { throw 'Tag target is not the current origin/main tip.' }
git tag vX.Y.Z
git push origin vX.Y.Z
```

`--ff-only` 失败,说明有人直接改了 `main`(它有 `dev` 没有的提交):先 `git merge dev` 解冲突,确认版本号/CHANGELOG 没被顶回旧值,再推。

## 三、发布后

- 盯 `backend-release`:获取这次 run ID 后执行 `gh run watch <run-id> --exit-status`,或看 GitHub Actions 页直到完成。
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

- **文档站**:合 `main` 那一步如果带上了 `site/**` 的改动(包括上面 bump 的导航版本徽章),`docs` workflow 会自动触发部署——获取这次 `docs.yml` run ID 后执行 `gh run watch <run-id> --exit-status`,确认 lint/build/deploy 三步绿。它只在 push `main` 命中 `site/**` 路径时跑,漏改了版本徽章之后单独补提交,记得也要单独确认这一步跑绿。
- 发布成功后,两个前端 `package.json` 与文档站导航徽章已是新版本,`dev` 继续开发,下一版从新的 `## Unreleased` 攒起。

> 发布节奏与闸门的**为什么**在 [CHANGELOG.md](../CHANGELOG.md) 顶部;本页是**怎么做**的操作清单。
