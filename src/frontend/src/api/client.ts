/** An error the API explained: the message is meant for people. */
export class ApiError extends Error {
  readonly status: number
  /** Stable reason the interface can react to, e.g. "not-a-repository". */
  readonly code: string | undefined

  constructor(status: number, message: string, code?: string) {
    super(message)
    this.status = status
    this.code = code
  }
}

interface ProblemDetails {
  title?: string
  detail?: string
  code?: string
}

export async function readError(response: Response): Promise<ApiError> {
  let problem: ProblemDetails = {}
  try {
    problem = (await response.json()) as ProblemDetails
  } catch {
    // Not a problem-details body: fall back to the status text.
  }
  return new ApiError(
    response.status,
    problem.detail ?? problem.title ?? response.statusText ?? 'Request failed',
    problem.code,
  )
}

export async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const response = await fetch(url, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!response.ok) {
    throw await readError(response)
  }
  if (response.status === 204 || response.headers.get('Content-Length') === '0') {
    return undefined as T
  }
  return (await response.json()) as T
}
