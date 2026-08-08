# StoryVoice 博客來書櫃 Companion

這個 Chrome／Chromium Manifest V3 extension 只會從**目前開啟、已載入的博客來電子書櫃頁面**讀取可見 metadata，並同步到本機 StoryVoice：

- 書籍識別碼
- 書名與作者
- 封面網址
- 博客來官方閱讀／商品連結

它不讀取或傳送博客來帳號、密碼、Cookie、localStorage、購買憑證或電子書內文，也不下載、解密或繞過 DRM。

## 安裝

1. 啟動 StoryVoice：
   ```bash
   docker compose up -d --build
   ```
2. 在 Chrome／Chromium 開啟 `chrome://extensions`。
3. 開啟「開發人員模式」，選擇「載入未封裝項目」。
4. 選擇本目錄：`extensions/books-com-tw-companion`。
5. 前往 [博客來電子書櫃](https://viewer-ebook.books.com.tw/viewer/index.html?readlist=all)，直接在博客來完成登入。
6. 先捲動或按「看更多」載入要同步的書，再點 extension 圖示、勾選書籍並同步。

預設只允許把資料送到 `http://localhost:3000` 或 `http://127.0.0.1:3000`，避免誤把私人書櫃 metadata 傳往其他主機。

## 驗證

```bash
npm run check
```

測試涵蓋官方來源 URL 限制、識別碼抽取、metadata 正規化與去重。`tests/shelf-fixture.html` 可用真實 Chromium 驗證 DOM adapter。

## MVP 邊界

- 只同步當前頁面已載入的項目，不呼叫博客來未公開 API。
- StoryVoice 中的連結書籍狀態是 `Linked`，沒有章節內文。
- 要進行故事解析與語音生成，仍須由使用者另外匯入合法取得、無 DRM 的 EPUB／TXT。
- 博客來若調整書櫃 DOM，可能需要更新 extractor；extension 會在找不到項目時明確提示，不會改用帳密或 Cookie 抓取。
