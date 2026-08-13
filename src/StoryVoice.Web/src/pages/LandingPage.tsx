import { Link } from 'react-router-dom'

import { apiUrl } from '../api'

type Feature = {
  eyebrow: string
  title: string
  description: string
  image: string
  imageAlt: string
}

const FEATURES: Feature[] = [
  {
    eyebrow: 'Your library',
    title: '整理你的故事書庫',
    description: '匯入 EPUB 或 TXT，自動解析章節；搜尋、篩選、排序，用裝置標籤整理你手邊正在追的作品。',
    image: '/landing/01-library.jpg',
    imageAlt: '書庫頁面，顯示已匯入的書籍與章節解析結果',
  },
  {
    eyebrow: 'AI narration',
    title: 'AI 朗讀與有聲書',
    description: '只處理你上傳或明確連結的合法正文，交給神經語音服務朗讀，音訊完成後私下保存在你的 StoryVoice 帳號。',
    image: '/landing/01b-library-reading.jpg',
    imageAlt: '章節閱讀畫面與 AI 朗讀功能入口',
  },
  {
    eyebrow: 'Character voice studio',
    title: '角色專屬聲線',
    description: '幫每個角色建立基礎聲線，再視劇情替緊張、開心、生氣、難過各自設計或克隆一組聲音，找不到的情境會自動退回基礎聲線。',
    image: '/landing/02b-character-voices.jpg',
    imageAlt: '角色管理頁面的聲線工作室，顯示基礎聲線與四種情境聲線',
  },
  {
    eyebrow: 'Series cast',
    title: '多角色系列配音',
    description: '同一系列固定旁白與角色聲線，逐章校正劇本後再整批建立 staged 音訊，任何一冊失敗都不會偷換目前版本。',
    image: '/landing/03-series-cast.jpg',
    imageAlt: '多角色系列配音控制台，顯示系列書籍與固定角色聲線',
  },
  {
    eyebrow: 'Collections',
    title: '把書整理成書冊',
    description: '書冊是單純的書本分類收藏，可以把系列作品排序收在一起，也能唯讀分享給其他 StoryVoice 使用者。',
    image: '/landing/04b-collections-list.jpg',
    imageAlt: '書冊列表頁面，顯示已建立的書冊卡片',
  },
]

export function LandingPage() {
  return (
    <main className="relative z-10 mx-auto max-w-7xl px-6 py-12 lg:px-10">
      <section className="overflow-hidden rounded-3xl border border-amber-200 bg-gradient-to-br from-amber-50 via-white to-orange-50 p-8 text-center sm:p-14">
        <p className="eyebrow">AI Story Director</p>
        <h1 className="mx-auto mt-4 max-w-3xl font-serif text-4xl leading-tight text-stone-900 sm:text-5xl">
          把你有權閱讀的故事，<span className="text-amber-700">變成一齣有聲演出。</span>
        </h1>
        <p className="mx-auto mt-5 max-w-2xl text-sm leading-7 text-stone-600 sm:text-base">
          StoryVoice 把電子書轉成多角色、具情緒演出的 AI 有聲書：整理書庫、替每個角色設計專屬聲線、
          逐章校正劇本，再交給系列配音一次產出整批音訊。
        </p>
        <div className="mt-8 flex flex-col items-center justify-center gap-3 sm:flex-row">
          <Link className="primary-button" to="/library">
            進入書庫開始
          </Link>
          <Link className="secondary-button" to="/characters">
            先逛逛角色聲線工作室
          </Link>
        </div>
      </section>

      <section className="mt-16 space-y-16">
        {FEATURES.map((feature, index) => (
          <div
            className={`grid grid-cols-1 items-center gap-8 lg:grid-cols-2 ${index % 2 === 1 ? 'lg:[&>*:first-child]:order-2' : ''}`}
            key={feature.title}
          >
            <div>
              <p className="eyebrow">{feature.eyebrow}</p>
              <h2 className="mt-3 font-serif text-2xl text-stone-900 sm:text-3xl">{feature.title}</h2>
              <p className="mt-4 text-sm leading-7 text-stone-600 sm:text-base">{feature.description}</p>
            </div>
            <div className="overflow-hidden rounded-2xl border border-stone-200 shadow-[0_20px_50px_rgba(96,70,30,.12)]">
              <img
                alt={feature.imageAlt}
                className="h-72 w-full object-cover object-top sm:h-80"
                loading="lazy"
                src={apiUrl(feature.image)}
              />
            </div>
          </div>
        ))}
      </section>

      <section className="mt-16 rounded-3xl border border-stone-200 bg-white p-8 text-center sm:p-12">
        <h2 className="font-serif text-2xl text-stone-900 sm:text-3xl">只處理你有權使用的內容</h2>
        <p className="mx-auto mt-3 max-w-2xl text-sm leading-7 text-stone-500">
          StoryVoice is open source. No DRM circumvention. Process only content you have the right to use.
        </p>
        <Link className="primary-button mt-6 inline-flex" to="/library">
          進入書庫
        </Link>
      </section>
    </main>
  )
}
