// Backend bilan yagona aloqa nuqtasi.
// Token localStorage da; 401 kelsa avtomatik chiqib ketadi.

const TOKEN_KEY = 'bigant.token'
const USER_KEY = 'bigant.user'

export type Role = 'Realizatsiya' | 'Perehod' | 'Admin'

export interface Me {
  id: number
  username: string
  fullName: string
  role: Role
  hasTelegram?: boolean
}

export class ApiError extends Error {
  constructor(public code: string, message: string, public status: number) {
    super(message)
  }
}

export const token = {
  get: () => localStorage.getItem(TOKEN_KEY),
  set: (t: string) => localStorage.setItem(TOKEN_KEY, t),
  clear: () => {
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem(USER_KEY)
  },
}

export const storedUser = {
  get: (): Me | null => {
    const raw = localStorage.getItem(USER_KEY)
    return raw ? (JSON.parse(raw) as Me) : null
  },
  set: (u: Me) => localStorage.setItem(USER_KEY, JSON.stringify(u)),
}

let onUnauthorized: (() => void) | null = null
export const setUnauthorizedHandler = (fn: () => void) => {
  onUnauthorized = fn
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = {}
  const t = token.get()
  if (t) headers['Authorization'] = `Bearer ${t}`
  if (body !== undefined) headers['Content-Type'] = 'application/json'

  // ── ALOQA ──
  // Server o'chiq yoki internet uzilgan bo'lsa `fetch` xato tashlaydi.
  // Ilgari bu holat ushlanmasdi va ekranga tushunarsiz yozuv chiqardi.
  let res: Response
  try {
    res = await fetch(path, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch {
    throw new ApiError('OFFLINE',
      'Server bilan aloqa yo\'q. Internetni tekshiring yoki bir ozdan keyin urining.', 0)
  }

  if (res.status === 401) {
    token.clear()
    onUnauthorized?.()
    throw new ApiError('UNAUTHORIZED', 'Sessiya tugadi — qayta kiring', 401)
  }

  if (res.status === 429) {
    throw new ApiError('RATE_LIMIT', 'Juda ko\'p urinish — bir daqiqadan keyin qayta urining', 429)
  }

  const text = await res.text()

  // Server HTML yoki bo'sh javob qaytarsa JSON.parse yiqilardi va
  // foydalanuvchi «Unexpected token» degan yozuvni ko'rardi.
  let data: { code?: string; message?: string } | null = null
  try { data = text ? JSON.parse(text) : null } catch { data = null }

  if (!res.ok) {
    // Backend {code, message} qaytaradi — o'sha matnni ko'rsatamiz.
    // Matn bo'lmasa RAQAM emas, tushunarli gap chiqadi: bugalter uchun
    // «500» hech narsa anglatmaydi, «serverda xato» anglatadi.
    throw new ApiError(data?.code ?? 'ERROR', data?.message ?? humanError(res.status), res.status)
  }

  return data as T
}

/**
 * Xato raqamini odam tiliga o'giradi.
 * Ekranda hech qachon «Xato 500» ko'rinmasligi kerak.
 */
function humanError(status: number): string {
  if (status >= 500) return 'Serverda xato yuz berdi. Bir ozdan keyin qayta urining.'
  if (status === 403) return 'Bu bo\'lim sizning ishingizga kirmaydi.'
  if (status === 404) return 'So\'ralgan ma\'lumot topilmadi.'
  if (status === 423) return 'Hisob vaqtincha bloklangan.'
  if (status >= 400) return 'So\'rov to\'g\'ri emas — ma\'lumotni tekshiring.'
  return 'Kutilmagan xato yuz berdi.'
}

export const api = {
  get: <T,>(path: string) => request<T>('GET', path),
  post: <T,>(path: string, body?: unknown) => request<T>('POST', path, body ?? {}),
  del: <T,>(path: string) => request<T>('DELETE', path),
}

// ── Domen turlari ─────────────────────────────────────────────
export type DealKind = 'Sale' | 'Purchase'
export type DealStatus = 'Draft' | 'Active' | 'Closed' | 'Cancelled'
export type StepStatus = 'Pending' | 'Done' | 'Skipped' | 'Failed'
export type DocType =
  | 'Contract' | 'InvoiceForPayment' | 'Doverennost' | 'Ttn' | 'Esf' | 'Act' | 'Reconciliation'

export interface DealRow {
  id: number
  number: string
  kind: DealKind
  status: DealStatus
  counterparty: string
  manager: string | null
  amountTotal: number
  amountPaid: number
  createdAt: string
  closedAt: string | null
  stepsDone: number
  stepsTotal: number
}

export interface DealStep {
  step: string
  label: string
  status: StepStatus
  isRequired: boolean
  dueDate: string | null
  completedAt: string | null
  documentId: number | null
  isOverdue: boolean
  note: string | null
}

export interface DealDoc {
  id: number
  type: DocType
  label: string
  direction: 'In' | 'Out'
  status: string
  docNumber: string | null
  docDate: string
  amount: number
  didoxId: string | null
  sentAt: string | null
  rejectReason: string | null
  /// Bu hujjat qaysi hujjat ASOSIDA chiqarilgan — «Doverennost №DOV-2026-00001»
  parentDocumentId: number | null
  basis: string | null
}

export interface DealCard {
  id: number
  number: string
  kind: DealKind
  status: DealStatus
  counterparty: { id: number; name: string; inn: string | null }
  manager: string | null
  amountTotal: number
  amountPaid: number

  ageDays: number
  contractDate: string | null
  dueDate: string | null
  createdAt: string
  closedAt: string | null
  comment: string | null
  steps: DealStep[]
  documents: DealDoc[]
  payments: { id: number; amount: number; payDate: string; bankDocNo: string | null; purpose: string | null }[]
}

export interface Summary {
  from: string
  to: string
  newDeals: number
  closedDeals: number
  openDeals: number
  openAmount: number
  receivableTotal: number
  payableTotal: number
  paidNotShipped: number
  shippedNotPaid: number
  overdueSteps: number
  unreadNotifications: number
  didoxEnabled: boolean
}

export interface OpenDeal {
  id: number
  number: string
  kind: string
  counterparty: string
  manager: string | null
  amount: number
  unpaid: number
  ageDays: number
  nextStep: string
  isOverdue: boolean
  flag: string
}

export interface PendingDocs {
  didoxEnabled: boolean
  signatureRequired: boolean
  count: number
  items: {
    id: number
    type: DocType
    docNumber: string | null
    docDate: string
    amount: number
    vatAmount: number
    counterparty: string | null
    deal: string | null
    dealId: number | null
  }[]
}

export interface InboxList {
  didoxEnabled: boolean
  count: number
  items: {
    id: number
    type: DocType
    docNumber: string | null
    docDate: string
    status: string
    sender: string | null
    deal: string | null
    dealId: number | null
    amount: number
    didoxId: string | null
    createdAt: string
  }[]
}

export interface Notif {
  id: number
  kind: string
  severity: 'Info' | 'Warning' | 'Critical'
  title: string
  body: string
  dealId: number | null
  documentId: number | null
  isRead: boolean
  createdAt: string
  /** Qaysi rol uchun. null — ikkala buxgalterga ham tegishli. */
  targetRole?: 'Realizatsiya' | 'Perehod' | 'Admin' | null
}

export interface Counterparty {
  id: number
  name: string
  inn: string | null
  isSupplier: boolean
  isClient: boolean
  paymentDays: number
  isBlocked: boolean
}

export interface Product {
  id: number
  article: string | null
  name: string
  brand: string | null
  unit: string
  basePrice: number
  isPriceFixed: boolean
  mxik: string | null
  vatRate: number
  group: string | null
}

export interface Readiness {
  company: { exists: boolean; inn: string | null; ready: boolean }
  didox: { enabled: boolean; baseUrl: string; hasToken: boolean; hasLogin: boolean }
  telegram: { enabled: boolean; connected: boolean }
  counterparties: { total: number; noInn: number; sample: { id: number; name: string }[] }
}

// ── Formatlash ────────────────────────────────────────────────
export const money = (n: number | null | undefined) =>
  n == null ? '—' : Math.round(n).toLocaleString('ru-RU').replace(/ /g, ' ')

export const dateUz = (iso: string | null | undefined) => {
  if (!iso) return '—'
  const d = new Date(iso)
  return d.toLocaleDateString('ru-RU', { day: '2-digit', month: '2-digit', year: 'numeric' })
}

export const dateTimeUz = (iso: string | null | undefined) => {
  if (!iso) return '—'
  // Server UTC beradi — brauzer foydalanuvchi vaqtiga o'giradi.
  return new Date(iso).toLocaleString('ru-RU', {
    day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit',
  })
}
