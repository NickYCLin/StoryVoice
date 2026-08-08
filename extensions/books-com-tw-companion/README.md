# StoryVoice 博客來書櫃 Companion

這個 Chrome／Chromium Manifest V3 extension 只會從**目前開啟的博客來電子書櫃頁面**讀取已呈現的 metadata，並同步到明確允許的 StoryVoice：

- 書籍識別碼
- 書名與作者
- 封面網址
- 博客來官方閱讀／商品連結
- 頁面明確標示的 EPUB 版型與博客來官方 TTS 狀態（未標示時保留未知）

它不讀取或傳送博客來帳號、密碼、Cookie、localStorage、購買憑證或電子書內文，也不下載、解密或繞過 DRM。

## 安裝

1. 啟動 StoryVoice：
   ```bash
   docker compose up -d --build
   ```
2. 登入 StoryVoice，在「進階：同步博客來書櫃書目」建立七天有效的連線金鑰。
3. 在 Chrome／Chromium 開啟 `chrome://extensions`。
4. 開啟「開發人員模式」，選擇「載入未封裝項目」。
5. 選擇本目錄：`extensions/books-com-tw-companion`。
6. 前往 [博客來電子書櫃](https://viewer-ebook.books.com.tw/viewer/index.html?readlist=all)，直接在博客來完成登入。
7. 點 extension 圖示後貼上金鑰，可掃描目前頁面，或明確按下「展開完整書櫃」讓 Companion 捲動／點擊書櫃的「看更多」。
8. 確認清單、勾選書籍，再同步到 StoryVoice。

只允許把資料送到 `http://localhost:3000`、`http://127.0.0.1:3000`，或正式上線後的
`https://aiprod.wrbtycg.tw/StoryVoice`。其他 host、port、path、HTTP 正式站、帳密 URL
與 query/hash 全部拒絕，避免誤把私人書櫃 metadata 傳往其他主機。

同步使用專用 Bearer 金鑰，不傳送 StoryVoice Cookie；金鑰只允許把書櫃 metadata 寫入簽發它的帳號。

## 驗證

```bash
npm run check
```

測試涵蓋官方來源 URL 限制、識別碼抽取、metadata 正規化、去重、完整書櫃合併與 500 本安全上限。`tests/shelf-fixture.html` 可用真實 Chromium 驗證 DOM adapter。

## MVP 邊界

- 「展開完整書櫃」只操作書櫃頁面上的捲動／看更多，最多 30 輪、500 本；不呼叫博客來未公開 API。
- 即使選擇完整掃描，也不會進入書籍閱讀器或讀取章節內文。
- API 每批最多接收 200 本；Companion 會在本機分批送出勾選項目。
- StoryVoice 中的連結書籍狀態是 `Linked`，沒有章節內文。
- 要進行故事解析與語音生成，仍須由使用者另外匯入合法取得、無 DRM 的 EPUB／TXT。
- 博客來若調整書櫃 DOM，可能需要更新 extractor；extension 會在找不到項目時明確提示，不會改用帳密或 Cookie 抓取。
