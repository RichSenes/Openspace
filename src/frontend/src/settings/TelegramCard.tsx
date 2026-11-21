import { useState, type FormEvent } from 'react'
import { Check, Plus, Search, Send, Trash2 } from 'lucide-react'
import {
  useAddTelegramBot,
  useFindTelegramChats,
  useRemoveTelegramBot,
  useSetTelegramChat,
  useSetTelegramTopic,
  useTelegram,
  useTestTelegramBot,
} from '../api/hooks'
import type { TelegramBotView, TelegramChatView } from '../api/types'
import { useToast } from '../ui/toastContext'
import { botName, botNameProblem, chatOf, groupsFirst, placeOf, telegramStep, telegramSummary, tokenProblem, topicOf } from './telegram'
import './connections.css'

/**
 * Telegram for routines, set up in three steps: a bot made with BotFather, the group the bots
 * write in, then each bot's topic and a test message. Tokens go to a file on this computer that
 * only the person can read, and never come back to the page.
 */
export function TelegramCard() {
  const telegram = useTelegram()
  const status = telegram.data
  const step = telegramStep(status)
  const [found, setFound] = useState<TelegramChatView[]>()
  const group = found?.find((c) => c.id === status?.chat)
  const bots = status?.bots ?? []

  return (
    <section className="settings-card" aria-labelledby="telegram">
      <h3 id="telegram">Telegram</h3>
      <p className="hint">
        Routines send what they make (the paper, the mail summary, proposals with their buttons) to a Telegram group of yours, each with a bot of its own
        and, if you like, in a topic of its own.
      </p>
      {telegram.error && <p className="error">{telegram.error.message}</p>}
      <p className="connection-status" role="status">
        {telegramSummary(status)}
      </p>
      <ol className="guide">
        <li className={bots.length > 0 ? 'done' : 'next'}>
          <span className="guide-mark" aria-hidden>
            {bots.length > 0 ? <Check size={14} /> : 1}
          </span>
          <div className="guide-body">
            <strong>Make a bot with BotFather</strong>
            <ol>
              <li>
                In Telegram, open{' '}
                <a href="https://t.me/BotFather" target="_blank" rel="noreferrer">
                  BotFather
                </a>{' '}
                and send <code className="mono">/newbot</code>.
              </li>
              <li>Give it a name and a username ending in “bot”; BotFather answers with its token.</li>
              <li>Paste the token here with a short name routines will know it by. One bot per routine keeps each one in its own topic.</li>
            </ol>
            <AddBot taken={bots.map((b) => b.name)} primary={bots.length === 0} />
          </div>
          <span className="sr-only">{bots.length > 0 ? 'Done' : 'To do'}</span>
        </li>

        <li className={status?.chat != null ? 'done' : step === 'group' ? 'next' : undefined}>
          <span className="guide-mark" aria-hidden>
            {status?.chat != null ? <Check size={14} /> : 2}
          </span>
          <div className="guide-body">
            <strong>Choose the group they write in</strong>
            <ol>
              <li>Make a group in Telegram, or use one you have. Turn on Topics in its settings to give each bot a topic.</li>
              <li>Add your bots to the group, then write any message in it.</li>
              <li>Find the group with one of your bots and choose it.</li>
            </ol>
            {status?.chat != null && <p>Routines write in group {group ? `“${group.title}” (${status.chat})` : status.chat}.</p>}
            <FindGroup bots={bots} current={status?.chat ?? null} found={found} onFound={setFound} primary={step === 'group'} />
          </div>
          <span className="sr-only">{status?.chat != null ? 'Done' : 'To do'}</span>
        </li>

        <li className={step === 'test' ? 'next' : undefined}>
          <span className="guide-mark" aria-hidden>
            3
          </span>
          <div className="guide-body">
            <strong>Give each bot its topic and test it</strong>
            {bots.length === 0 ? (
              <span className="hint">Your bots show up here once added.</span>
            ) : (
              <ul className="telegram-bots">
                {bots.map((b) => (
                  <Bot key={b.name} bot={b} group={group} canTest={status?.chat != null} />
                ))}
              </ul>
            )}
          </div>
        </li>
      </ol>
      {status && <p className="hint">Tokens are kept in {status.folder}/telegram.env, the group and topics in telegram.json next to it.</p>}
    </section>
  )
}

function AddBot({ taken, primary }: { taken: string[]; primary: boolean }) {
  const [name, setName] = useState('')
  const [token, setToken] = useState('')
  const [tried, setTried] = useState(false)
  const add = useAddTelegramBot()
  const toast = useToast()
  const nameProblem = botNameProblem(name, taken)
  const keyProblem = tokenProblem(token)

  const submit = (e: FormEvent) => {
    e.preventDefault()
    setTried(true)
    if (nameProblem || keyProblem) return
    add.mutate(
      { name: botName(name), token: token.trim(), topic: null },
      {
        onSuccess: ({ username }) => {
          toast(`${botName(name)} added: it is @${username} on Telegram`)
          setName('')
          setToken('')
          setTried(false)
        },
      },
    )
  }

  return (
    <form className="guide-form" onSubmit={submit} noValidate>
      <label className="field">
        <span>Name in Openspace</span>
        <input className="input" value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. PAPER" autoComplete="off" maxLength={32} />
        <span className={tried && nameProblem ? 'error' : 'hint'}>{tried && nameProblem ? nameProblem : 'Routines choose the bot by this name.'}</span>
      </label>
      <label className="field">
        <span>Token from BotFather</span>
        <input
          className="input mono"
          type="password"
          value={token}
          onChange={(e) => setToken(e.target.value)}
          autoComplete="off"
          spellCheck={false}
          placeholder="123456789:AA…"
        />
        <span className={tried && keyProblem ? 'error' : 'hint'}>
          {tried && keyProblem ? keyProblem : 'Checked with Telegram, then kept on this computer only. It is never shown again.'}
        </span>
      </label>
      <button type="submit" className={primary ? 'btn btn-primary' : 'btn'} disabled={add.isPending}>
        <Plus size={16} aria-hidden /> {add.isPending ? 'Checking the token…' : 'Add the bot'}
      </button>
      {add.error && <p className="error">{add.error.message}</p>}
    </form>
  )
}

function FindGroup({
  bots,
  current,
  found,
  onFound,
  primary,
}: {
  bots: TelegramBotView[]
  current: number | null
  found: TelegramChatView[] | undefined
  onFound: (chats: TelegramChatView[]) => void
  primary: boolean
}) {
  const [chosen, setChosen] = useState('')
  const [typed, setTyped] = useState('')
  const find = useFindTelegramChats()
  const setChat = useSetTelegramChat()
  const toast = useToast()
  const bot = bots.some((b) => b.name === chosen) ? chosen : (bots[0]?.name ?? '')
  const number = chatOf(typed)

  const use = (chat: number, title?: string) => setChat.mutate(chat, { onSuccess: () => toast(`Routines will write in ${title ? `“${title}”` : `group ${chat}`}`) })

  return (
    <>
      <div className="guide-form">
        {bots.length > 1 && (
          <label className="field">
            <span>Look with</span>
            <select className="input" value={bot} onChange={(e) => setChosen(e.target.value)}>
              {bots.map((b) => (
                <option key={b.name} value={b.name}>
                  {b.name}
                </option>
              ))}
            </select>
          </label>
        )}
        <button
          type="button"
          className={primary ? 'btn btn-primary' : 'btn'}
          disabled={!bot || find.isPending}
          onClick={() => find.mutate(bot, { onSuccess: (chats) => onFound(groupsFirst(chats)) })}
        >
          <Search size={16} aria-hidden /> {find.isPending ? 'Looking…' : `Find the group with ${bot || 'a bot'}`}
        </button>
      </div>
      {!bot && <span className="hint">Add a bot first.</span>}
      {find.error && <p className="error">{find.error.message}</p>}
      {found && found.length === 0 && (
        <span className="hint">
          {bot} has not seen any chat yet. Add it to the group, write a message there (mention it with @ if it does not read the group), then look again.
        </span>
      )}
      {found && found.length > 0 && (
        <ul className="telegram-chats">
          {found.map((c) => (
            <li key={c.id} className="telegram-bot-head">
              <span>
                <strong>{c.title}</strong> <span className="hint">{c.kind === 'private' ? 'your own chat with the bot' : c.kind}, {c.id}</span>
              </span>
              {c.id === current ? (
                <span className="hint">Chosen</span>
              ) : (
                <button type="button" className="btn" disabled={setChat.isPending} onClick={() => use(c.id, c.title)}>
                  Use this {c.kind === 'private' ? 'chat' : 'group'}
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      <form
        className="guide-form"
        onSubmit={(e) => {
          e.preventDefault()
          if (number !== undefined) use(number)
        }}
      >
        <label className="field">
          <span>Or type the group&apos;s number</span>
          <input className="input mono" value={typed} onChange={(e) => setTyped(e.target.value)} inputMode="numeric" placeholder="-100…" />
          <span className={typed && number === undefined ? 'error' : 'hint'}>
            {typed && number === undefined ? 'A whole number, negative for groups.' : 'Groups have negative numbers, such as -1001234567890.'}
          </span>
        </label>
        <button type="submit" className="btn btn-quiet" disabled={number === undefined || setChat.isPending}>
          Use this number
        </button>
      </form>
      {setChat.error && <p className="error">{setChat.error.message}</p>}
    </>
  )
}

function Bot({ bot, group, canTest }: { bot: TelegramBotView; group: TelegramChatView | undefined; canTest: boolean }) {
  const [topic, setTopic] = useState(bot.topic === null ? '' : String(bot.topic))
  const [removing, setRemoving] = useState(false)
  const saveTopic = useSetTelegramTopic()
  const test = useTestTelegramBot()
  const remove = useRemoveTelegramBot()
  const toast = useToast()
  const typed = topicOf(topic)
  const changed = typed !== undefined && typed !== bot.topic
  const topics = group?.topics ?? []

  return (
    <li>
      <div className="telegram-bot-head">
        <span>
          <strong>{bot.name}</strong> <span className="hint">writes in {placeOf(bot.topic, group)}</span>
        </span>
        {removing ? (
          <span className="row">
            <button type="button" className="btn btn-quiet" onClick={() => setRemoving(false)}>
              Keep it
            </button>
            <button
              type="button"
              className="btn btn-danger"
              disabled={remove.isPending}
              onClick={() => remove.mutate(bot.name, { onSuccess: () => toast(`${bot.name} removed`) })}
            >
              <Trash2 size={16} aria-hidden /> Remove: its routines stop writing
            </button>
          </span>
        ) : (
          <span className="row">
            <button
              type="button"
              className="btn"
              disabled={!canTest || test.isPending}
              onClick={() => test.mutate(bot.name, { onSuccess: () => toast(`Test message sent with ${bot.name}`) })}
            >
              <Send size={16} aria-hidden /> {test.isPending ? 'Sending…' : 'Send a test message'}
            </button>
            <button type="button" className="btn btn-quiet" onClick={() => setRemoving(true)}>
              <Trash2 size={16} aria-hidden /> Remove…
            </button>
          </span>
        )}
      </div>
      {!canTest && <span className="hint">Choose the group first to send a test.</span>}
      {test.error && <p className="error">{test.error.message}</p>}
      <form
        className="guide-form"
        onSubmit={(e) => {
          e.preventDefault()
          if (changed) saveTopic.mutate({ name: bot.name, topic: typed }, { onSuccess: () => toast(`${bot.name}'s topic saved`) })
        }}
      >
        <label className="field">
          <span>Topic</span>
          {topics.length > 0 ? (
            <select className="input" value={topic} onChange={(e) => setTopic(e.target.value)}>
              <option value="">The main chat of the group</option>
              {topics.map((t) => (
                <option key={t.id} value={String(t.id)}>
                  {t.name}
                </option>
              ))}
              {bot.topic !== null && !topics.some((t) => t.id === bot.topic) && <option value={String(bot.topic)}>Topic {bot.topic}</option>}
            </select>
          ) : (
            <input className="input mono" value={topic} onChange={(e) => setTopic(e.target.value)} inputMode="numeric" placeholder="Empty: the main chat" />
          )}
          <span className={typed === undefined ? 'error' : 'hint'}>
            {typed === undefined
              ? 'A whole number, or empty for the main chat.'
              : topics.length > 0
                ? 'Topics seen in the group by the bot.'
                : 'The number at the end of a topic’s link; find the group again to choose topics by name.'}
          </span>
        </label>
        <button type="submit" className="btn btn-quiet" disabled={!changed || saveTopic.isPending}>
          Save the topic
        </button>
      </form>
      {(saveTopic.error ?? remove.error) && <p className="error">{(saveTopic.error ?? remove.error)?.message}</p>}
    </li>
  )
}
