/* Company registration (UI-only stubs until the backend exists) */

export interface RegistrationData {
  companyName: string
  firstName: string
  lastName: string
  /** lower-case, no spaces */
  email: string
  /** 9 digits without +994, e.g. "501234567" */
  phone: string
  /** 4-digit PIN */
  pin: string
}

const STUB_DELAY_MS = 800

function wait(milliseconds: number): Promise<void> {
  return new Promise((resolve) => window.setTimeout(resolve, milliseconds))
}

/* Send code */
export async function sendCode(phone: string): Promise<void> {
  // TODO(backend): send a 6-digit SMS code to +994{phone}
  void phone
  await wait(STUB_DELAY_MS)
}

/* Resend code */
export async function resendCode(phone: string): Promise<void> {
  // TODO(backend): send a new SMS code to +994{phone}
  void phone
  await wait(STUB_DELAY_MS)
}

/* Verify code */
export async function verifyCode(phone: string, code: string): Promise<boolean> {
  // TODO(backend): check the code; return false when it is wrong or expired
  void phone
  void code
  await wait(STUB_DELAY_MS)
  return true
}

/* Register company */
export async function registerCompany(data: RegistrationData): Promise<void> {
  // TODO(backend): create the company and its first admin from the verified phone
  void data
  await wait(STUB_DELAY_MS)
}
